using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace HotKeyCommandApp.Views
{
    public class HotkeyToPaddingConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is string hotkey && !string.IsNullOrEmpty(hotkey))
            {
                // "+" が含まれる場合は複合キーとみなし、左右余白を6に設定
                if (hotkey.Contains("+"))
                {
                    return new Thickness(6, 2, 6, 2);
                }
            }
            
            // 単一キー（1文字）の場合は左右余白を0に設定
            return new Thickness(0, 2, 0, 2);
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}
