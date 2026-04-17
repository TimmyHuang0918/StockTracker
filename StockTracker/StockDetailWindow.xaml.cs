using System.Windows;
using StockTracker.ViewModels;

namespace StockTracker
{
    public partial class StockDetailWindow : Window
    {
        public StockDetailWindow()
        {
            InitializeComponent();
        }

        private void ChartViewbox_OnSizeChanged(object sender, SizeChangedEventArgs e)
        {
            if ((sender as FrameworkElement)?.DataContext is StockViewModel stock)
            {
                stock.UpdateDisplayCapacity(e.NewSize.Width);
            }
        }
    }
}
