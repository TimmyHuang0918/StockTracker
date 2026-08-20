using Microsoft.Win32;
using Newtonsoft.Json;
using StockTracker.Models;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows.Input;
using System.Windows.Media;

namespace StockTracker.ViewModels
{
    public sealed class PortfolioHoldingViewModel : ViewModelBase
    {
        public PortfolioHoldingViewModel(PortfolioHolding holding) { Holding = holding; }
        public PortfolioHolding Holding { get; }
        public string Symbol => Holding.Symbol;
        public int Quantity => Holding.Quantity;
        public decimal AverageCost => Holding.AverageCost;
        public string Name { get; private set; } = "尚未訂閱／無資料";
        public decimal LatestPrice { get; private set; }
        public decimal MarketValue => LatestPrice * Quantity;
        public double Weight { get; private set; }
        public int Score { get; private set; }
        public int Risk { get; private set; }
        public string ScoreRiskText => $"{Score} / {Risk}";
        public double ProfitPercentage { get; private set; }
        public decimal TodayChangePercentage { get; private set; }
        public string Recommendation { get; private set; } = "等待資料";
        public string Guidance { get; private set; } = "等待資料";
        public Brush RecommendationBrush { get; private set; } = Brushes.Gray;
        public double TargetWeight { get; private set; }
        public decimal SuggestedTradeAmount { get; private set; }

        public void Refresh(StockViewModel stock, RankedStock rankedStock, decimal totalAssets, double positionLimit, decimal availableToBuy, double targetWeight)
        {
            Name = rankedStock?.Name ?? stock?.Name ?? "尚未訂閱／無資料";
            LatestPrice = rankedStock?.LatestPrice ?? stock?.LatestPrice ?? 0;
            Score = rankedStock?.Score ?? stock?.StrategyOutput?.FinalScore ?? 0;
            Risk = rankedStock?.CrashRiskScore ?? stock?.CurrentCrashRiskScore ?? 0;
            ProfitPercentage = LatestPrice > 0 && AverageCost > 0 ? (double)((LatestPrice / AverageCost - 1m) * 100m) : 0;
            TodayChangePercentage = rankedStock?.ChangePercent ?? stock?.ChangePercent ?? 0;
            Weight = totalAssets == 0 ? 0 : (double)(MarketValue / totalAssets * 100m);
            TargetWeight = Math.Max(0, Math.Min(positionLimit, targetWeight));
            var targetValue = totalAssets * (decimal)(TargetWeight / 100d);
            SuggestedTradeAmount = targetValue - MarketValue;
            if (stock == null && rankedStock == null || LatestPrice <= 0) { Recommendation = "等待掃描資料"; RecommendationBrush = Brushes.Gray; SuggestedTradeAmount = 0; Guidance = "找不到最新全市場掃描資料；完成掃描後會自動更新。"; }
            else if (TargetWeight <= 0) { Recommendation = "減碼／檢討"; RecommendationBrush = Brushes.IndianRed; Guidance = $"分數 {Score}、風險 {Risk} 未達納入動態配置的門檻；不新增部位，參考調整至 0%。"; }
            else if (Weight > positionLimit) { Recommendation = "減碼至上限"; RecommendationBrush = Brushes.IndianRed; TargetWeight = positionLimit; SuggestedTradeAmount = totalAssets * (decimal)(TargetWeight / 100d) - MarketValue; Guidance = $"目前權重 {Weight:F1}% 超過 {positionLimit:F1}% 上限；參考減少約 {Math.Floor(Math.Abs(SuggestedTradeAmount) / LatestPrice):N0} 股。"; }
            else if (Weight + 0.5 < TargetWeight && availableToBuy > 0) { Recommendation = "分批加碼"; RecommendationBrush = Brushes.SeaGreen; SuggestedTradeAmount = Math.Min(SuggestedTradeAmount, availableToBuy); Guidance = $"分數 {Score}、風險 {Risk}，依合格持股的相對配置分數分配至 {TargetWeight:F1}%；參考分批買入約 {Math.Floor(SuggestedTradeAmount / LatestPrice):N0} 股。"; }
            else { Recommendation = "暫不交易"; RecommendationBrush = Brushes.DarkOrange; Guidance = $"目前權重 {Weight:F1}% 接近 {TargetWeight:F1}% 目標；等待下一次評分或價格更新再檢視。"; }
            foreach (var property in new[] { nameof(Name), nameof(LatestPrice), nameof(MarketValue), nameof(Weight), nameof(Score), nameof(Risk), nameof(ScoreRiskText), nameof(ProfitPercentage), nameof(TodayChangePercentage), nameof(Recommendation), nameof(Guidance), nameof(RecommendationBrush), nameof(TargetWeight), nameof(SuggestedTradeAmount) }) OnPropertyChanged(property);
        }
    }

