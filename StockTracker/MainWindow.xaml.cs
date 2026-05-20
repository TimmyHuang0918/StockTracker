using StockTracker.Models;
using StockTracker.Services;
using StockTracker.ViewModels;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Media3D;

namespace StockTracker
{
    public partial class MainWindow : Window
    {
        private CapitalApiService _apiService;
        private Point _stockDragStartPoint;
        private StockViewModel _pendingDragStock;
        private ListBoxItem _draggedItemContainer;
        private ListBoxItem _dragTargetItemContainer;
        public MainWindow()
        {
            InitializeComponent();
            DataContextChanged += MainWindow_DataContextChanged;
            StockListBox.PreviewMouseLeftButtonDown += StockList_OnPreviewMouseLeftButtonDown;
            StockListBox.PreviewMouseMove += StockList_OnPreviewMouseMove;
            StockListBox.DragOver += StockList_OnDragOver;
            StockListBox.Drop += StockList_OnDrop;
            StockListBox.DragLeave += StockList_OnDragLeave;
        }

        private void TitleBar_OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ClickCount == 2)
            {
                WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
                return;
            }

            DragMove();
        }

        private void MinimizeButton_OnClick(object sender, RoutedEventArgs e)
        {
            WindowState = WindowState.Minimized;
        }

        private void MaximizeRestoreButton_OnClick(object sender, RoutedEventArgs e)
        {
            WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
        }

        private void CloseButton_OnClick(object sender, RoutedEventArgs e)
        {
            Close();
        }

        private void StockList_OnMouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if ((e.OriginalSource as FrameworkElement)?.DataContext is StockViewModel stock)
            {
                var detailVm = stock.CreateDetailViewModel();
                var detailWindow = new StockDetailWindow
                {
                    Owner = this,
                    DataContext = detailVm
                };
                detailWindow.Closed += (_, __) => stock.DetachDetailViewModel(detailVm);
                detailWindow.Show();
            }
        }

        private async void btnSubscribe_Click(object sender, RoutedEventArgs e)
        {
            var symbol = textBoxSubscribe.Text?.Trim();
            if (string.IsNullOrWhiteSpace(symbol))
            {
                return;
            }

            if (!(DataContext is MainWindowViewModel vm))
            {
                return;
            }

            vm.NewSymbol = symbol;
            await vm.SubscribeSymbolAsync();

            var stock = vm.Stocks.FirstOrDefault(x => string.Equals(x.Symbol, symbol, StringComparison.OrdinalIgnoreCase));
            if (stock == null)
            {
                return;
            }

            RequestStockKLine(vm, stock, true);
        }

        private void btnUnsubscribe_Click(object sender, RoutedEventArgs e)
        {
            if (DataContext is MainWindowViewModel vm && vm.UnsubscribeCommand.CanExecute(null))
            {
                vm.UnsubscribeCommand.Execute(null);
            }
        }

        private void comboBoxChangeInterval_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        {
            if (!(DataContext is MainWindowViewModel vm) || vm.Stocks.Count == 0)
            {
                return;
            }

            ResolveKLineRequest(vm.SelectedGlobalKLineInterval, out var kLineType, out var minuteNumber);

            foreach (var stock in vm.Stocks)
            {
                RequestStockKLine(vm, stock, true, kLineType, minuteNumber);
            }
        }

        private void RequestStockKLine(MainWindowViewModel vm, StockViewModel stock, bool clearData, short? resolvedKLineType = null, short? resolvedMinuteNumber = null)
        {
            if (vm == null || stock == null)
            {
                return;
            }

            int.TryParse(vm.SelectedGlobalKLineCount, out var kLineCount);
            if (kLineCount <= 0)
            {
                kLineCount = 120;
            }

            var kLineType = resolvedKLineType ?? 4;
            var minuteNumber = resolvedMinuteNumber ?? 0;
            if (!resolvedKLineType.HasValue || !resolvedMinuteNumber.HasValue)
            {
                ResolveKLineRequest(vm.SelectedGlobalKLineInterval, out var resolvedType, out var resolvedMinute);
                kLineType = resolvedType;
                minuteNumber = resolvedMinute;
            }

            if (clearData)
            {
                stock.ClearData();
            }

            BuildDateRangeForBars(vm.SelectedGlobalKLineInterval, kLineCount, out var startDate, out var endDate);
            _apiService?.RequestKLineByDate(stock.Symbol, kLineType, 1, 0, startDate, endDate, minuteNumber);
        }

        private void MainWindow_DataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            if (_apiService != null)
            {
                _apiService.KLineDataReceived -= ApiService_OnKLineDataReceived;
                _apiService.InstantCandleReceived -= ApiService_OnInstantCandleReceived;
            }

            _apiService = (DataContext as MainWindowViewModel)?.ApiService;
            if (_apiService != null)
            {
                _apiService.KLineDataReceived += ApiService_OnKLineDataReceived;
                _apiService.InstantCandleReceived += ApiService_OnInstantCandleReceived;
            }
        }

        private void ApiService_OnKLineDataReceived(string symbol, CandleData candle)
        {
            Dispatcher.BeginInvoke(new Action(() =>
            {
                (DataContext as MainWindowViewModel)?.ApplyKLineData(symbol, candle);
            }));
        }

        private void ApiService_OnInstantCandleReceived(string symbol, CandleData candle)
        {
            Dispatcher.BeginInvoke(new Action(() =>
            {
                if (!(DataContext is MainWindowViewModel vm) || candle.Close == 0)
                {
                    return;
                }

                var normalized = symbol?.Trim() ?? string.Empty;
                var stock = vm.Stocks.FirstOrDefault(x =>
                    string.Equals(x.Symbol, normalized, StringComparison.OrdinalIgnoreCase) ||
                    normalized.EndsWith(x.Symbol, StringComparison.OrdinalIgnoreCase));
                stock?.ApplyInstantCandle(candle, stock.SelectedKLineInterval);
            }));
        }

        private static void ResolveKLineRequest(string interval, out short kLineType, out short minuteNumber)
        {
            switch (interval)
            {
                case "5分K":
                    kLineType = 0;
                    minuteNumber = 5;
                    break;
                case "3分K":
                    kLineType = 0;
                    minuteNumber = 3;
                    break;
                case "日K":
                    kLineType = 4;
                    minuteNumber = 0;
                    break;
                default:
                    kLineType = 4;
                    minuteNumber = 1;
                    break;
            }
        }

        public static void BuildDateRangeForBars(string interval, int requiredBars, out string startDate, out string endDate)
        {
            // 每日交易分鐘數 (例如台股 9:00~13:30 = 270 分鐘)
            var minutesPerDay = 270;

            // 每日可產生多少 K 線
            int barsPerDay;
            switch (interval)
            {
                case "日K":
                    barsPerDay = 1;
                    break;
                case "5分K":
                    barsPerDay = minutesPerDay / 5;
                    break;
                case "3分K":
                    barsPerDay = minutesPerDay / 3;
                    break;
                case "1分K":
                default:
                    barsPerDay = minutesPerDay / 1;
                    break;
            }

            // 需要多少交易日
            int requiredTradingDays = (int)Math.Ceiling((double)requiredBars / barsPerDay);

            // 取得最近交易日清單
            List<DateTime> tradingDays = GetRecentTradingDays(requiredTradingDays);

            // 起始日是最早的交易日，結束日是今天或最近交易日
            startDate = tradingDays.First().ToString("yyyyMMdd");
            endDate = tradingDays.Last().ToString("yyyyMMdd");
        }

        // 範例：模擬取得最近交易日清單
        private static List<DateTime> GetRecentTradingDays(int requiredDays)
        {
            var days = new List<DateTime>();
            var current = DateTime.Now;
            while (days.Count < requiredDays)
            {
                if (IsTradingDay(current))
                {
                    days.Insert(0, current); // 往前插入
                }
                current = current.AddDays(-1);
            }
            return days;
        }

        // 判斷是否為交易日 (簡單版：排除週六週日)
        private static bool IsTradingDay(DateTime date)
        {
            return date.DayOfWeek != DayOfWeek.Saturday && date.DayOfWeek != DayOfWeek.Sunday;
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

        private void ListBox_SelectionStockChanged(object sender, SelectionChangedEventArgs e)
        {
            if (e.AddedItems.Count > 0 && e.AddedItems[0] is StockViewModel stock)
            {
                textBoxSubscribe.Text = stock.Symbol;
            }
        }

        private void StockList_OnPreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            _stockDragStartPoint = e.GetPosition(null);
            var dragHandle = FindDragHandle((DependencyObject)e.OriginalSource);
            _pendingDragStock = dragHandle == null
                ? null
                : FindAncestor<ListBoxItem>(dragHandle)?.DataContext as StockViewModel;
            _draggedItemContainer = dragHandle == null ? null : FindAncestor<ListBoxItem>(dragHandle);
        }

        private void StockList_OnPreviewMouseMove(object sender, MouseEventArgs e)
        {
            if (e.LeftButton != MouseButtonState.Pressed || _pendingDragStock == null)
            {
                return;
            }

            var currentPosition = e.GetPosition(null);
            if (Math.Abs(currentPosition.X - _stockDragStartPoint.X) < SystemParameters.MinimumHorizontalDragDistance &&
                Math.Abs(currentPosition.Y - _stockDragStartPoint.Y) < SystemParameters.MinimumVerticalDragDistance)
            {
                return;
            }

            SetDraggedItemVisual(_draggedItemContainer);
            DragDrop.DoDragDrop(StockListBox, new DataObject(typeof(StockViewModel), _pendingDragStock), DragDropEffects.Move);
            ClearDragVisuals();
            _pendingDragStock = null;
            _draggedItemContainer = null;
        }

        private void StockList_OnDragOver(object sender, DragEventArgs e)
        {
            if (!e.Data.GetDataPresent(typeof(StockViewModel)))
            {
                e.Effects = DragDropEffects.None;
                e.Handled = true;
                return;
            }

            e.Effects = DragDropEffects.Move;
            var targetItem = FindAncestor<ListBoxItem>((DependencyObject)e.OriginalSource);
            SetDragTargetVisual(targetItem);
            e.Handled = true;
        }

        private void StockList_OnDragLeave(object sender, DragEventArgs e)
        {
            if (!StockListBox.IsMouseOver)
            {
                SetDragTargetVisual(null);
            }
        }

        private void StockList_OnDrop(object sender, DragEventArgs e)
        {
            if (!(DataContext is MainWindowViewModel vm))
            {
                ClearDragVisuals();
                return;
            }

            var sourceStock = e.Data.GetData(typeof(StockViewModel)) as StockViewModel;
            if (sourceStock == null)
            {
                ClearDragVisuals();
                return;
            }

            var targetItem = FindAncestor<ListBoxItem>((DependencyObject)e.OriginalSource);
            var targetStock = targetItem?.DataContext as StockViewModel;
            var targetIndex = targetStock == null ? vm.Stocks.Count - 1 : vm.Stocks.IndexOf(targetStock);
            if (targetIndex < 0)
            {
                targetIndex = vm.Stocks.Count - 1;
            }

            var sourceIndex = vm.Stocks.IndexOf(sourceStock);
            if (sourceIndex < 0 || sourceIndex == targetIndex)
            {
                ClearDragVisuals();
                return;
            }

            vm.MoveStock(sourceIndex, targetIndex);
            StockListBox.SelectedItem = sourceStock;
            ClearDragVisuals();
            _pendingDragStock = null;
            _draggedItemContainer = null;
        }

        private static T FindAncestor<T>(DependencyObject current) where T : DependencyObject
        {
            while (current != null)
            {
                if (current is T matched)
                {
                    return matched;
                }

                current = current is Visual || current is Visual3D
                    ? VisualTreeHelper.GetParent(current)
                    : LogicalTreeHelper.GetParent(current);
            }

            return null;
        }

        private void SetDraggedItemVisual(ListBoxItem item)
        {
            if (_draggedItemContainer != null && !ReferenceEquals(_draggedItemContainer, item))
            {
                _draggedItemContainer.Opacity = 1d;
            }

            _draggedItemContainer = item;
            if (_draggedItemContainer != null)
            {
                _draggedItemContainer.Opacity = 0.55d;
            }
        }

        private void SetDragTargetVisual(ListBoxItem item)
        {
            if (_dragTargetItemContainer != null && !ReferenceEquals(_dragTargetItemContainer, item))
            {
                _dragTargetItemContainer.BorderBrush = Brushes.Transparent;
                _dragTargetItemContainer.BorderThickness = new Thickness(1);
            }

            _dragTargetItemContainer = item;
            if (_dragTargetItemContainer != null)
            {
                _dragTargetItemContainer.BorderBrush = Brushes.DeepSkyBlue;
                _dragTargetItemContainer.BorderThickness = new Thickness(2);
            }
        }

        private void ClearDragVisuals()
        {
            if (_draggedItemContainer != null)
            {
                _draggedItemContainer.Opacity = 1d;
            }

            if (_dragTargetItemContainer != null)
            {
                _dragTargetItemContainer.BorderBrush = Brushes.Transparent;
                _dragTargetItemContainer.BorderThickness = new Thickness(1);
            }

            _draggedItemContainer = null;
            _dragTargetItemContainer = null;
        }

        private static FrameworkElement FindDragHandle(DependencyObject current)
        {
            while (current != null)
            {
                if (current is FrameworkElement element && string.Equals(element.Tag as string, "StockDragHandle", StringComparison.Ordinal))
                {
                    return element;
                }

                current = current is Visual || current is Visual3D
                    ? VisualTreeHelper.GetParent(current)
                    : LogicalTreeHelper.GetParent(current);
            }

            return null;
        }

        private void btnScanAll_Click(object sender, RoutedEventArgs e)
        {
            if (DataContext is ViewModels.MainWindowViewModel vm && vm.ApiService != null)
            {
                var rankingWindow = new RankingWindow
                {
                    DataContext = new ViewModels.RankingViewModel(vm.ApiService, vm),
                    Owner = this
                };
                rankingWindow.Show();
            }
        }
    }
}
