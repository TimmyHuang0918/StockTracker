using StockTracker.ViewModels;
using System.Windows;
using System.Windows.Input;

namespace StockTracker
{
    public partial class PortfolioWindow : Window
    {
        private bool _holdingDetailsVisible = true;
        private readonly PortfolioViewModel _portfolioViewModel;

        public PortfolioWindow(MainWindowViewModel mainViewModel)
        {
            InitializeComponent();
            _portfolioViewModel = new PortfolioViewModel(mainViewModel.Stocks, mainViewModel);
            DataContext = _portfolioViewModel;
            Closed += (_, __) => _portfolioViewModel.Dispose();
        }

        private void HoldingGrid_OnMouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (!(HoldingGrid.SelectedItem is PortfolioHoldingViewModel holding) || !(DataContext is PortfolioViewModel portfolio))
            {
                return;
            }

            var stock = portfolio.FindStock(holding.Symbol);
            if (stock == null)
            {
                return;
            }

            var detailViewModel = stock.CreateDetailViewModel();
            var detailWindow = new StockDetailWindow { Owner = this, DataContext = detailViewModel };
            detailWindow.Closed += (_, __) => stock.DetachDetailViewModel(detailViewModel);
            detailWindow.Show();
        }

        private void ToggleHoldingDetails_Click(object sender, RoutedEventArgs e)
        {
            _holdingDetailsVisible = !_holdingDetailsVisible;
            var visibility = _holdingDetailsVisible ? Visibility.Visible : Visibility.Collapsed;
            HoldingQuantityColumn.Visibility = visibility;
            HoldingAverageCostColumn.Visibility = visibility;
            ToggleHoldingDetailsButton.Content = _holdingDetailsVisible ? "隱藏成本／股數" : "顯示成本／股數";
        }
    }
}
