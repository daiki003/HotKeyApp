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
                // システムコマンドの場合は削除ボタンを非表示にする
                if (entry.Value != null && entry.Value.StartsWith("ADD_"))
                    return Visibility.Collapsed;
                
                // 特定の固定コマンド（再ビルドなど）も非表示にする
                if (entry.Name == "再ビルド" || entry.Value == "reload.bat")
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
