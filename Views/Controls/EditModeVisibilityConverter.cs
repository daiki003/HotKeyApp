using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace HotKeyCommandApp.Views.Controls
{
    public class EditModeVisibilityConverter : IMultiValueConverter
    {
        public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
        {
            bool isEditing = values.Length >= 2 && values[0] != null && ReferenceEquals(values[0], values[1]);
            string mode = parameter as string ?? string.Empty;

            return mode == "Edit"
                ? (isEditing ? Visibility.Visible : Visibility.Collapsed)
                : (isEditing ? Visibility.Collapsed : Visibility.Visible);
        }

        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
        {
            throw new NotSupportedException();
        }
    }
}
