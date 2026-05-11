using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;
using HotKeyCommandApp.Models;

namespace HotKeyCommandApp.Views
{
    public class MenuIndicatorConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is CommandEntry entry)
            {
                if (entry.Category == CommandCategory.Hierarchy || (entry.Value?.StartsWith("FOLDER:") ?? false))
                {
                    // 種類選択テンプレート用の矢印は表示しない
                    if (entry.Value == "TYPE_TEMPLATE") return Visibility.Collapsed;
                    
                    return Visibility.Visible;
                }
            }
            return Visibility.Collapsed;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return Binding.DoNothing;
        }
    }
}
