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
            _priceStructure = PriceStructureAnalyzer.Analyze(_candles);

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
        public string StructureSummary => _priceStructure?.Message ?? "尚無結構資料。";
        public string SupportOneText => FormatZone(_priceStructure?.Supports?.ElementAtOrDefault(0));
        public string SupportTwoText => FormatZone(_priceStructure?.Supports?.ElementAtOrDefault(1));
        public string ResistanceOneText => FormatZone(_priceStructure?.Resistances?.ElementAtOrDefault(0));
        public string ResistanceTwoText => FormatZone(_priceStructure?.Resistances?.ElementAtOrDefault(1));
        public string ResistanceThreeText => FormatZone(_priceStructure?.Resistances?.ElementAtOrDefault(2));
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
                : $"{SuggestedShares:N0} 股（約 {SuggestedShares / 1000d:F2} 張）";
        public string QualitySummary => $"分數 {Score}／風險 {RiskScore}；{InstitutionalLeadership}，外資 {ForeignSensitivity}／投信 {TrustSensitivity}";
        public ICommand ApplyStrategyCommand { get; }
        public ICommand SavePlanCommand { get; }
        public ICommand CopyPlanCommand { get; }

        private void ApplyStrategy()
        {
            if (_priceStructure == null || !_priceStructure.HasSufficientData)
            {
                ClearPricePlan("日 K 結構資料不足，請切換或補足日 K 後再建立計畫；你仍可手動填寫價格。" );
                return;
            }

            var primarySupport = _priceStructure.Supports.ElementAtOrDefault(0);
            var firstResistance = _priceStructure.Resistances.ElementAtOrDefault(0);
            var secondResistance = _priceStructure.Resistances.ElementAtOrDefault(1);
            var thirdResistance = _priceStructure.Resistances.ElementAtOrDefault(2);
            if (primarySupport == null || firstResistance == null)
            {
                ClearPricePlan("目前找不到足夠的支撐或壓力區，請先手動判讀，不自動預填交易價格。" );
                return;
            }

            _isApplyingDefaults = true;
            if (SelectedStrategy == "拉回買進")
            {
                EntryLower = primarySupport.Low;
                EntryUpper = primarySupport.High;
                StopLoss = PriceStructureAnalyzer.RoundToTick(primarySupport.Low - GetZoneBuffer(primarySupport), false);
                TargetOne = TargetBelowResistance(firstResistance);
                TargetTwo = TargetBelowResistance(secondResistance);
                CancelCondition = "僅在支撐區內止跌、隔日再突破反彈 K 高點時考慮；若收盤跌破支撐區下緣，取消買進。";
            }
            else
            {
                EntryLower = PriceStructureAnalyzer.RoundToTick(firstResistance.High + PriceStructureAnalyzer.GetPriceTick(firstResistance.High), true);
                EntryUpper = PriceStructureAnalyzer.RoundToTick(EntryLower + Math.Max(_priceStructure.Atr14 * 0.25m, PriceStructureAnalyzer.GetPriceTick(EntryLower) * 2m), true);
                StopLoss = PriceStructureAnalyzer.RoundToTick(firstResistance.Low - GetZoneBuffer(firstResistance), false);
                TargetOne = TargetBelowResistance(secondResistance);
                TargetTwo = TargetBelowResistance(thirdResistance);
                CancelCondition = "只在壓力區上緣有效突破後考慮；若突破後收盤回到壓力區內，視為假突破，取消或退出計畫。";
            }
            _isApplyingDefaults = false;

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

        private decimal GetZoneBuffer(PriceStructureZone zone)
        {
            var zoneWidth = zone == null ? 0m : Math.Max(0m, zone.High - zone.Low);
            return Math.Max(PriceStructureAnalyzer.GetPriceTick(Math.Max(0.01m, LatestPrice)), Math.Max(_priceStructure?.Atr14 ?? 0m, zoneWidth) * 0.15m);
        }

        private decimal TargetBelowResistance(PriceStructureZone zone)
        {
            if (zone == null) return 0m;
            return PriceStructureAnalyzer.RoundToTick(zone.Low - GetZoneBuffer(zone), false);
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
                StatusText = "尚未找到第二層壓力；可先以第一層壓力作為分段停利，剩餘部位等待後續結構確認。";
                return;
            }

            StatusText = "計畫已依支撐／壓力區預填；僅供你手動判斷、儲存與複製備忘，不會送出委託。";
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

        private static string FormatZone(PriceStructureZone zone)
        {
            return zone == null
                ? "—"
                : $"{zone.Low:F2} ～ {zone.High:F2}（強度 {zone.Strength}／觸及 {zone.Touches} 次）";
        }
    }
}