    public sealed class PortfolioCashFlowViewModel : ViewModelBase
    {
        public PortfolioCashFlowViewModel(PortfolioCashFlow cashFlow) { CashFlow = cashFlow; }
        public PortfolioCashFlow CashFlow { get; }
        public DateTime Date => CashFlow.Date;
        public decimal Amount => CashFlow.Amount;
        public string TypeName => Amount >= 0 ? "入金" : "出金";
    }

    public sealed class PortfolioViewModel : ViewModelBase
    {
        private readonly ObservableCollection<StockViewModel> _stocks;
        private readonly MainWindowViewModel _mainViewModel;
        private readonly string _filePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "StockTracker", "portfolio.json");
        private PortfolioSettings _settings = new PortfolioSettings();
        private string _symbolInput;
        private string _quantityInput;
        private string _averageCostInput;
        private string _statusMessage;
        private DateTime? _cashFlowDate = DateTime.Today;
        private string _cashFlowType = "Deposit";
        private string _cashFlowAmountInput;

        public PortfolioViewModel(ObservableCollection<StockViewModel> stocks, MainWindowViewModel mainViewModel = null)
        {
            _stocks = stocks ?? new ObservableCollection<StockViewModel>();
            _mainViewModel = mainViewModel;
            Holdings = new ObservableCollection<PortfolioHoldingViewModel>();
            CashFlows = new ObservableCollection<PortfolioCashFlowViewModel>();
            AddHoldingCommand = new RelayCommand(_ => AddHolding());
            RemoveHoldingCommand = new RelayCommand(item => RemoveHolding(item as PortfolioHoldingViewModel));
            ImportCsvCommand = new RelayCommand(_ => ImportCsv());
            ExportCsvCommand = new RelayCommand(_ => ExportCsv());
            AddCashFlowCommand = new RelayCommand(_ => AddCashFlow());
            RemoveCashFlowCommand = new RelayCommand(item => RemoveCashFlow(item as PortfolioCashFlowViewModel));
            RefreshCommand = new RelayCommand(_ => Refresh());
            SaveCommand = new RelayCommand(_ => Save());
            Load();
            if (_mainViewModel != null) _mainViewModel.MarketScanUpdated += MainViewModelOnMarketScanUpdated;
        }

