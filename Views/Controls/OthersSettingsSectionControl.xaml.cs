using System.Windows.Controls;

namespace HotKeyCommandApp.Views.Controls
{
    public partial class OthersSettingsSectionControl : UserControl, ISettingsRecordSectionControl
    {
        public OthersSettingsSectionControl()
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
