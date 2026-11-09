using System;
using System.Collections.ObjectModel;
using HotKeyCommandApp.ViewModels;

namespace HotKeyCommandApp.ViewModels.Settings
{
    public class ShortcutSettingsSectionViewModel : SettingsSectionViewModelBase
    {
        private const string CapturePrompt = "キーを押してください...";
        private readonly MainViewModel _source;

        private string _editingGlobalHotkey;
        private string _editingSettingsShortcut;
        private string _editingCreateButtonShortcut;
        private string _editingGitDiffFocusShortcut;
        private bool _isCapturingHotkey;
        private string _captureTarget = string.Empty;

        public ShortcutSettingsSectionViewModel(MainViewModel source)
            : base("Shortcuts", "ショートカット")
        {
            _source = source;
            Records = new ObservableCollection<ActionValueRecordViewModel>
            {
                new ActionValueRecordViewModel("Global", "起動ショートカット", () => EditingGlobalHotkey),
                new ActionValueRecordViewModel("Settings", "設定を開く", () => EditingSettingsShortcut),
                new ActionValueRecordViewModel("Create", "ボタン作成", () => EditingCreateButtonShortcut),
                new ActionValueRecordViewModel("GitDiffFocus", "差分ウィンドウにフォーカス", () =>
                    string.IsNullOrWhiteSpace(EditingGitDiffFocusShortcut) ? "未設定" : EditingGitDiffFocusShortcut)
            };
            Reset();
        }

        public ObservableCollection<ActionValueRecordViewModel> Records { get; }

        public string EditingGlobalHotkey
        {
            get => _editingGlobalHotkey;
            set { _editingGlobalHotkey = value; OnPropertyChanged(); RefreshRecords(); }
        }

        public string EditingSettingsShortcut
        {
            get => _editingSettingsShortcut;
            set { _editingSettingsShortcut = value; OnPropertyChanged(); RefreshRecords(); }
        }

        public string EditingCreateButtonShortcut
        {
            get => _editingCreateButtonShortcut;
            set { _editingCreateButtonShortcut = value; OnPropertyChanged(); RefreshRecords(); }
        }

        public string EditingGitDiffFocusShortcut
        {
            get => _editingGitDiffFocusShortcut;
            set { _editingGitDiffFocusShortcut = value; OnPropertyChanged(); RefreshRecords(); }
        }

        public bool IsCapturingHotkey
        {
            get => _isCapturingHotkey;
            private set { _isCapturingHotkey = value; OnPropertyChanged(); }
        }

        public string CaptureTarget
        {
            get => _captureTarget;
            private set { _captureTarget = value; OnPropertyChanged(); }
        }

        public void BeginCapture(string captureTarget)
        {
            IsCapturingHotkey = true;
            CaptureTarget = captureTarget;
            switch (captureTarget)
            {
                case "Global":
                    EditingGlobalHotkey = CapturePrompt;
                    break;
                case "Settings":
                    EditingSettingsShortcut = CapturePrompt;
                    break;
                case "Create":
                    EditingCreateButtonShortcut = CapturePrompt;
                    break;
                case "GitDiffFocus":
                    EditingGitDiffFocusShortcut = CapturePrompt;
                    break;
            }
        }

        public void CompleteCapture(string finalHotkey)
        {
            switch (CaptureTarget)
            {
                case "Global":
                    EditingGlobalHotkey = finalHotkey;
                    break;
                case "Settings":
                    EditingSettingsShortcut = finalHotkey;
                    break;
                case "Create":
                    EditingCreateButtonShortcut = finalHotkey;
                    break;
                case "GitDiffFocus":
                    EditingGitDiffFocusShortcut = finalHotkey;
                    break;
            }

            IsCapturingHotkey = false;
            CaptureTarget = string.Empty;
        }

        public void CancelCapture()
        {
            IsCapturingHotkey = false;
            switch (CaptureTarget)
            {
                case "Global":
                    EditingGlobalHotkey = _source.GlobalHotkey;
                    break;
                case "Settings":
                    EditingSettingsShortcut = _source.SettingsShortcut;
                    break;
                case "Create":
                    EditingCreateButtonShortcut = _source.CreateButtonShortcut;
                    break;
                case "GitDiffFocus":
                    EditingGitDiffFocusShortcut = _source.GitDiffFocusShortcut;
                    break;
            }

            CaptureTarget = string.Empty;
        }

        public override void Save()
        {
            _source.EditingGlobalHotkey = EditingGlobalHotkey;
            _source.EditingSettingsShortcut = EditingSettingsShortcut;
            _source.EditingCreateButtonShortcut = EditingCreateButtonShortcut;
            _source.EditingGitDiffFocusShortcut = EditingGitDiffFocusShortcut;
            _source.IsCapturingHotkey = IsCapturingHotkey;
            _source.CaptureTarget = CaptureTarget;
        }

        public override void Reset()
        {
            EditingGlobalHotkey = _source.EditingGlobalHotkey;
            EditingSettingsShortcut = _source.EditingSettingsShortcut;
            EditingCreateButtonShortcut = _source.EditingCreateButtonShortcut;
            EditingGitDiffFocusShortcut = _source.EditingGitDiffFocusShortcut;
            IsCapturingHotkey = _source.IsCapturingHotkey;
            CaptureTarget = _source.CaptureTarget;
            RefreshRecords();
        }

        private void RefreshRecords()
        {
            foreach (ActionValueRecordViewModel record in Records)
            {
                record.Refresh();
            }
        }
    }
}
