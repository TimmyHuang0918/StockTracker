using StockTracker.ViewModels;
using System.Windows;
using System.Windows.Input;

namespace StockTracker
{
    public partial class PortfolioWindow : Window
    {
        public PortfolioWindow(MainWindowViewModel mainViewModel)
        {
            InitializeComponent();
            DataContext = new PortfolioViewModel(mainViewModel.Stocks);
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
    }
}