        public ObservableCollection<PortfolioHoldingViewModel> Holdings { get; }
        public ObservableCollection<PortfolioCashFlowViewModel> CashFlows { get; }
        public ICommand AddHoldingCommand { get; }
        public ICommand RemoveHoldingCommand { get; }
        public ICommand ImportCsvCommand { get; }
        public ICommand ExportCsvCommand { get; }
        public ICommand AddCashFlowCommand { get; }
        public ICommand RemoveCashFlowCommand { get; }
        public ICommand RefreshCommand { get; }
        public ICommand SaveCommand { get; }
        public decimal Cash { get => _settings.Cash; set { _settings.Cash = Math.Max(0, value); OnPropertyChanged(); Refresh(); } }
        public double CashReservePercentage { get => _settings.CashReservePercentage; set { _settings.CashReservePercentage = Math.Max(0, Math.Min(100, value)); OnPropertyChanged(); Refresh(); } }
        public double SinglePositionLimitPercentage { get => _settings.SinglePositionLimitPercentage; set { _settings.SinglePositionLimitPercentage = Math.Max(1, Math.Min(100, value)); OnPropertyChanged(); Refresh(); } }
        public string SymbolInput { get => _symbolInput; set { _symbolInput = value; OnPropertyChanged(); } }
        public string QuantityInput { get => _quantityInput; set { _quantityInput = value; OnPropertyChanged(); } }
        public string AverageCostInput { get => _averageCostInput; set { _averageCostInput = value; OnPropertyChanged(); } }
        public DateTime? CashFlowDate { get => _cashFlowDate; set { _cashFlowDate = value; OnPropertyChanged(); } }
        public string CashFlowType { get => _cashFlowType; set { _cashFlowType = value; OnPropertyChanged(); } }
        public string CashFlowAmountInput { get => _cashFlowAmountInput; set { _cashFlowAmountInput = value; OnPropertyChanged(); } }
        public string StatusMessage { get => _statusMessage; private set { _statusMessage = value; OnPropertyChanged(); } }
        public decimal StockMarketValue => Holdings.Sum(x => x.MarketValue);
        public decimal TotalAssets => StockMarketValue + Cash;
        public double CashRatio => TotalAssets == 0 ? 0 : (double)(Cash / TotalAssets * 100m);
        public double StockHoldingRatio => TotalAssets == 0 ? 0 : (double)(StockMarketValue / TotalAssets * 100m);
        public decimal NetInvested => (_settings.CashFlows ?? new List<PortfolioCashFlow>()).Sum(x => x.Amount);
        public decimal CumulativeProfitLoss => TotalAssets - NetInvested;
        public double CumulativeReturnPercentage => NetInvested == 0 ? 0 : (double)(CumulativeProfitLoss / NetInvested * 100m);
        public StockViewModel FindStock(string symbol) => _stocks.FirstOrDefault(stock => stock.Symbol == symbol);

        public void Dispose()
        {
            if (_mainViewModel != null) _mainViewModel.MarketScanUpdated -= MainViewModelOnMarketScanUpdated;
        }

        private void MainViewModelOnMarketScanUpdated(object sender, EventArgs eventArgs) => Refresh();

        private void Load()
        {
            try
            {
                if (File.Exists(_filePath)) _settings = JsonConvert.DeserializeObject<PortfolioSettings>(File.ReadAllText(_filePath)) ?? new PortfolioSettings();
            }
            catch { StatusMessage = "無法讀取既有投資組合，已使用空白資料。"; _settings = new PortfolioSettings(); }
            // Older or manually edited files can omit these arrays (or contain null entries).
            // Normalize them before any subsequent add/remove/refresh operation.
            _settings.Holdings = (_settings.Holdings ?? new List<PortfolioHolding>())
                .Where(holding => holding != null)
                .ToList();
            _settings.CashFlows = (_settings.CashFlows ?? new List<PortfolioCashFlow>())
                .Where(cashFlow => cashFlow != null)
                .ToList();
            foreach (var holding in _settings.Holdings) Holdings.Add(new PortfolioHoldingViewModel(holding));
            foreach (var cashFlow in _settings.CashFlows.OrderByDescending(x => x.Date)) CashFlows.Add(new PortfolioCashFlowViewModel(cashFlow));
            OnPropertyChanged(nameof(Cash)); OnPropertyChanged(nameof(CashReservePercentage)); OnPropertyChanged(nameof(SinglePositionLimitPercentage));
            Refresh();
        }

