using StockTracker.Models;
using StockTracker.Services;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace StockTracker.ViewModels
{
    public class StrategyOutputViewModel : ViewModelBase
    {
        private string _globalDecision;
        private string _actionText;
        private double _currentHoldingPercentage;
        private double _executedHolding;
        private string _stageLabel;
        private string _description;
        private string _actionColor;
        private int _finalScore;
        private double _techScore;
        private double _chipScore;
        private double _volumeRatio;
        private double _bias20;

        public StrategyOutputViewModel()
        {
            Reasons = new ObservableCollection<string>();
            ChartMarkers = new ObservableCollection<ChartMarker>();
            SupportZones = new List<PriceZone>();
            TrendLines = new List<TrendLine>();
            _globalDecision = "NEUTRAL";
            _actionText = "觀望";
            _currentHoldingPercentage = 0d;
            _executedHolding = 0d;
            _stageLabel = "線性倉位｜空倉 0%";
            _description = "尚未觸發策略事件，部位維持鎖定。";
            _actionColor = "#A0A0A0";
        }

        public string GlobalDecision
        {
            get => _globalDecision;
            set
            {
                _globalDecision = value;
                OnPropertyChanged();
            }
        }

        public string ActionText
        {
            get => _actionText;
            set
            {
                _actionText = value;
                OnPropertyChanged();
            }
        }

        public double CurrentHoldingPercentage
        {
            get => _currentHoldingPercentage;
            set
            {
                _currentHoldingPercentage = value;
                OnPropertyChanged();
            }
        }

        public double ExecutedHolding
        {
            get => _executedHolding;
            set
            {
                _executedHolding = value;
                OnPropertyChanged();
            }
        }

        public string StageLabel
        {
            get => _stageLabel;
            set
            {
                _stageLabel = value;
                OnPropertyChanged();
            }
        }

        public string Description
        {
            get => _description;
            set
            {
                _description = value;
                OnPropertyChanged();
            }
        }

        public string ActionColor
        {
            get => _actionColor;
            set
            {
                _actionColor = value;
                OnPropertyChanged();
            }
        }

        public ObservableCollection<string> Reasons { get; }
        public ObservableCollection<ChartMarker> ChartMarkers { get; }

        /// <summary>最終平滑分數 (0~100)。</summary>
        public int FinalScore
        {
            get => _finalScore;
            set { _finalScore = value; OnPropertyChanged(); OnPropertyChanged(nameof(FinalScoreText)); OnPropertyChanged(nameof(FinalScoreBarWidth)); OnPropertyChanged(nameof(FinalScoreColor)); }
        }

        /// <summary>純技術面加權分數。</summary>
        public double TechScore
        {
            get => _techScore;
            set { _techScore = value; OnPropertyChanged(); OnPropertyChanged(nameof(TechScoreText)); }
        }

        /// <summary>籌碼面分數。</summary>
        public double ChipScore
        {
            get => _chipScore;
            set { _chipScore = value; OnPropertyChanged(); OnPropertyChanged(nameof(ChipScoreText)); }
        }

        /// <summary>當日量比（currentVolume / avgVolume20）。</summary>
        public double VolumeRatio
        {
            get => _volumeRatio;
            set { _volumeRatio = value; OnPropertyChanged(); OnPropertyChanged(nameof(VolumeRatioText)); OnPropertyChanged(nameof(VolumeRatioColor)); }
        }

        /// <summary>20 日乖離率。</summary>
        public double Bias20
        {
            get => _bias20;
            set { _bias20 = value; OnPropertyChanged(); OnPropertyChanged(nameof(Bias20Text)); OnPropertyChanged(nameof(Bias20Color)); }
        }

        /// <summary>由 TechnicalLineQuantizer 算出的支撐壓力區。</summary>
        public List<PriceZone> SupportZones { get; set; }

        /// <summary>由 TechnicalLineQuantizer 算出的趨勢線。</summary>
        public List<TrendLine> TrendLines { get; set; }

        // ── 衍生顯示屬性 ──────────────────────────────────────────────────
        public string FinalScoreText => $"{_finalScore}";
        public double FinalScoreBarWidth => System.Math.Max(0d, System.Math.Min(200d, _finalScore * 2.0d));
        public string FinalScoreColor
        {
            get
            {
                if (_finalScore >= 80) return "#00CC66";
                if (_finalScore >= 60) return "#FFD700";
                if (_finalScore >= 45) return "#FF9800";
                return "#FF4444";
            }
        }

        public string TechScoreText => $"技術:{_techScore:F0}";
        public string ChipScoreText => $"籌碼:{_chipScore:F0}";

        public string VolumeRatioText => $"量比:{_volumeRatio:F2}x";
        public string VolumeRatioColor
        {
            get
            {
                if (_volumeRatio >= 1.5) return "#FF4444";
                if (_volumeRatio >= 1.2) return "#FFD700";
                if (_volumeRatio <= 0.7) return "#AAAAAA";
                return "#66C2FF";
            }
        }

        public string Bias20Text => $"乖離:{_bias20:P1}";
        public string Bias20Color
        {
            get
            {
                if (_bias20 > 0.15) return "#FF4444";
                if (_bias20 > 0.08) return "#FFD700";
                if (_bias20 < -0.05) return "#66C2FF";
                return "#AAAAAA";
            }
        }
    }
}
