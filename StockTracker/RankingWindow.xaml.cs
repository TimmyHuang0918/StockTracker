using System.Windows;

namespace StockTracker
{
    public partial class RankingWindow : Window
    {
        public RankingWindow()
        {
            InitializeComponent();
        }

        private void DataGrid_PreviewMouseDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            e.Handled = true; 
        }
    }
}
