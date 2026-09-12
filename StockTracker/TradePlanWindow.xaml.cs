using System.Windows;

namespace StockTracker
{
    public partial class TradePlanWindow : Window
    {
        public TradePlanWindow()
        {
            InitializeComponent();
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}
