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
        private readonly PriceStructureAnalysis _priceStructure;
        private string _selectedStrategy;
        private string _selectedHoldingPeriod;
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
            ApplyStrategyCommand = new RelayCommand(_ => ApplyStrategy());
            SavePlanCommand = new RelayCommand(_ => SavePlan());
            CopyPlanCommand = new RelayCommand(_ => CopyPlan());

            _selectedStrategy = TradePlanCalculator.Pullback;
            _selectedHoldingPeriod = HoldingPeriodOptions[0];
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
        public string ReferencePriceText => _priceStructure == null || _priceStructure.LatestPrice <= 0m
            ? "日 K 參考價待更新"
            : $"日 K 參考價 {_priceStructure.LatestPrice:F2}／目前價格 {LatestPrice:F2}";
        public string SupportOneText => FormatZone(_priceStructure?.Supports?.ElementAtOrDefault(0));
        public string SupportTwoText => FormatZone(_priceStructure?.Supports?.ElementAtOrDefault(1));
        public string ResistanceOneText => FormatZone(_priceStructure?.Resistances?.ElementAtOrDefault(0));
        public string ResistanceTwoText => FormatZone(_priceStructure?.Resistances?.ElementAtOrDefault(1));
        public string ResistanceThreeText => FormatZone(_priceStructure?.Resistances?.ElementAtOrDefault(2));
        public DateTime ValidUntil => GetNextWeekday(DateTime.Today);
        public string ValidUntilText => ValidUntil.ToString("yyyy/MM/dd（ddd）");
        public ObservableCollection<string> StrategyOptions { get; }
        public ObservableCollection<string> HoldingPeriodOptions { get; }

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
                : $"{SuggestedShares:N0} 股（約 {SuggestedShares / 1000d:F2} 張；未含交易成本與跳空風險）";
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

            if (TargetOne <= EntryLower || (TargetOne - EntryLower) / RiskPerShare < 1.5m)
            {
                StatusText = "第一層壓力距離不足 1.5R，風報比不佳，標示為不建議進場。";
                return;
            }

            if (TargetTwo <= TargetOne)
            {
                StatusText = "第二個壓力目標結構不足；可先以第一個壓力作為分段減碼，剩餘部位不預設價格。";
                return;
            }

            StatusText = "計畫已依支撐／壓力區預填；僅供你手動判斷、儲存與複製備忘，不會送出委託。";
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
            return $"【手動交易計畫｜非下單】\n{Symbol} {Name}\n策略：{SelectedStrategy}／{SelectedHoldingPeriod}（有效至 {ValidUntil:yyyy/MM/dd}）\n結構：{PlanStructureText}\n可買區：{EntryLower:F2} ～ {EntryUpper:F2}\n結構失效價：{StopLoss:F2}\n目標一：{TargetOne:F2}（{RewardRiskOneText}）\n目標二：{(TargetTwo > 0m ? TargetTwo.ToString("F2") : "結構不足")}（{RewardRiskTwoText}）\n{CancelCondition}\n建議股數：{SuggestedSharesText}\n備註：{Notes}";
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

        private static string FormatZone(PriceStructureZone zone)
        {
            return zone == null
                ? "—"
                : $"{zone.Low:F2} ～ {zone.High:F2}（強度 {zone.Strength}／觸及 {zone.Touches} 次）";
        }
    }
}
