using StockTracker.Models;
using StockTracker.Services;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Input;

namespace StockTracker.ViewModels
{
    /// <summary>
    /// Builds an editable next-session plan from local technical data. This class
    /// deliberately has no broker, order, or account-execution dependencies.
    /// </summary>
    public class TradePlanViewModel : ViewModelBase
    {
        private readonly StockViewModel _stock;
        private readonly List<CandleData> _candles;
        private string _selectedStrategy;
        private decimal _entryLower;
        private decimal _entryUpper;
        private decimal _stopLoss;
        private decimal _targetOne;
        private decimal _targetTwo;
        private decimal _riskBudget;
        private string _notes;
        private string _cancelCondition;
        private string _statusText;
        private bool _isApplyingDefaults;

        public TradePlanViewModel(StockViewModel stock)
        {
            _stock = stock ?? throw new ArgumentNullException(nameof(stock));
            _candles = (_stock.GetPublicCandles() ?? new List<CandleData>())
                .Where(item => item != null && item.Close > 0)
                .OrderBy(item => item.Time)
                .ToList();

            StrategyOptions = new ObservableCollection<string> { "突破買進", "拉回買進" };
            ApplyStrategyCommand = new RelayCommand(_ => ApplyStrategy());
            SavePlanCommand = new RelayCommand(_ => SavePlan());
            CopyPlanCommand = new RelayCommand(_ => CopyPlan());

            _selectedStrategy = StrategyOptions[0];
            ApplyStrategy();
        }

        public string Symbol => _stock.Symbol;
        public string Name => _stock.Name;
        public decimal LatestPrice => _stock.LatestPrice;
        public int Score => _stock.CurrentOpportunityScore;
        public int RiskScore => _stock.CurrentCrashRiskScore;
        public int ForeignSensitivity => _stock.ForeignSensitivity;
        public int TrustSensitivity => _stock.TrustSensitivity;
        public string InstitutionalLeadership => _stock.InstitutionalLeadershipLabel;
        public double MA5 => _stock.MA5;
        public double MA20 => _stock.MA20;
        public DateTime ValidUntil => GetNextWeekday(DateTime.Today);
        public string ValidUntilText => ValidUntil.ToString("yyyy/MM/dd（ddd）");
        public ObservableCollection<string> StrategyOptions { get; }

        public string SelectedStrategy
        {
            get => _selectedStrategy;
            set
            {
                var normalized = string.IsNullOrWhiteSpace(value) ? StrategyOptions[0] : value;
                if (_selectedStrategy == normalized) return;
                _selectedStrategy = normalized;
                OnPropertyChanged();
                ApplyStrategy();
            }
        }

        public decimal EntryLower
        {
            get => _entryLower;
            set
            {
                _entryLower = value;
                OnPropertyChanged();
                if (!_isApplyingDefaults) RecalculateTargetsAndSizing();
            }
        }

        public decimal EntryUpper
        {
            get => _entryUpper;
            set { _entryUpper = value; OnPropertyChanged(); }
        }

        public decimal StopLoss
        {
            get => _stopLoss;
            set
            {
                _stopLoss = value;
                OnPropertyChanged();
                if (!_isApplyingDefaults) RecalculateTargetsAndSizing();
            }
        }

        public decimal TargetOne
        {
            get => _targetOne;
            set { _targetOne = value; OnPropertyChanged(); OnPropertyChanged(nameof(RewardRiskOneText)); }
        }

        public decimal TargetTwo
        {
            get => _targetTwo;
            set { _targetTwo = value; OnPropertyChanged(); OnPropertyChanged(nameof(RewardRiskTwoText)); }
        }

        public decimal RiskBudget
        {
            get => _riskBudget;
            set { _riskBudget = Math.Max(0m, value); OnPropertyChanged(); OnPropertyChanged(nameof(SuggestedSharesText)); }
        }

        public string CancelCondition
        {
            get => _cancelCondition;
            private set { _cancelCondition = value ?? string.Empty; OnPropertyChanged(); }
        }

        public string Notes
        {
            get => _notes;
            set { _notes = value ?? string.Empty; OnPropertyChanged(); }
        }

        public string StatusText
        {
            get => _statusText;
            private set { _statusText = value ?? string.Empty; OnPropertyChanged(); }
        }

        public decimal RiskPerShare => Math.Max(0m, EntryLower - StopLoss);
        public string RiskPercentText => EntryLower <= 0 || StopLoss <= 0
            ? "—"
            : ((EntryLower - StopLoss) / EntryLower * 100m).ToString("0.00") + "%";
        public string RewardRiskOneText => FormatRewardRisk(TargetOne);
        public string RewardRiskTwoText => FormatRewardRisk(TargetTwo);
        public int SuggestedShares => RiskBudget <= 0 || RiskPerShare <= 0 ? 0 : (int)Math.Floor(RiskBudget / RiskPerShare);
        public string SuggestedSharesText => RiskBudget <= 0
            ? "請輸入單筆最大可承受損失"
            : SuggestedShares <= 0
                ? "風險金額不足以買進一股"
                : $"{SuggestedShares:N0} 股（約 {SuggestedShares / 1000d:F2} 張）";
        public string QualitySummary => $"分數 {Score}／風險 {RiskScore}；{InstitutionalLeadership}，外資 {ForeignSensitivity}／投信 {TrustSensitivity}";
        public ICommand ApplyStrategyCommand { get; }
        public ICommand SavePlanCommand { get; }
        public ICommand CopyPlanCommand { get; }

