using System.Windows.Input;

namespace HotKeyCommandApp.Views.Controls
{
    public interface ISettingsRecordListNavigator
    {
        void FocusFirstRecord();
        void FocusLastRecord();
        void FocusRecordAfterAppend();
        bool IsFocusInEditor();
        bool TryHandleKeyboardShortcut(KeyEventArgs e);
        bool TryFocusHeaderFromEditor();
    }
}