        private void AddHolding()
        {
            var symbol = (SymbolInput ?? string.Empty).Trim();
            if (!System.Text.RegularExpressions.Regex.IsMatch(symbol, "^\\d{4,6}$") || !int.TryParse(QuantityInput, out var quantity) || quantity <= 0 || !decimal.TryParse(AverageCostInput, NumberStyles.Number, CultureInfo.CurrentCulture, out var cost) || cost < 0)
            { StatusMessage = "請輸入有效的股票代號、股數與平均成本。"; return; }
            var existing = _settings.Holdings.FirstOrDefault(x => x.Symbol == symbol);
            if (existing != null) { existing.Quantity = quantity; existing.AverageCost = cost; var vm = Holdings.First(x => x.Holding == existing); Holdings.Remove(vm); }
            else { existing = new PortfolioHolding { Symbol = symbol, Quantity = quantity, AverageCost = cost }; _settings.Holdings.Add(existing); }
            Holdings.Add(new PortfolioHoldingViewModel(existing));
            SymbolInput = QuantityInput = AverageCostInput = string.Empty; StatusMessage = "持股已新增／更新。"; Save();
        }

        private void RemoveHolding(PortfolioHoldingViewModel holding)
        {
            if (holding == null) return;
            _settings.Holdings.Remove(holding.Holding); Holdings.Remove(holding); StatusMessage = "持股已移除。"; Save();
        }

        private void AddCashFlow()
        {
            if (!CashFlowDate.HasValue || !decimal.TryParse(CashFlowAmountInput, NumberStyles.Number, CultureInfo.CurrentCulture, out var amount) || amount <= 0)
            {
                StatusMessage = "請填寫資金日期與大於 0 的金額。";
                return;
            }

            var cashFlow = new PortfolioCashFlow
            {
                Date = CashFlowDate.Value.Date,
                Amount = string.Equals(CashFlowType, "Withdrawal", StringComparison.OrdinalIgnoreCase) ? -amount : amount
            };
            _settings.CashFlows.Add(cashFlow);
            CashFlows.Insert(0, new PortfolioCashFlowViewModel(cashFlow));
            CashFlowAmountInput = string.Empty;
            StatusMessage = "已新增資金異動紀錄。";
            Save();
        }

        private void RemoveCashFlow(PortfolioCashFlowViewModel cashFlow)
        {
            if (cashFlow == null) return;
            _settings.CashFlows.Remove(cashFlow.CashFlow);
            CashFlows.Remove(cashFlow);
            StatusMessage = "已移除資金異動紀錄。";
            Save();
        }

