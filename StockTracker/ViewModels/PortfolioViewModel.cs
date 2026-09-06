using Microsoft.Win32;
using Newtonsoft.Json;
using StockTracker.Models;
using StockTracker.Services;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows;

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
        public string GroupName { get; private set; } = "未分類";
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

        public void Refresh(StockViewModel stock, RankedStock rankedStock, decimal totalAssets, double positionLimit, decimal availableToBuy, double targetWeight, string groupName)
        {
            Name = rankedStock?.Name ?? stock?.Name ?? "尚未訂閱／無資料";
            GroupName = string.IsNullOrWhiteSpace(groupName) ? "未分類" : groupName;
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
            foreach (var property in new[] { nameof(Name), nameof(GroupName), nameof(LatestPrice), nameof(MarketValue), nameof(Weight), nameof(Score), nameof(Risk), nameof(ScoreRiskText), nameof(ProfitPercentage), nameof(TodayChangePercentage), nameof(Recommendation), nameof(Guidance), nameof(RecommendationBrush), nameof(TargetWeight), nameof(SuggestedTradeAmount) }) OnPropertyChanged(property);
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

    public sealed class PortfolioTradeViewModel : ViewModelBase
    {
        public PortfolioTradeViewModel(PortfolioTrade trade) { Trade = trade; }
        public PortfolioTrade Trade { get; }
        public DateTime Date => Trade.Date;
        public string TypeName => string.Equals(Trade.Type, "Sell", StringComparison.OrdinalIgnoreCase) ? "賣出" : "買入";
        public string Symbol => Trade.Symbol;
        public int Quantity => Trade.Quantity;
        public decimal Price => Trade.Price;
        public decimal RealizedProfitLoss => Trade.RealizedProfitLoss;
    }

    public sealed class PortfolioRealizedAdjustmentViewModel : ViewModelBase
    {
        public PortfolioRealizedAdjustmentViewModel(PortfolioRealizedAdjustment adjustment) { Adjustment = adjustment; }
        public PortfolioRealizedAdjustment Adjustment { get; }
        public DateTime Date => Adjustment.Date;
        public decimal Amount => Adjustment.Amount;
        public string Note => Adjustment.Note;
    }

    public sealed class PortfolioViewModel : ViewModelBase
    {
        private readonly ObservableCollection<StockViewModel> _stocks;
        private readonly MainWindowViewModel _mainViewModel;
        private readonly StockGroupCatalog _stockGroupCatalog = new StockGroupCatalog();
        private readonly string _filePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "StockTracker", "portfolio.json");
        private PortfolioSettings _settings = new PortfolioSettings();
        private string _symbolInput;
        private string _quantityInput;
        private string _averageCostInput;
        private string _statusMessage;
        private DateTime? _cashFlowDate = DateTime.Today;
        private string _cashFlowType = "Deposit";
        private string _cashFlowAmountInput;
        private DateTime? _tradeDate = DateTime.Today;
        private string _tradeType = "Buy";
        private string _tradeSymbolInput;
        private string _tradeQuantityInput;
        private string _tradePriceInput;
        private string _tradeFeeInput;
        private string _tradeTaxInput;
        private DateTime? _adjustmentDate = DateTime.Today;
        private string _adjustmentAmountInput;
        private string _adjustmentNoteInput;

        public PortfolioViewModel(ObservableCollection<StockViewModel> stocks, MainWindowViewModel mainViewModel = null)
        {
            _stocks = stocks ?? new ObservableCollection<StockViewModel>();
            _mainViewModel = mainViewModel;
            Holdings = new ObservableCollection<PortfolioHoldingViewModel>();
            CashFlows = new ObservableCollection<PortfolioCashFlowViewModel>();
            Trades = new ObservableCollection<PortfolioTradeViewModel>();
            RealizedAdjustments = new ObservableCollection<PortfolioRealizedAdjustmentViewModel>();
            AddHoldingCommand = new RelayCommand(_ => AddHolding());
            RemoveHoldingCommand = new RelayCommand(item => RemoveHolding(item as PortfolioHoldingViewModel));
            ImportCsvCommand = new RelayCommand(_ => ImportCsv());
            ExportCsvCommand = new RelayCommand(_ => ExportCsv());
            AddCashFlowCommand = new RelayCommand(_ => AddCashFlow());
            RemoveCashFlowCommand = new RelayCommand(item => RemoveCashFlow(item as PortfolioCashFlowViewModel));
            AddTradeCommand = new RelayCommand(_ => AddTrade());
            AddRealizedAdjustmentCommand = new RelayCommand(_ => AddRealizedAdjustment());
            RefreshCommand = new RelayCommand(_ => Refresh());
            SaveCommand = new RelayCommand(_ => Save());
            ResetAllRecordsCommand = new RelayCommand(_ => ResetAllRecords());
            Load();
            if (_mainViewModel != null) _mainViewModel.MarketScanUpdated += MainViewModelOnMarketScanUpdated;
        }

        public ObservableCollection<PortfolioHoldingViewModel> Holdings { get; }
        public ObservableCollection<PortfolioCashFlowViewModel> CashFlows { get; }
        public ObservableCollection<PortfolioTradeViewModel> Trades { get; }
        public ObservableCollection<PortfolioRealizedAdjustmentViewModel> RealizedAdjustments { get; }
        public ICommand AddHoldingCommand { get; }
        public ICommand RemoveHoldingCommand { get; }
        public ICommand ImportCsvCommand { get; }
        public ICommand ExportCsvCommand { get; }
        public ICommand AddCashFlowCommand { get; }
        public ICommand RemoveCashFlowCommand { get; }
        public ICommand AddTradeCommand { get; }
        public ICommand AddRealizedAdjustmentCommand { get; }
        public ICommand RefreshCommand { get; }
        public ICommand SaveCommand { get; }
        public ICommand ResetAllRecordsCommand { get; }
        public decimal Cash => Math.Max(0m, NetInvested + TradeCashMovement);
        public double CashReservePercentage { get => _settings.CashReservePercentage; set { _settings.CashReservePercentage = Math.Max(0, Math.Min(100, value)); OnPropertyChanged(); Refresh(); } }
        public double SinglePositionLimitPercentage { get => _settings.SinglePositionLimitPercentage; set { _settings.SinglePositionLimitPercentage = Math.Max(1, Math.Min(100, value)); OnPropertyChanged(); Refresh(); } }
        public string SymbolInput { get => _symbolInput; set { _symbolInput = value; OnPropertyChanged(); } }
        public string QuantityInput { get => _quantityInput; set { _quantityInput = value; OnPropertyChanged(); } }
        public string AverageCostInput { get => _averageCostInput; set { _averageCostInput = value; OnPropertyChanged(); } }
        public DateTime? CashFlowDate { get => _cashFlowDate; set { _cashFlowDate = value; OnPropertyChanged(); } }
        public string CashFlowType { get => _cashFlowType; set { _cashFlowType = value; OnPropertyChanged(); } }
        public string CashFlowAmountInput { get => _cashFlowAmountInput; set { _cashFlowAmountInput = value; OnPropertyChanged(); } }
        public DateTime? TradeDate { get => _tradeDate; set { _tradeDate = value; OnPropertyChanged(); } }
        public string TradeType { get => _tradeType; set { _tradeType = value; OnPropertyChanged(); } }
        public string TradeSymbolInput { get => _tradeSymbolInput; set { _tradeSymbolInput = value; OnPropertyChanged(); } }
        public string TradeQuantityInput { get => _tradeQuantityInput; set { _tradeQuantityInput = value; OnPropertyChanged(); } }
        public string TradePriceInput { get => _tradePriceInput; set { _tradePriceInput = value; OnPropertyChanged(); } }
        public string TradeFeeInput { get => _tradeFeeInput; set { _tradeFeeInput = value; OnPropertyChanged(); } }
        public string TradeTaxInput { get => _tradeTaxInput; set { _tradeTaxInput = value; OnPropertyChanged(); } }
        public DateTime? AdjustmentDate { get => _adjustmentDate; set { _adjustmentDate = value; OnPropertyChanged(); } }
        public string AdjustmentAmountInput { get => _adjustmentAmountInput; set { _adjustmentAmountInput = value; OnPropertyChanged(); } }
        public string AdjustmentNoteInput { get => _adjustmentNoteInput; set { _adjustmentNoteInput = value; OnPropertyChanged(); } }
        public string StatusMessage { get => _statusMessage; private set { _statusMessage = value; OnPropertyChanged(); } }
        public decimal StockMarketValue => Holdings.Sum(x => x.MarketValue);
        public decimal TotalAssets => StockMarketValue + Cash;
        public double CashRatio => TotalAssets == 0 ? 0 : (double)(Cash / TotalAssets * 100m);
        public double StockHoldingRatio => TotalAssets == 0 ? 0 : (double)(StockMarketValue / TotalAssets * 100m);
        public decimal NetInvested => (_settings.CashFlows ?? new List<PortfolioCashFlow>()).Sum(x => x.Amount);
        public decimal TradeCashMovement => (_settings.Trades ?? new List<PortfolioTrade>()).Sum(trade =>
            string.Equals(trade.Type, "Sell", StringComparison.OrdinalIgnoreCase)
                ? trade.Price * trade.Quantity - trade.Fee - trade.Tax
                : -(trade.Price * trade.Quantity + trade.Fee + trade.Tax));
        public decimal CumulativeProfitLoss => TotalAssets - NetInvested;
        public double CumulativeReturnPercentage => NetInvested == 0 ? 0 : (double)(CumulativeProfitLoss / NetInvested * 100m);
        public decimal TransactionRealizedProfitLoss => (_settings.Trades ?? new List<PortfolioTrade>()).Sum(x => x.RealizedProfitLoss);
        public decimal HistoricalRealizedProfitLoss => (_settings.RealizedAdjustments ?? new List<PortfolioRealizedAdjustment>()).Sum(x => x.Amount);
        public decimal RealizedProfitLoss => TransactionRealizedProfitLoss + HistoricalRealizedProfitLoss;
        public decimal UnrealizedProfitLoss => Holdings.Sum(x => x.MarketValue - x.Quantity * x.AverageCost);
        public string ConcentrationRiskSummary
        {
            get
            {
                var largest = Holdings.OrderByDescending(x => x.Weight).FirstOrDefault();
                var group = Holdings.GroupBy(x => x.GroupName).OrderByDescending(x => x.Sum(y => y.Weight)).FirstOrDefault();
                if (largest == null) return "尚未建立持股。";
                var positionWarning = largest.Weight > SinglePositionLimitPercentage ? $"單一持股 {largest.Symbol} 占 {largest.Weight:F1}% 超過上限。" : $"最大持股 {largest.Symbol} 占 {largest.Weight:F1}%。";
                var groupWarning = group == null ? string.Empty : $" 最大族群「{group.Key}」占 {group.Sum(x => x.Weight):F1}%。";
                return positionWarning + groupWarning;
            }
        }
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
            _settings.Trades = (_settings.Trades ?? new List<PortfolioTrade>()).Where(trade => trade != null).ToList();
            _settings.RealizedAdjustments = (_settings.RealizedAdjustments ?? new List<PortfolioRealizedAdjustment>()).Where(adjustment => adjustment != null).ToList();
            // 將舊版可手動輸入的現金餘額轉為一筆期初入金，往後完全由資金與交易紀錄推算。
            if (_settings.Cash != 0m)
            {
                var legacyOpeningCash = _settings.Cash - _settings.CashFlows.Sum(x => x.Amount) - TradeCashMovement;
                if (legacyOpeningCash != 0m)
                    _settings.CashFlows.Add(new PortfolioCashFlow { Date = DateTime.Today, Amount = legacyOpeningCash });
                _settings.Cash = 0m;
            }
            foreach (var holding in _settings.Holdings) Holdings.Add(new PortfolioHoldingViewModel(holding));
            foreach (var cashFlow in _settings.CashFlows.OrderByDescending(x => x.Date)) CashFlows.Add(new PortfolioCashFlowViewModel(cashFlow));
            foreach (var trade in _settings.Trades.OrderByDescending(x => x.Date)) Trades.Add(new PortfolioTradeViewModel(trade));
            foreach (var adjustment in _settings.RealizedAdjustments.OrderByDescending(x => x.Date)) RealizedAdjustments.Add(new PortfolioRealizedAdjustmentViewModel(adjustment));
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
            SymbolInput = QuantityInput = AverageCostInput = string.Empty; StatusMessage = "持股庫存已更新；實際買賣請用下方交易紀錄，剩餘資金才會自動結算。"; Save();
        }

        private void RemoveHolding(PortfolioHoldingViewModel holding)
        {
            if (holding == null) return;
            _settings.Holdings.Remove(holding.Holding);
            Holdings.Remove(holding);
            StatusMessage = "持股紀錄已移除；若為實際賣出，請改用交易紀錄登錄，系統才會回補現金。";
            Save();
        }

        private void ResetAllRecords()
        {
            var confirmation = MessageBox.Show(
                "這會清除所有持股、入出金、買賣交易與已實現損益紀錄，且無法復原。\n\n確定要重設嗎？",
                "重設所有投資組合紀錄",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning,
                MessageBoxResult.No);
            if (confirmation != MessageBoxResult.Yes) return;

            _settings.Holdings.Clear();
            _settings.CashFlows.Clear();
            _settings.Trades.Clear();
            _settings.RealizedAdjustments.Clear();
            _settings.Cash = 0m;
            Holdings.Clear();
            CashFlows.Clear();
            Trades.Clear();
            RealizedAdjustments.Clear();
            Save();
            StatusMessage = "所有投資組合紀錄已重設。";
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

        private void AddTrade()
        {
            var symbol = (TradeSymbolInput ?? string.Empty).Trim();
            if (!TradeDate.HasValue || !System.Text.RegularExpressions.Regex.IsMatch(symbol, "^\\d{4,6}$")
                || !int.TryParse(TradeQuantityInput, out var quantity) || quantity <= 0
                || !decimal.TryParse(TradePriceInput, NumberStyles.Number, CultureInfo.CurrentCulture, out var price) || price <= 0
                || !decimal.TryParse(string.IsNullOrWhiteSpace(TradeFeeInput) ? "0" : TradeFeeInput, NumberStyles.Number, CultureInfo.CurrentCulture, out var fee) || fee < 0
                || !decimal.TryParse(string.IsNullOrWhiteSpace(TradeTaxInput) ? "0" : TradeTaxInput, NumberStyles.Number, CultureInfo.CurrentCulture, out var tax) || tax < 0)
            {
                StatusMessage = "請填寫有效的交易日期、代號、股數、成交價、手續費與交易稅。";
                return;
            }

            var isSell = string.Equals(TradeType, "Sell", StringComparison.OrdinalIgnoreCase);
            var holding = _settings.Holdings.FirstOrDefault(x => x.Symbol == symbol);
            if (isSell && (holding == null || holding.Quantity < quantity))
            {
                StatusMessage = "賣出股數不得超過目前持有股數。";
                return;
            }

            var realized = 0m;
            if (isSell)
            {
                realized = (price - holding.AverageCost) * quantity - fee - tax;
                holding.Quantity -= quantity;
                if (holding.Quantity == 0) _settings.Holdings.Remove(holding);
            }
            else
            {
                var grossCost = price * quantity + fee + tax;
                if (Cash < grossCost) { StatusMessage = "可用現金不足以支付本次買入交易。"; return; }
                if (holding == null)
                {
                    holding = new PortfolioHolding { Symbol = symbol, Quantity = 0, AverageCost = 0 };
                    _settings.Holdings.Add(holding);
                }
                holding.AverageCost = (holding.AverageCost * holding.Quantity + grossCost) / (holding.Quantity + quantity);
                holding.Quantity += quantity;
            }

            var trade = new PortfolioTrade { Date = TradeDate.Value.Date, Type = isSell ? "Sell" : "Buy", Symbol = symbol, Quantity = quantity, Price = price, Fee = fee, Tax = tax, CostBasisPerShare = isSell ? holding?.AverageCost ?? 0 : 0, RealizedProfitLoss = realized };
            _settings.Trades.Add(trade);
            Trades.Insert(0, new PortfolioTradeViewModel(trade));
            Holdings.Clear();
            foreach (var item in _settings.Holdings) Holdings.Add(new PortfolioHoldingViewModel(item));
            TradeSymbolInput = TradeQuantityInput = TradePriceInput = TradeFeeInput = TradeTaxInput = string.Empty;
            StatusMessage = isSell ? $"已記錄賣出，已實現損益 {realized:N0}。" : "已記錄買入交易。";
            Save();
        }

        private void AddRealizedAdjustment()
        {
            if (!AdjustmentDate.HasValue || !decimal.TryParse(AdjustmentAmountInput, NumberStyles.Number, CultureInfo.CurrentCulture, out var amount))
            {
                StatusMessage = "請填寫有效的日期與已實現損益金額。";
                return;
            }

            var adjustment = new PortfolioRealizedAdjustment { Date = AdjustmentDate.Value.Date, Amount = amount, Note = (AdjustmentNoteInput ?? string.Empty).Trim() };
            _settings.RealizedAdjustments.Add(adjustment);
            RealizedAdjustments.Insert(0, new PortfolioRealizedAdjustmentViewModel(adjustment));
            AdjustmentAmountInput = AdjustmentNoteInput = string.Empty;
            StatusMessage = "已新增歷史已實現損益調整。";
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
                var groups = _stockGroupCatalog.GetGroups(holding.Symbol);
                holding.Refresh(_stocks.FirstOrDefault(s => s.Symbol == holding.Symbol), rankedStock, total, SinglePositionLimitPercentage, available, targets.TryGetValue(holding, out var target) ? target : 0, groups.FirstOrDefault());
            }
            OnPropertyChanged(nameof(StockMarketValue)); OnPropertyChanged(nameof(TotalAssets)); OnPropertyChanged(nameof(CashRatio)); OnPropertyChanged(nameof(StockHoldingRatio));
            OnPropertyChanged(nameof(Cash)); OnPropertyChanged(nameof(NetInvested)); OnPropertyChanged(nameof(TradeCashMovement)); OnPropertyChanged(nameof(CumulativeProfitLoss)); OnPropertyChanged(nameof(CumulativeReturnPercentage));
            OnPropertyChanged(nameof(TransactionRealizedProfitLoss)); OnPropertyChanged(nameof(HistoricalRealizedProfitLoss)); OnPropertyChanged(nameof(RealizedProfitLoss)); OnPropertyChanged(nameof(UnrealizedProfitLoss));
            OnPropertyChanged(nameof(ConcentrationRiskSummary));
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