        private void ApplyStrategy()
        {
            var price = LatestPrice > 0 ? LatestPrice : _candles.LastOrDefault()?.Close ?? 0m;
            if (price <= 0)
            {
                StatusText = "尚無有效價格，無法建立交易計畫。";
                return;
            }

            var latest = _candles.LastOrDefault();
            var recentCandles = _candles.Skip(Math.Max(0, _candles.Count - 3)).ToList();
            var recentLow = recentCandles.Where(item => item.Low > 0).Select(item => item.Low).DefaultIfEmpty(price * 0.96m).Min();
            var latestHigh = latest?.High > 0 ? latest.High : price;

            _isApplyingDefaults = true;
            if (SelectedStrategy == "拉回買進")
            {
                var support = MA5 > 0 ? (decimal)MA5 : price * 0.99m;
                EntryLower = RoundToTick(support * 0.995m, false);
                EntryUpper = RoundToTick(support * 1.005m, true);
                StopLoss = BuildStop(EntryLower, recentLow);
                CancelCondition = "價格跌破停損價或 MA20 支撐時，取消買進；若反彈直接高於買入區上緣，等待下一次拉回，不追價。";
            }
            else
            {
                EntryLower = RoundToTick(Math.Max(price * 1.001m, latestHigh * 1.001m), true);
                EntryUpper = RoundToTick(EntryLower * 1.008m, true);
                StopLoss = BuildStop(EntryLower, recentLow);
                CancelCondition = "僅在突破買入區時考慮；若開盤或盤中直接高於買入區上緣，或回落跌破今日低點，取消買進、不追價。";
            }
            _isApplyingDefaults = false;

            RecalculateTargetsAndSizing();
            StatusText = "此計畫僅供你手動判斷、儲存與複製備忘；不會連接券商或送出委託。";
        }

        private decimal BuildStop(decimal entry, decimal recentLow)
        {
            var structureStop = recentLow * 0.995m;
            var hardStop = entry * 0.94m;
            var stop = Math.Max(structureStop, hardStop);
            if (stop >= entry) stop = hardStop;
            return RoundToTick(stop, false);
        }

        private void RecalculateTargetsAndSizing()
        {
            var risk = RiskPerShare;
            if (risk > 0)
            {
                _targetOne = RoundToTick(EntryLower + risk * 1.5m, true);
                _targetTwo = RoundToTick(EntryLower + risk * 2.5m, true);
                OnPropertyChanged(nameof(TargetOne));
                OnPropertyChanged(nameof(TargetTwo));
            }

            OnPropertyChanged(nameof(RiskPerShare));
            OnPropertyChanged(nameof(RiskPercentText));
            OnPropertyChanged(nameof(RewardRiskOneText));
            OnPropertyChanged(nameof(RewardRiskTwoText));
            OnPropertyChanged(nameof(SuggestedShares));
            OnPropertyChanged(nameof(SuggestedSharesText));
        }

        private void SavePlan()
        {
            if (EntryLower <= 0 || EntryUpper < EntryLower || StopLoss <= 0 || StopLoss >= EntryLower || TargetOne <= EntryLower || TargetTwo <= TargetOne)
            {
                StatusText = "請確認買入區、停損與兩個目標價的價格順序。";
                return;
            }

            TradePlanStore.Save(new TradePlan
            {
                Symbol = Symbol,
                Name = Name,
                Strategy = SelectedStrategy,
                EntryLower = EntryLower,
                EntryUpper = EntryUpper,
                StopLoss = StopLoss,
                TargetOne = TargetOne,
                TargetTwo = TargetTwo,
                RiskBudget = RiskBudget,
                SuggestedShares = SuggestedShares,
                ValidUntil = ValidUntil,
                CancelCondition = CancelCondition,
                Notes = Notes,
                UpdatedAt = DateTime.Now
            });
            StatusText = "交易計畫已儲存到本機；這不是下單，亦不會觸發任何委託。";
        }

        private void CopyPlan()
        {
            try
            {
                Clipboard.SetText(BuildMemo());
                StatusText = "交易計畫已複製到剪貼簿；請自行確認後再於券商端手動處理。";
            }
            catch
            {
                StatusText = "無法複製到剪貼簿，請手動查看計畫內容。";
            }
        }

        private string BuildMemo()
        {
            return $"【手動交易計畫｜非下單】\n{Symbol} {Name}\n策略：{SelectedStrategy}（有效至 {ValidUntil:yyyy/MM/dd}）\n可買區：{EntryLower:F2} ～ {EntryUpper:F2}\n停損：{StopLoss:F2}\n目標一：{TargetOne:F2}（{RewardRiskOneText}）\n目標二：{TargetTwo:F2}（{RewardRiskTwoText}）\n{CancelCondition}\n建議股數：{SuggestedSharesText}\n備註：{Notes}";
        }

        private string FormatRewardRisk(decimal target)
        {
            return RiskPerShare <= 0 ? "—" : ((target - EntryLower) / RiskPerShare).ToString("0.00") + "R";
        }

        private static DateTime GetNextWeekday(DateTime date)
        {
            var next = date.AddDays(1);
            while (next.DayOfWeek == DayOfWeek.Saturday || next.DayOfWeek == DayOfWeek.Sunday)
            {
                next = next.AddDays(1);
            }
            return next;
        }

        private static decimal RoundToTick(decimal price, bool roundUp)
        {
            if (price <= 0) return 0;
            decimal tick;
            if (price < 10m) tick = 0.01m;
            else if (price < 50m) tick = 0.05m;
            else if (price < 100m) tick = 0.1m;
            else if (price < 500m) tick = 0.5m;
            else if (price < 1000m) tick = 1m;
            else tick = 5m;

            var units = price / tick;
            return (roundUp ? Math.Ceiling(units) : Math.Floor(units)) * tick;
        }
    }
}