        private void ImportCsv()
        {
            var dialog = new OpenFileDialog { Filter = "CSV 檔案 (*.csv)|*.csv|所有檔案 (*.*)|*.*" };
            if (dialog.ShowDialog() != true) return;
            try
            {
                var lines = File.ReadAllLines(dialog.FileName).Where(x => !string.IsNullOrWhiteSpace(x)).ToArray();
                var header = ParseCsvRow(lines.FirstOrDefault()?.TrimStart('\uFEFF') ?? string.Empty).Select(x => x.Trim().ToLowerInvariant()).ToArray();
                var s = Array.IndexOf(header, "symbol"); var q = Array.IndexOf(header, "quantity"); var c = Array.IndexOf(header, "averagecost");
                var type = Array.IndexOf(header, "recordtype"); var date = Array.IndexOf(header, "date"); var amount = Array.IndexOf(header, "amount");
                if (s < 0 || q < 0 || c < 0) throw new InvalidDataException("CSV 欄位需為 symbol,quantity,averageCost。");
                var importedHoldings = 0;
                var importedCashFlows = 0;
                foreach (var line in lines.Skip(1))
                {
                    var values = ParseCsvRow(line);
                    var recordType = type >= 0 && values.Count > type ? values[type].Trim().ToLowerInvariant() : "holding";
                    if (recordType == "cash_flow")
                    {
                        if (date < 0 || amount < 0 || values.Count <= Math.Max(date, amount)
                            || !DateTime.TryParseExact(values[date].Trim(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var cashFlowDate)
                            || !decimal.TryParse(values[amount].Trim(), NumberStyles.Number, CultureInfo.InvariantCulture, out var cashFlowAmount)) continue;
                        if (_settings.CashFlows.Any(x => x.Date.Date == cashFlowDate.Date && x.Amount == cashFlowAmount)) continue;
                        var cashFlow = new PortfolioCashFlow { Date = cashFlowDate.Date, Amount = cashFlowAmount };
                        _settings.CashFlows.Add(cashFlow);
                        CashFlows.Insert(0, new PortfolioCashFlowViewModel(cashFlow));
                        importedCashFlows++;
                        continue;
                    }
                    if (recordType != "holding" || values.Count <= Math.Max(s, Math.Max(q, c))) continue;
                    var symbol = values[s].Trim().ToUpperInvariant();
                    if (!System.Text.RegularExpressions.Regex.IsMatch(symbol, "^\\d{4,6}$")
                        || !int.TryParse(values[q].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var quantity) || quantity <= 0
                        || !decimal.TryParse(values[c].Trim(), NumberStyles.Number, CultureInfo.InvariantCulture, out var averageCost) || averageCost < 0) continue;
                    var existing = _settings.Holdings.FirstOrDefault(x => x.Symbol == symbol);
                    if (existing != null)
                    {
                        existing.Quantity = quantity;
                        existing.AverageCost = averageCost;
                        var existingViewModel = Holdings.FirstOrDefault(x => x.Holding == existing);
                        if (existingViewModel != null) Holdings.Remove(existingViewModel);
                    }
                    else
                    {
                        existing = new PortfolioHolding { Symbol = symbol, Quantity = quantity, AverageCost = averageCost };
                        _settings.Holdings.Add(existing);
                    }
                    Holdings.Add(new PortfolioHoldingViewModel(existing));
                    importedHoldings++;
                }
                StatusMessage = $"已匯入 {Path.GetFileName(dialog.FileName)}：{importedHoldings} 筆持股、{importedCashFlows} 筆資金異動。";
                Save();
            }
            catch (Exception ex) { StatusMessage = $"匯入失敗：{ex.Message}"; }
        }

        private static List<string> ParseCsvRow(string line)
        {
            var values = new List<string>();
            var value = new System.Text.StringBuilder();
            var quoted = false;
            for (var index = 0; index < line.Length; index++)
            {
                var character = line[index];
                if (character == '"')
                {
                    if (quoted && index + 1 < line.Length && line[index + 1] == '"') { value.Append('"'); index++; }
                    else quoted = !quoted;
                }
                else if (character == ',' && !quoted) { values.Add(value.ToString()); value.Clear(); }
                else value.Append(character);
            }
            values.Add(value.ToString());
            return values;
        }

        private void ExportCsv()
        {
            var dialog = new SaveFileDialog { Filter = "CSV 檔案 (*.csv)|*.csv", FileName = $"portfolio-{DateTime.Today:yyyyMMdd}.csv" };
            if (dialog.ShowDialog() != true) return;
            try
            {
                var rows = new List<string> { "recordType,date,amount,symbol,quantity,averageCost" };
                rows.AddRange((_settings.CashFlows ?? new List<PortfolioCashFlow>()).OrderBy(x => x.Date).Select(x => $"cash_flow,{x.Date:yyyy-MM-dd},{x.Amount.ToString(CultureInfo.InvariantCulture)},,,"));
                rows.AddRange(_settings.Holdings.Select(x => $"holding,,,{x.Symbol},{x.Quantity},{x.AverageCost.ToString(CultureInfo.InvariantCulture)}"));
                rows.Add($"summary,,{TotalAssets.ToString(CultureInfo.InvariantCulture)},,,");
                rows.Add($"net_invested,,{NetInvested.ToString(CultureInfo.InvariantCulture)},,,");
                rows.Add($"cumulative_profit_loss,,{CumulativeProfitLoss.ToString(CultureInfo.InvariantCulture)},,,");
                rows.Add($"cumulative_return_percentage,,{CumulativeReturnPercentage.ToString(CultureInfo.InvariantCulture)},,,");
                File.WriteAllText(dialog.FileName, string.Join(Environment.NewLine, rows), new System.Text.UTF8Encoding(true));
                StatusMessage = $"已匯出 CSV：{Path.GetFileName(dialog.FileName)}";
            }
            catch (Exception ex) { StatusMessage = $"匯出 CSV 失敗：{ex.Message}"; }
        }

        private void Save()
        {
            try { Directory.CreateDirectory(Path.GetDirectoryName(_filePath)); File.WriteAllText(_filePath, JsonConvert.SerializeObject(_settings, Formatting.Indented)); StatusMessage = "已儲存至本機。"; }
            catch (Exception ex) { StatusMessage = $"儲存失敗：{ex.Message}"; }
            Refresh();
        }

        private void Refresh()
        {
            var stockValue = Holdings.Sum(x => { var rankedStock = _mainViewModel?.FindLatestMarketScanStock(x.Symbol); var stock = _stocks.FirstOrDefault(s => s.Symbol == x.Symbol); return (rankedStock?.LatestPrice ?? stock?.LatestPrice ?? 0) * x.Quantity; });
            var total = stockValue + Cash;
            var available = Math.Max(0, Cash - total * (decimal)(CashReservePercentage / 100d));
            var targets = CalculateDynamicTargets();
            foreach (var holding in Holdings)
            {
                var rankedStock = _mainViewModel?.FindLatestMarketScanStock(holding.Symbol);
                holding.Refresh(_stocks.FirstOrDefault(s => s.Symbol == holding.Symbol), rankedStock, total, SinglePositionLimitPercentage, available, targets.TryGetValue(holding, out var target) ? target : 0);
            }
            OnPropertyChanged(nameof(StockMarketValue)); OnPropertyChanged(nameof(TotalAssets)); OnPropertyChanged(nameof(CashRatio)); OnPropertyChanged(nameof(StockHoldingRatio));
            OnPropertyChanged(nameof(NetInvested)); OnPropertyChanged(nameof(CumulativeProfitLoss)); OnPropertyChanged(nameof(CumulativeReturnPercentage));
        }

        private Dictionary<PortfolioHoldingViewModel, double> CalculateDynamicTargets()
        {
            var targets = Holdings.ToDictionary(holding => holding, _ => 0d);
            var active = Holdings
                .Select(holding => new { Holding = holding, Stock = _stocks.FirstOrDefault(stock => stock.Symbol == holding.Symbol), RankedStock = _mainViewModel?.FindLatestMarketScanStock(holding.Symbol) })
                .Select(item => new
                {
                    item.Holding,
                    Price = item.RankedStock?.LatestPrice ?? item.Stock?.LatestPrice ?? 0,
                    Score = item.RankedStock?.Score ?? item.Stock?.StrategyOutput?.FinalScore ?? 0,
                    Risk = item.RankedStock?.CrashRiskScore ?? item.Stock?.CurrentCrashRiskScore ?? 100
                })
                .Where(item => item.Price > 0 && item.Score >= 45 && item.Risk <= 70)
                .Select(item => new
                {
                    item.Holding,
                    Signal = item.Score * Math.Max(0.1, 1d - item.Risk / 100d),
                    Cap = Math.Min(SinglePositionLimitPercentage,
                        item.Score >= 75 && item.Risk <= 35 ? 30d :
                        item.Score >= 65 && item.Risk <= 45 ? 25d :
                        item.Score >= 55 && item.Risk <= 60 ? 15d : 8d)
                })
                .ToList();
            var remainingBudget = Math.Max(0, 100d - CashReservePercentage);

            while (active.Count > 0 && remainingBudget > 0.0001)
            {
                var totalSignal = active.Sum(item => item.Signal);
                var capped = active.Where(item => remainingBudget * item.Signal / totalSignal > item.Cap).ToList();
                if (capped.Count == 0)
                {
                    foreach (var item in active) targets[item.Holding] = remainingBudget * item.Signal / totalSignal;
                    break;
                }

                foreach (var item in capped) { targets[item.Holding] = item.Cap; remainingBudget -= item.Cap; active.Remove(item); }
            }

            return targets;
        }
    }
}
