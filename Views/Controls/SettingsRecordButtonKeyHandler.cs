using System.Windows.Input;

namespace HotKeyCommandApp.Views.Controls
{
    internal static class SettingsRecordButtonKeyHandler
    {
        public static bool TryHandleStandardNavigation(SettingsRecordButtonControl recordButton, KeyEventArgs e)
        {
            switch (e.Key)
            {
                case Key.Up:
                    e.Handled = SettingsRecordNavigationHelper.FocusPrevious(recordButton.RowButtonElement);
                    return true;
                case Key.Down:
                    e.Handled = SettingsRecordNavigationHelper.FocusNext(recordButton.RowButtonElement);
                    return true;
                case Key.Home:
                    e.Handled = SettingsRecordNavigationHelper.FocusFirst(recordButton.RowButtonElement);
                    return true;
                case Key.End:
                    e.Handled = SettingsRecordNavigationHelper.FocusLast(recordButton.RowButtonElement);
                    return true;
                default:
                    return false;
            }
        }
    }
}
