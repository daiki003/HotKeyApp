using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;

namespace HotKeyCommandApp.Views
{
    /// <summary>
    /// ImageSourceがNullでない場合にVisibleを返し、Nullの場合にCollapsedを返すコンバーター
    /// </summary>
    public class ImageSourceToVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return (value != null) ? Visibility.Visible : Visibility.Collapsed;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}
