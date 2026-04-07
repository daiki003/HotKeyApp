using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;
using HotKeyCommandApp.Models;

namespace HotKeyCommandApp.Views
{
    public class DeleteButtonVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is CommandEntry entry)
            {
                // メニュー（カテゴリ）およびシステムコマンド（ボタン追加）の場合は非表示にする
                if (entry.Type == CommandType.Command) 
                    return Visibility.Collapsed;
                
                // 「Reload App」 (再ビルド) の場合は特別に非表示にする
                if (entry.Name == "再ビルド" || (entry.Type == CommandType.Batch && entry.Value == "reload.bat"))
                    return Visibility.Collapsed;
                
                return Visibility.Visible;
            }
            return Visibility.Collapsed;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}
