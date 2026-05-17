using StockTracker.ViewModels;
using System.Windows;
using System.Windows.Input;

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

        private void KLineCanvas_OnMouseMove(object sender, MouseEventArgs e)
        {
            if ((sender as FrameworkElement)?.DataContext is StockViewModel stock)
            {
                var point = e.GetPosition((IInputElement)sender);
                stock.UpdateCrosshair(point.X, point.Y);
            }
        }

        private void KLineCanvas_OnMouseLeave(object sender, MouseEventArgs e)
        {
            if ((sender as FrameworkElement)?.DataContext is StockViewModel stock)
            {
                stock.ClearCrosshair();
            }
        }

        private void ThreeMajorCanvas_OnMouseMove(object sender, MouseEventArgs e)
        {
            if ((sender as FrameworkElement)?.DataContext is StockViewModel stock)
            {
                var point = e.GetPosition((IInputElement)sender);
                stock.UpdateThreeMajorCrosshair(point.X);
            }
        }

        private void ThreeMajorCanvas_OnMouseLeave(object sender, MouseEventArgs e)
        {
            if ((sender as FrameworkElement)?.DataContext is StockViewModel stock)
            {
                stock.ClearThreeMajorCrosshair();
            }
        }
    }
}
