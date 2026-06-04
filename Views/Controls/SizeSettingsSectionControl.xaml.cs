using System.Windows.Controls;

namespace HotKeyCommandApp.Views.Controls
{
    public partial class SizeSettingsSectionControl : UserControl, ISettingsRecordSectionControl
    {
        public SizeSettingsSectionControl()
        {
            InitializeComponent();
        }

        public void FocusPrimaryInput()
        {
            FocusFirstRecord();
        }

        public void FocusFirstRecord()
        {
            RecordsList.FocusFirstRecord();
        }

        public void FocusLastRecord()
        {
            RecordsList.FocusLastRecord();
        }

        public void FocusRecordAfterAppend()
        {
            RecordsList.FocusRecordAfterAppend();
        }

        public bool IsFocusInEditor()
        {
            return RecordsList.IsFocusInEditor();
        }

        public bool TryHandleKeyboardShortcut(System.Windows.Input.KeyEventArgs e)
        {
            return RecordsList.TryHandleKeyboardShortcut(e);
        }

        public bool TryFocusHeaderFromEditor()
        {
            return RecordsList.TryFocusHeaderFromEditor();
        }
    }
}
