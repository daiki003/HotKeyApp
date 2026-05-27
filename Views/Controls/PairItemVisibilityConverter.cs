using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace HotKeyCommandApp.Views.Controls
{
    public class PairItemVisibilityConverter : IMultiValueConverter
    {
        public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
        {
            bool isCurrent = values.Length >= 2 && values[0] != null && ReferenceEquals(values[0], values[1]);
            bool isFolder = values.Length >= 3 && values[2] is bool folderValue && folderValue;
            string mode = parameter as string ?? string.Empty;

            return mode switch
            {
                "RecordEdit" => isCurrent && !isFolder ? Visibility.Visible : Visibility.Collapsed,
                "FolderEdit" => isCurrent && isFolder ? Visibility.Visible : Visibility.Collapsed,
                _ => isCurrent ? Visibility.Collapsed : Visibility.Visible
            };
        }

        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
        {
            throw new NotSupportedException();
        }
    }
}
