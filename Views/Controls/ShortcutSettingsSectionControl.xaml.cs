using System;
using System.Windows.Controls;
using HotKeyCommandApp.ViewModels.Settings;

namespace HotKeyCommandApp.Views.Controls
{
    public partial class ShortcutSettingsSectionControl : UserControl, ISettingsRecordSectionControl
    {
        public event EventHandler? GlobalHotkeyRequested;
        public event EventHandler? SettingsHotkeyRequested;
        public event EventHandler? CreateHotkeyRequested;
        public event EventHandler? GitDiffFocusHotkeyRequested;

        public ShortcutSettingsSectionControl()
        {
            InitializeComponent();
        }

        public void FocusPrimaryButton()
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

        private void RecordControl_Activated(object sender, ActionValueRecordViewModel item)
        {
            switch (item.Key)
            {
                case "Global":
                    GlobalHotkeyRequested?.Invoke(this, EventArgs.Empty);
                    break;
                case "Settings":
                    SettingsHotkeyRequested?.Invoke(this, EventArgs.Empty);
                    break;
                case "Create":
                    CreateHotkeyRequested?.Invoke(this, EventArgs.Empty);
                    break;
                case "GitDiffFocus":
                    GitDiffFocusHotkeyRequested?.Invoke(this, EventArgs.Empty);
                    break;
            }
        }
    }
}
