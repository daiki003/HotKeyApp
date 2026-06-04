using System;
using System.Windows.Controls.Primitives;
using System.Windows.Input;

namespace HotKeyCommandApp.Views.Controls
{
    internal static class SettingsDialogKeyboardNavigationHelper
    {
        public static bool TryFocusFirstRecordOnDirectionalKey(
            KeyEventArgs e,
            bool isInteractiveFocused,
            bool hasActiveNavigator,
            Action focusFirstRecordAction,
            bool includeHorizontalKeys = false)
        {
            if (!hasActiveNavigator || isInteractiveFocused)
            {
                return false;
            }

            if (!IsDirectionalKey(e.Key, includeHorizontalKeys))
            {
                return false;
            }

            focusFirstRecordAction();
            e.Handled = true;
            return true;
        }

        private static bool IsDirectionalKey(Key key, bool includeHorizontalKeys)
        {
            if (key == Key.Up || key == Key.Down)
            {
                return true;
            }

            return includeHorizontalKeys && (key == Key.Left || key == Key.Right);
        }
    }
}
