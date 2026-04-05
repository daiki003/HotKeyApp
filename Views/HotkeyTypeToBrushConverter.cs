using System;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;
using HotKeyCommandApp.Models;

namespace HotKeyCommandApp.Views
{
    public class HotkeyTypeToBrushConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is HotkeyType type)
            {
                string colorHex = type switch
                {
                    HotkeyType.WindowGlobal => "#004B7E",   // 濃い青
                    HotkeyType.HierarchyLocal => "#1B5E20", // 濃い緑
                    HotkeyType.Global => "#BF360C",         // 濃い朱色
                    _ => "#333333"
                };

                return (Brush)new BrushConverter().ConvertFrom(colorHex)!;
            }

            return Brushes.Transparent;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}
