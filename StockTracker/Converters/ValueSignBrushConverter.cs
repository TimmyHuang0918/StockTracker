using System;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace StockTracker.Converters
{
    public sealed class ValueSignBrushConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            try
            {
                var number = System.Convert.ToDouble(value, CultureInfo.InvariantCulture);
                if (number > 0) return Brushes.IndianRed;
                if (number < 0) return Brushes.MediumSeaGreen;
            }
            catch { }

            return Brushes.LightGray;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotSupportedException();
        }
    }
}
