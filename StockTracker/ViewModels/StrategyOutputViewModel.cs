using StockTracker.Models;
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

        public StrategyOutputViewModel()
        {
            Reasons = new ObservableCollection<string>();
            ChartMarkers = new ObservableCollection<ChartMarker>();
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
    }
}
