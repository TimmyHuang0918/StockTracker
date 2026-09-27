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
        public const string ConservativePressureAction = "保守：第一壓力減碼 50%";
        public const string BalancedPressureAction = "平衡：第一壓力減碼 1/3";
        public const string TrendPressureAction = "趨勢：不減碼，改用結構移動停損";
        public const string CustomPressureAction = "自訂：依備註手動處理";

        private readonly StockViewModel _stock;
        private readonly List<CandleData> _candles;
        private readonly PriceStructureAnalysis _priceStructure;
        private string _selectedStrategy;
        private string _selectedHoldingPeriod;
        private string _selectedFirstPressureAction;
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
        private TradePlanProposal _proposal;

        public TradePlanViewModel(StockViewModel stock)
        {
            _stock = stock ?? throw new ArgumentNullException(nameof(stock));
            _candles = (_stock.GetPublicCandles() ?? new List<CandleData>())
                .Where(item => item != null && item.Close > 0)
                .OrderBy(item => item.Time)
                .ToList();
            _priceStructure = PriceStructureAnalyzer.Analyze(_candles);

            StrategyOptions = new ObservableCollection<string> { TradePlanCalculator.Pullback, TradePlanCalculator.Breakout, TradePlanCalculator.Manual };
            HoldingPeriodOptions = new ObservableCollection<string> { "短線：1～5 個交易日", "波段：1～4 週" };
            FirstPressureActionOptions = new ObservableCollection<string>
            {
                ConservativePressureAction,
                BalancedPressureAction,
                TrendPressureAction,
                CustomPressureAction
            };
            ApplyStrategyCommand = new RelayCommand(_ => ApplyStrategy());
            SavePlanCommand = new RelayCommand(_ => SavePlan());
            CopyPlanCommand = new RelayCommand(_ => CopyPlan());

            _selectedStrategy = TradePlanCalculator.Pullback;
            _selectedHoldingPeriod = HoldingPeriodOptions[0];
            _selectedFirstPressureAction = BalancedPressureAction;
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
        public string StructureSummary => _priceStructure?.Message ?? "尚無結構資料。";
        public string PlanStructureText => !string.IsNullOrWhiteSpace(_proposal?.StructureText) ? _proposal.StructureText : "尚未形成可用交易情境；請參考下方支撐與壓力區，或選擇手動規劃。";
        private string LatestCandleDateText => _candles.Count == 0
            ? "資料待更新"
            : _candles[_candles.Count - 1].Time.ToString("yyyy/MM/dd");
        public string ReferencePriceText => _priceStructure == null || _priceStructure.LatestPrice <= 0m
            ? "日 K 參考價待更新"
            : $"日 K {LatestCandleDateText} 參考價 {_priceStructure.LatestPrice:F2}／目前價格 {LatestPrice:F2}";
        public string SupportOneText => FormatZone(_priceStructure?.Supports?.ElementAtOrDefault(0));
        public string SupportTwoText => FormatZone(_priceStructure?.Supports?.ElementAtOrDefault(1));
        public string ResistanceOneText => FormatZone(_priceStructure?.Resistances?.ElementAtOrDefault(0));
        public string ResistanceTwoText => FormatZone(_priceStructure?.Resistances?.ElementAtOrDefault(1));
        public string ResistanceThreeText => FormatZone(_priceStructure?.Resistances?.ElementAtOrDefault(2));
        public DateTime ValidUntil => GetNextWeekday(DateTime.Today);
        public string ValidUntilText => ValidUntil.ToString("yyyy/MM/dd（ddd）");
        public ObservableCollection<string> StrategyOptions { get; }
        public ObservableCollection<string> HoldingPeriodOptions { get; }
        public ObservableCollection<string> FirstPressureActionOptions { get; }

        public string TomorrowDecisionTitle
        {
            get
            {
                if (SelectedStrategy == TradePlanCalculator.Manual)
                    return "明日自行規劃";
                if (_proposal == null || !_proposal.IsAvailable)
                    return "明日不做";
                return SelectedStrategy == TradePlanCalculator.Pullback
                    ? "明日可做：等待拉回確認"
                    : "明日可做：等待突破確認";
            }
        }

        public string TomorrowDecisionDetail
        {
            get
            {
                if (SelectedStrategy == TradePlanCalculator.Manual)
                    return "此模式不代替你判斷；請自行填寫進場、失效與壓力區處理規則。";
                if (_proposal == null || !_proposal.IsAvailable)
                    return _proposal?.Status ?? "尚未建立可用的隔日情境。";
                if (SelectedStrategy == TradePlanCalculator.Pullback)
                    return $"僅在價格回到 {EntryLower:F2} ～ {EntryUpper:F2}、未跌破 {StopLoss:F2} 後出現止穩時，才手動評估；高於買入區上緣不追價。";
                return $"僅在價格突破壓力後進入 {EntryLower:F2} ～ {EntryUpper:F2} 時，才手動評估；高於買入區上緣不追價。";
            }
        }

        public string FirstPressureActionText
        {
            get
            {
                if (TargetOne <= 0m)
                    return "尚未有有效第一壓力區；請先確認價格結構。";
                switch (SelectedFirstPressureAction)
                {
                    case ConservativePressureAction:
                        return $"到第一壓力區 {TargetOne:F2} 時，手動減碼 50%；剩餘部位再依結構重新確認。";
                    case TrendPressureAction:
                        return $"第一壓力區 {TargetOne:F2} 不預設減碼；僅在有效站穩後，才以新的支撐區上移失效價。";
                    case CustomPressureAction:
                        return $"第一壓力區 {TargetOne:F2} 的處理由你的備註決定，不預設減碼。";
                    default:
                        return $"到第一壓力區 {TargetOne:F2} 時，手動減碼約 1/3，觀察是否能站穩後再決定剩餘部位。";
                }
            }
        }

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

        public string SelectedHoldingPeriod
        {
            get => _selectedHoldingPeriod;
            set
            {
                var normalized = string.IsNullOrWhiteSpace(value) ? HoldingPeriodOptions[0] : value;
                if (_selectedHoldingPeriod == normalized) return;
                _selectedHoldingPeriod = normalized;
                OnPropertyChanged();
            }
        }

        public string SelectedFirstPressureAction
        {
            get => _selectedFirstPressureAction;
            set
            {
                var normalized = string.IsNullOrWhiteSpace(value) ? BalancedPressureAction : value;
                if (_selectedFirstPressureAction == normalized) return;
                _selectedFirstPressureAction = normalized;
                OnPropertyChanged();
                OnPropertyChanged(nameof(FirstPressureActionText));
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
            set
            {
                _entryUpper = value;
                OnPropertyChanged();
                if (!_isApplyingDefaults) RecalculateTargetsAndSizing();
            }
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
            set
            {
                _targetOne = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(RewardRiskOneText));
                if (!_isApplyingDefaults) RecalculateSizingAndValidation();
            }
        }

        public decimal TargetTwo
        {
            get => _targetTwo;
            set
            {
                _targetTwo = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(RewardRiskTwoText));
                if (!_isApplyingDefaults) RecalculateSizingAndValidation();
            }
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

        public decimal MaxEntryPrice => Math.Max(EntryLower, EntryUpper);
        public decimal RiskPerShare => Math.Max(0m, MaxEntryPrice - StopLoss);
        public string RiskPercentText => MaxEntryPrice <= 0 || StopLoss <= 0
            ? "—"
            : ((MaxEntryPrice - StopLoss) / MaxEntryPrice * 100m).ToString("0.00") + "%";
        public string RewardRiskOneText => FormatRewardRisk(TargetOne);
        public string RewardRiskTwoText => FormatRewardRisk(TargetTwo);
        public int SuggestedShares => RiskBudget <= 0 || RiskPerShare <= 0 ? 0 : (int)Math.Floor(RiskBudget / RiskPerShare);
        public string SuggestedSharesText => RiskBudget <= 0
            ? "請輸入單筆最大可承受損失"
            : SuggestedShares <= 0
                ? "風險金額不足以買進一股"
                : $"{SuggestedShares:N0} 股（約 {SuggestedShares / 1000d:F2} 張；以買入區上緣估算，未含交易成本與跳空風險）";
        public string QualitySummary => $"分數 {Score}／風險 {RiskScore}；{InstitutionalLeadership}，外資 {ForeignSensitivity}／投信 {TrustSensitivity}";
        public ICommand ApplyStrategyCommand { get; }
        public ICommand SavePlanCommand { get; }
        public ICommand CopyPlanCommand { get; }

        private void ApplyStrategy()
        {
            _proposal = TradePlanCalculator.Create(_priceStructure, LatestPrice, SelectedStrategy);
            _isApplyingDefaults = true;
            EntryLower = _proposal.EntryLower;
            EntryUpper = _proposal.EntryUpper;
            StopLoss = _proposal.StopLoss;
            TargetOne = _proposal.TargetOne;
            TargetTwo = _proposal.TargetTwo;
            _isApplyingDefaults = false;
            CancelCondition = _proposal.CancelCondition;
            StatusText = _proposal.Status;
            OnPropertyChanged(nameof(PlanStructureText));
            OnPropertyChanged(nameof(ReferencePriceText));
            RecalculateSizingAndValidation();
        }

        private void ClearPricePlan(string message)
        {
            _isApplyingDefaults = true;
            EntryLower = 0m;
            EntryUpper = 0m;
            StopLoss = 0m;
            TargetOne = 0m;
            TargetTwo = 0m;
            _isApplyingDefaults = false;
            CancelCondition = "尚未產生結構化條件；若自行輸入價格，請自行確認支撐區下緣與壓力區上緣。";
            StatusText = message;
            RecalculateSizingAndValidation();
        }

        private void RecalculateTargetsAndSizing()
        {
            if (RiskPerShare > 0 && TargetOne <= EntryLower)
            {
                StatusText = "尚未找到第一層壓力目標；請手動確認上方壓力區。";
            }

            RecalculateSizingAndValidation();
        }

        private void RecalculateSizingAndValidation()
        {
            OnPropertyChanged(nameof(RiskPerShare));
            OnPropertyChanged(nameof(RiskPercentText));
            OnPropertyChanged(nameof(RewardRiskOneText));
            OnPropertyChanged(nameof(RewardRiskTwoText));
            OnPropertyChanged(nameof(SuggestedShares));
            OnPropertyChanged(nameof(SuggestedSharesText));
            OnPropertyChanged(nameof(TomorrowDecisionTitle));
            OnPropertyChanged(nameof(TomorrowDecisionDetail));
            OnPropertyChanged(nameof(FirstPressureActionText));

            if ((_proposal == null || !_proposal.IsAvailable) && SelectedStrategy != TradePlanCalculator.Manual)
            {
                StatusText = _proposal?.Status ?? "尚未建立可用交易情境。";
                return;
            }

            if (EntryLower <= 0 || StopLoss <= 0 || StopLoss >= EntryLower)
            {
                return;
            }

            if (RiskPerShare / EntryLower > 0.06m)
            {
                StatusText = "結構停損距離超過 6%，這筆交易風險過大；建議等待更好的買點，不用硬縮停損。";
                return;
            }

            if (TargetOne <= MaxEntryPrice || (TargetOne - MaxEntryPrice) / RiskPerShare < 1.5m)
            {
                StatusText = "第一壓力區距離不足 1.5R，風報比不佳，標示為明日不做。";
                return;
            }

            if (TargetTwo <= TargetOne)
            {
                StatusText = "明日可做：條件成立才手動執行；第一壓力區處理方式可自行選擇，第二個目標結構不足。";
                return;
            }

            StatusText = "明日可做：條件成立才手動執行；僅供你手動判斷、儲存與複製備忘，不會送出委託。";
        }

        private void SavePlan()
        {
            if (EntryLower <= 0 || EntryUpper < EntryLower || StopLoss <= 0 || StopLoss >= EntryLower || TargetOne <= EntryLower)
            {
                StatusText = "請確認買入區、停損與兩個目標價的價格順序。";
                return;
            }

            TradePlanStore.Save(new TradePlan
            {
                Symbol = Symbol,
                Name = Name,
                Strategy = SelectedStrategy,
                HoldingPeriod = SelectedHoldingPeriod,
                EntryLower = EntryLower,
                EntryUpper = EntryUpper,
                StopLoss = StopLoss,
                TargetOne = TargetOne,
                TargetTwo = TargetTwo,
                FirstPressureAction = SelectedFirstPressureAction,
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
            return $"【明日手動交易計畫｜非下單】\n{Symbol} {Name}\n明日判斷：{TomorrowDecisionTitle}\n{TomorrowDecisionDetail}\n策略：{SelectedStrategy}／{SelectedHoldingPeriod}（有效至 {ValidUntil:yyyy/MM/dd}）\n資料：{ReferencePriceText}\n結構：{PlanStructureText}\n可買區：{EntryLower:F2} ～ {EntryUpper:F2}\n結構失效價：{StopLoss:F2}\n第一壓力區：{TargetOne:F2}（{RewardRiskOneText}）\n處理方式：{SelectedFirstPressureAction}\n{FirstPressureActionText}\n下一壓力區：{(TargetTwo > 0m ? TargetTwo.ToString("F2") : "結構不足")}（{RewardRiskTwoText}）\n放棄條件：{CancelCondition}\n建議股數：{SuggestedSharesText}\n備註：{Notes}";
        }

        private string FormatRewardRisk(decimal target)
        {
            return RiskPerShare <= 0 ? "—" : ((target - MaxEntryPrice) / RiskPerShare).ToString("0.00") + "R";
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

        private static string FormatZone(PriceStructureZone zone)
        {
            return zone == null
                ? "—"
                : $"{zone.Low:F2} ～ {zone.High:F2}（強度 {zone.Strength}／觸及 {zone.Touches} 次）";
        }
    }
}
