using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using HotKeyCommandApp.Models;
using HotKeyCommandApp.Services;

namespace HotKeyCommandApp.ViewModels
{
    public class ButtonCreationViewModel : INotifyPropertyChanged
    {
        private readonly ConfigService _configService;
        private readonly WindowService _windowService;
        private AppSettings _appSettings;
        
        // Output result
        public CommandEntry? ResultCommand { get; private set; }

        private readonly List<CreationState> _history = new();
        private int _historyIndex = -1;
        private bool _isNavigatingHistory = false;

        private class CreationState
        {
            public InputStep CurrentStep { get; init; }
            public string InputText { get; init; } = "";
            public string InputPrompt { get; init; } = "";
            public string NewEntryName { get; init; } = "";
            public CommandType NewEntryType { get; init; }
            public CommandType? SelectedType { get; init; }
        }

        private InputStep _currentStep = InputStep.EnteringName;
        public InputStep CurrentStep
        {
            get => _currentStep;
            set
            {
                _currentStep = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(ShowArgumentOption));
                OnPropertyChanged(nameof(ShowFileSearchOption));
                OnPropertyChanged(nameof(ShowMainBrowseButton));
                OnPropertyChanged(nameof(IsListBoxVisible));
                OnPropertyChanged(nameof(ShowAppPathInput));
            }
        }

        public string Title => _editingCommand == null ? "新しいボタンの作成" : "ボタンの編集";

        private string _inputPrompt = "";
        public string InputPrompt
        {
            get => _inputPrompt;
            set { _inputPrompt = value; OnPropertyChanged(); }
        }

        private string _inputText = "";
        public string InputText
        {
            get => _inputText;
            set
            {
                _inputText = value;
                OnPropertyChanged();
                if (CurrentStep == InputStep.EnteringValue && (_newEntryType == CommandType.Folder || _newEntryType == CommandType.File || _newEntryType == CommandType.Batch || _newEntryType == CommandType.URL || _newEntryType == CommandType.WindowSwitcher))
                {
                    PerformIncrementalSearch(value);
                }
            }
        }

        private string _appPath = "";
        public string AppPath
        {
            get => _appPath;
            set { _appPath = value; OnPropertyChanged(); }
        }

        public ObservableCollection<CommandEntry> DisplayCommands { get; } = new();

        private CommandEntry? _selectedItem;
        public CommandEntry? SelectedItem
        {
            get => _selectedItem;
            set
            {
                _selectedItem = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(IsItemSelected));
            }
        }

        public bool IsItemSelected => SelectedItem != null;

        public bool IsListBoxVisible => CurrentStep == InputStep.EnteringName || CurrentStep == InputStep.EnteringAppPath || DisplayCommands.Count > 0;

        private string _newEntryName = "";
        private CommandType _newEntryType;
        private CommandEntry? _editingCommand;

        private bool _isRequiresArgumentChecked;
        public bool IsRequiresArgumentChecked
        {
            get => _isRequiresArgumentChecked;
            set { _isRequiresArgumentChecked = value; OnPropertyChanged(); }
        }

        private bool _isFileSearchChecked;
        public bool IsFileSearchChecked
        {
            get => _isFileSearchChecked;
            set { _isFileSearchChecked = value; OnPropertyChanged(); }
        }

        public bool ShowArgumentOption => CurrentStep == InputStep.EnteringValue && (_newEntryType == CommandType.URL || _newEntryType == CommandType.Folder || _newEntryType == CommandType.Batch || _newEntryType == CommandType.File);
        public bool ShowFileSearchOption => (CurrentStep == InputStep.EnteringValue || CurrentStep == InputStep.EnteringAppPath) && (_newEntryType == CommandType.Folder || _newEntryType == CommandType.File);
        public bool ShowMainBrowseButton => CurrentStep == InputStep.EnteringValue && (_newEntryType == CommandType.Folder || _newEntryType == CommandType.File || _newEntryType == CommandType.Batch);
        public bool ShowAppPathInput => CurrentStep == InputStep.EnteringAppPath;

        public List<CommandTypeOption> AvailableCommandTypes { get; } = new()
        {
            new CommandTypeOption { Name = "URLを開く", Type = CommandType.URL },
            new CommandTypeOption { Name = "フォルダを開く", Type = CommandType.Folder },
            new CommandTypeOption { Name = "バッチ実行", Type = CommandType.Batch },
            new CommandTypeOption { Name = "ファイルを開く", Type = CommandType.File },
            new CommandTypeOption { Name = "特定のウィンドウを切替", Type = CommandType.WindowSwitcher },
            new CommandTypeOption { Name = "階層の親", Type = CommandType.Menu }
        };
        
        public double ButtonWidth => _appSettings?.ButtonWidth ?? 350;
        public double ButtonHeight => _appSettings?.ButtonHeight ?? 50;
        public double FontSize => _appSettings?.FontSize ?? 15;

        public event Action<string>? RequestControlFocus;
        public event Action? RequestClose;

        public ICommand CommitInputCommand => new RelayCommand<object>(_ => CommitInput());
        public ICommand CancelInputCommand => new RelayCommand<object>(_ => CancelInput());
        public ICommand NavigateBackCommand => new RelayCommand<object>(_ => NavigateBack());
        public ICommand NavigateForwardCommand => new RelayCommand<object>(_ => NavigateForward());
        public ICommand BrowseFileCommand => new RelayCommand<object>(_ => BrowseFile());
        public ICommand BrowseAppCommand => new RelayCommand<object>(_ => BrowseApp());
        public ICommand ExecuteCommand => new RelayCommand<CommandEntry>(Execute);
        public ICommand MoveUpCommand => new RelayCommand<object>(_ => MoveSelection(-1));
        public ICommand MoveDownCommand => new RelayCommand<object>(_ => MoveSelection(1));
        public ICommand DeleteRegisteredAppCommand => new RelayCommand<CommandEntry>(DeleteRegisteredApp);
        public ICommand EditRegisteredAppCommand => new RelayCommand<CommandEntry>(EditRegisteredApp);

        public ButtonCreationViewModel(AppSettings appSettings, CommandEntry? editingCommand = null)
        {
            _configService = new ConfigService();
            _windowService = new WindowService();
            _appSettings = appSettings;
            _editingCommand = editingCommand;

            StartFlow();
        }

        private void StartFlow()
        {
            if (_editingCommand == null)
            {
                _newEntryName = "";
                InputText = "";
                AppPath = "";
                IsRequiresArgumentChecked = false;
                IsFileSearchChecked = false;

                CurrentStep = InputStep.EnteringName;
                InputPrompt = "名前を入力し、機能を選んでください:";

                SetupTypeSelectionDisplay();
                SelectedItem = DisplayCommands.FirstOrDefault();
            }
            else
            {
                _newEntryName = _editingCommand.Name;
                _newEntryType = _editingCommand.Type;
                IsRequiresArgumentChecked = _editingCommand.RequiresArgument;
                IsFileSearchChecked = _editingCommand.IsFileSearchEnabled;

                CurrentStep = InputStep.EnteringName;
                InputPrompt = "名前を編集し、機能を選んでください:";
                InputText = _editingCommand.Name;
                AppPath = _editingCommand.AppPath ?? "";

                SetupTypeSelectionDisplay();
                SelectedItem = DisplayCommands.FirstOrDefault(c => c.Type == _editingCommand.Type) ?? DisplayCommands.FirstOrDefault();
            }

            RecordHistory();
            RequestControlFocus?.Invoke("InputTextBox");
        }

        private void SetupTypeSelectionDisplay()
        {
            DisplayCommands.Clear();
            foreach (var t in AvailableCommandTypes)
            {
                DisplayCommands.Add(new CommandEntry { Name = t.Name, Type = t.Type, Value = "FEATURE_SELECTION" });
            }
        }

        private void RecordHistory()
        {
            if (_isNavigatingHistory) return;

            if (_historyIndex >= 0 && _historyIndex < _history.Count - 1)
            {
                _history.RemoveRange(_historyIndex + 1, _history.Count - (_historyIndex + 1));
            }

            var state = new CreationState
            {
                CurrentStep = CurrentStep,
                InputText = InputText,
                InputPrompt = InputPrompt,
                NewEntryName = _newEntryName,
                NewEntryType = _newEntryType,
                SelectedType = SelectedItem?.Type
            };

            if (_history.Count > 0)
            {
                var last = _history.Last();
                if (last.CurrentStep == state.CurrentStep &&
                    last.InputText == state.InputText &&
                    last.SelectedType == state.SelectedType)
                {
                    return;
                }
            }

            _history.Add(state);
            _historyIndex++;
        }

        private void NavigateBack()
        {
            if (_historyIndex > 0)
            {
                _isNavigatingHistory = true;
                _historyIndex--;
                RestoreState(_history[_historyIndex]);
                _isNavigatingHistory = false;
            }
        }

        private void NavigateForward()
        {
            if (_historyIndex >= 0 && _historyIndex < _history.Count - 1)
            {
                _isNavigatingHistory = true;
                _historyIndex++;
                RestoreState(_history[_historyIndex]);
                _isNavigatingHistory = false;
            }
        }

        private void RestoreState(CreationState state)
        {
            CurrentStep = state.CurrentStep;
            InputText = state.InputText;
            InputPrompt = state.InputPrompt;
            _newEntryName = state.NewEntryName;
            _newEntryType = state.NewEntryType;

            if (CurrentStep == InputStep.EnteringName)
            {
                SetupTypeSelectionDisplay();
                if (state.SelectedType.HasValue)
                {
                    SelectedItem = DisplayCommands.FirstOrDefault(c => c.Type == state.SelectedType.Value);
                }
            }
            else if (CurrentStep == InputStep.EnteringAppPath)
            {
                PopulateRegisteredApps();
            }
            else
            {
                DisplayCommands.Clear();
            }

            if (CurrentStep == InputStep.EnteringAppPath)
                RequestControlFocus?.Invoke("AppPathTextBox");
            else
                RequestControlFocus?.Invoke("InputTextBox");
        }

        private void MoveSelection(int direction)
        {
            if (DisplayCommands.Count == 0) return;
            int currentIndex = SelectedItem != null ? DisplayCommands.IndexOf(SelectedItem) : -1;

            bool isFileSearchScreen = CurrentStep == InputStep.EnteringValue || CurrentStep == InputStep.EnteringAppPath;

            if (direction < 0) // Up
            {
                if (currentIndex == 0)
                {
                    if (isFileSearchScreen)
                    {
                        // ファイル検索画面では一番上で「上」を押すと、入力ボックスにフォーカスを戻す
                        SelectedItem = null;
                        RequestControlFocus?.Invoke("InputTextBox");
                        return;
                    }
                    else
                    {
                        // 機能選択画面等では、通常通りループする
                        SelectedItem = DisplayCommands.Last();
                        return;
                    }
                }
                else if (currentIndex == -1)
                {
                    SelectedItem = DisplayCommands.Last();
                    return;
                }
            }
            else // Down
            {
                if (currentIndex == DisplayCommands.Count - 1)
                {
                    if (isFileSearchScreen)
                    {
                        // ファイル検索画面では、一番下で「下」を押すと、入力ボックスへ移動する
                        SelectedItem = null;
                        RequestControlFocus?.Invoke("InputTextBox");
                        return;
                    }
                    else
                    {
                        // 通常通りループする
                        SelectedItem = DisplayCommands[0];
                        return;
                    }
                }
                else if (currentIndex == -1)
                {
                    SelectedItem = DisplayCommands[0];
                    return;
                }
            }

            int newIndex = currentIndex + direction;
            if (newIndex >= 0 && newIndex < DisplayCommands.Count)
            {
                SelectedItem = DisplayCommands[newIndex];
            }
        }

        public void Execute(CommandEntry? command)
        {
            if (command != null)
            {
                if (command.Value == "TYPE_TEMPLATE")
                {
                    // "Pick" the suggestion into the text box and stay in this step
                    InputText = command.Name;
                    SelectedItem = null;
                    RequestControlFocus?.Invoke("InputTextBox");
                    return;
                }
                SelectedItem = command;
                CommitInput();
            }
        }

        private void CommitInput()
        {
            if (CurrentStep == InputStep.EnteringName)
            {
                if (string.IsNullOrWhiteSpace(InputText)) return;
                
                // For name entry, a feature MUST be selected. 
                // If focus is in TextBox and nothing selected, pick the first one by default if available.
                if (SelectedItem == null && DisplayCommands.Count > 0) SelectedItem = DisplayCommands[0];
                if (SelectedItem == null) return;

                _newEntryName = InputText.Trim();
                _newEntryType = SelectedItem.Type;

                if (_newEntryType == CommandType.Menu)
                {
                    FinishInputFlow();
                }
                else
                {
                    InputText = _editingCommand?.Value ?? "";
                    CurrentStep = InputStep.EnteringValue;
                    InputPrompt = (_newEntryType == CommandType.WindowSwitcher) ? "ウィンドウタイトルを入力してください:" : "URLまたはパスを入力してください:";
                    DisplayCommands.Clear();
                    RequestControlFocus?.Invoke("InputTextBox");
                }
                RecordHistory();
                return;
            }

            if (CurrentStep == InputStep.EnteringValue)
            {
                // If an item is selected, this is equivalent to "Picking" it
                if (SelectedItem != null)
                {
                    Execute(SelectedItem);
                    return;
                }

                string path = InputText.Trim();
                if (string.IsNullOrEmpty(path)) return;

                if (_newEntryType == CommandType.Folder)
                {
                    CurrentStep = InputStep.EnteringAppPath;
                    InputText = path;
                    InputPrompt = "開く際に使用するソフトを選択してください (任意):";
                    PopulateRegisteredApps();
                    RequestControlFocus?.Invoke("CommandListBox");
                }
                else if (_newEntryType == CommandType.Batch || _newEntryType == CommandType.File)
                {
                    CurrentStep = InputStep.EnteringAppPath;
                    InputText = path;
                    InputPrompt = "開く際に使用するソフトを選択してください (任意):";
                    PopulateRegisteredApps();
                    RequestControlFocus?.Invoke("CommandListBox");
                }
                else
                {
                    InputText = path;
                    FinishInputFlow();
                }
                RecordHistory();
                return;
            }

            if (CurrentStep == InputStep.EnteringAppPath)
            {
                if (SelectedItem != null)
                {
                    if (SelectedItem.Value == "NONE")
                    {
                        AppPath = "";
                        FinishInputFlow();
                        return;
                    }

                    if (SelectedItem.Value == "ADD_REGISTERED_APP")
                    {
                        AddRegisteredApp();
                        return;
                    }

                    // それ以外の値はアプリのパスとして扱う（PopulateRegisteredAppsでValueにPathが入っている）
                    AppPath = SelectedItem.Value;
                    FinishInputFlow();
                    return;
                }

                // For App Selection, ensure focus is on the ListBox if we committed via Enter in the TextBox
                RequestControlFocus?.Invoke("CommandListBox");
                FinishInputFlow();
                return;
            }
        }

        private void FinishInputFlow()
        {
            string val = InputText.Trim();
            if (_newEntryType == CommandType.URL && !string.IsNullOrEmpty(val) && !val.StartsWith("http") && !val.Contains("://"))
            {
                val = "https://" + val;
            }

            if (_editingCommand != null)
            {
                _editingCommand.Name = _newEntryName;
                _editingCommand.Type = _newEntryType;
                _editingCommand.Value = val;
                _editingCommand.AppPath = (_newEntryType == CommandType.File || _newEntryType == CommandType.Folder || _newEntryType == CommandType.Batch) ? AppPath.Trim() : null;
                _editingCommand.RequiresArgument = IsRequiresArgumentChecked;
                _editingCommand.IsFileSearchEnabled = IsFileSearchChecked;

                if (_editingCommand.Type == CommandType.Menu && _editingCommand.Children == null)
                    _editingCommand.Children = new List<CommandEntry>();
                else if (_editingCommand.Type != CommandType.Menu)
                    _editingCommand.Children = null;

                ResultCommand = _editingCommand;
            }
            else
            {
                ResultCommand = new CommandEntry
                {
                    Name = _newEntryName,
                    Type = _newEntryType,
                    Value = val,
                    AppPath = (_newEntryType == CommandType.File || _newEntryType == CommandType.Folder || _newEntryType == CommandType.Batch) ? AppPath.Trim() : null,
                    RequiresArgument = IsRequiresArgumentChecked,
                    IsFileSearchEnabled = IsFileSearchChecked,
                    Children = _newEntryType == CommandType.Menu ? new List<CommandEntry>() : null
                };
            }

            RequestClose?.Invoke();
        }

        private void CancelInput()
        {
            ResultCommand = null;
            RequestClose?.Invoke();
        }

        private void BrowseFile()
        {
            try
            {
                if (_newEntryType == CommandType.WindowSwitcher)
                {
                    // Show all windows when clicking browse button in window mode
                    PerformIncrementalSearch("");
                    return;
                }

                string? initialDirectory = GetContextDirectory();
                if (_newEntryType == CommandType.Folder)
                {
                    var dialog = new Microsoft.Win32.OpenFolderDialog();
                    if (!string.IsNullOrEmpty(initialDirectory)) dialog.InitialDirectory = initialDirectory;
                    if (dialog.ShowDialog() == true) InputText = dialog.FolderName;
                }
                else
                {
                    var dialog = new Microsoft.Win32.OpenFileDialog();
                    if (!string.IsNullOrEmpty(initialDirectory)) dialog.InitialDirectory = initialDirectory;
                    if (dialog.ShowDialog() == true) InputText = dialog.FileName;
                }
            }
            finally
            {
                if (CurrentStep == InputStep.EnteringValue) RequestControlFocus?.Invoke("InputTextBox");
                else if (CurrentStep == InputStep.EnteringAppPath) RequestControlFocus?.Invoke("AppPathTextBox");
            }
        }

        private void BrowseApp()
        {
            try
            {
                string? initialDirectory = GetContextDirectory();
                var dialog = new Microsoft.Win32.OpenFileDialog();
                dialog.Filter = "Executable Files (*.exe)|*.exe|All Files (*.*)|*.*";
                if (!string.IsNullOrEmpty(initialDirectory)) dialog.InitialDirectory = initialDirectory;
                
                if (dialog.ShowDialog() == true)
                {
                    AppPath = dialog.FileName;
                }
            }
            finally
            {
                RequestControlFocus?.Invoke("AppPathTextBox");
            }
        }

        private void PopulateRegisteredApps()
        {
            DisplayCommands.Clear();
            DisplayCommands.Add(new CommandEntry { Name = "選択なし", Value = "NONE", Type = CommandType.Command });
            foreach (var app in _appSettings.RegisteredApps)
            {
                DisplayCommands.Add(new CommandEntry { Name = app.Name, Value = app.Path, Type = CommandType.Command });
            }
            DisplayCommands.Add(new CommandEntry { Name = "+ アプリを追加...", Value = "ADD_REGISTERED_APP", Type = CommandType.Command });
            SelectedItem = DisplayCommands.FirstOrDefault();
        }

        private void AddRegisteredApp()
        {
            try
            {
                var dialog = new Microsoft.Win32.OpenFileDialog();
                dialog.Title = "登録するアプリを選択してください";
                dialog.Filter = "Executable Files (*.exe)|*.exe|All Files (*.*)|*.*";

                string programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
                if (Directory.Exists(programFiles)) dialog.InitialDirectory = programFiles;

                if (dialog.ShowDialog() == true)
                {
                    RegisterAppFromPath(dialog.FileName);
                    AppPath = dialog.FileName;
                    // アプリ追加後、即座に決定せずリストを更新して選択状態にする
                    PopulateRegisteredApps();
                    SelectedItem = DisplayCommands.FirstOrDefault(c => c.Value == dialog.FileName);
                }
            }
            finally
            {
                RequestControlFocus?.Invoke("AppPathTextBox");
            }
        }

        private void DeleteRegisteredApp(CommandEntry? command)
        {
            if (command == null || command.Value == "NONE" || command.Value == "ADD_REGISTERED_APP") return;

            var appToRemove = _appSettings.RegisteredApps.FirstOrDefault(a => a.Path == command.Value);
            if (appToRemove != null)
            {
                _appSettings.RegisteredApps.Remove(appToRemove);
                _configService.SaveSettings(_appSettings);
                PopulateRegisteredApps();
            }
        }

        private void EditRegisteredApp(CommandEntry? command)
        {
            if (command == null || command.Value == "NONE" || command.Value == "ADD_REGISTERED_APP") return;

            try
            {
                var dialog = new Microsoft.Win32.OpenFileDialog();
                dialog.Title = $"{command.Name} の実行ファイルを選び直してください";
                dialog.Filter = "Executable Files (*.exe)|*.exe|All Files (*.*)|*.*";
                dialog.FileName = command.Value;

                if (dialog.ShowDialog() == true)
                {
                    var appToEdit = _appSettings.RegisteredApps.FirstOrDefault(a => a.Path == command.Value);
                    if (appToEdit != null)
                    {
                        appToEdit.Path = dialog.FileName;
                        try {
                            var info = System.Diagnostics.FileVersionInfo.GetVersionInfo(dialog.FileName);
                            appToEdit.Name = !string.IsNullOrWhiteSpace(info.ProductName) ? info.ProductName : System.IO.Path.GetFileNameWithoutExtension(dialog.FileName);
                        } catch {
                            appToEdit.Name = System.IO.Path.GetFileNameWithoutExtension(dialog.FileName);
                        }
                        _configService.SaveSettings(_appSettings);
                        PopulateRegisteredApps();
                        SelectedItem = DisplayCommands.FirstOrDefault(c => c.Value == dialog.FileName);
                    }
                }
            }
            finally
            {
                RequestControlFocus?.Invoke("AppPathTextBox");
            }
        }

        private void RegisterAppFromPath(string? appPath)
        {
            if (string.IsNullOrWhiteSpace(appPath)) return;

            string displayName;
            try
            {
                var info = System.Diagnostics.FileVersionInfo.GetVersionInfo(appPath);
                displayName = !string.IsNullOrWhiteSpace(info.ProductName)
                    ? info.ProductName
                    : System.IO.Path.GetFileNameWithoutExtension(appPath);
            }
            catch
            {
                displayName = System.IO.Path.GetFileNameWithoutExtension(appPath);
            }

            if (!_appSettings.RegisteredApps.Any(a => a.Path.Equals(appPath, StringComparison.OrdinalIgnoreCase)))
            {
                _appSettings.RegisteredApps.Add(new RegisteredApp { Name = displayName, Path = appPath });
                _configService.SaveSettings(_appSettings);
            }
        }

        private string? GetContextDirectory()
        {
            string path = (CurrentStep == InputStep.EnteringAppPath) ? AppPath : InputText;
            if (string.IsNullOrWhiteSpace(path)) return null;

            try
            {
                if (Directory.Exists(path)) return path;
                if (File.Exists(path)) return Path.GetDirectoryName(path);

                string? dir = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(dir) && Directory.Exists(dir)) return dir;
            }
            catch { }
            return null;
        }

        private CancellationTokenSource? _searchCts;
        private async void PerformIncrementalSearch(string query)
        {
            _searchCts?.Cancel();
            _searchCts = new CancellationTokenSource();
            var ct = _searchCts.Token;

            if (string.IsNullOrWhiteSpace(query))
            {
                if (_newEntryType == CommandType.WindowSwitcher)
                {
                    // Show all windows if query is empty for Window type
                    var allTitles = _windowService.GetAllVisibleWindowTitles();
                    DisplayCommands.Clear();
                    foreach (var t in allTitles)
                    {
                        DisplayCommands.Add(new CommandEntry { Name = t, Value = "TYPE_TEMPLATE", Type = CommandType.Command });
                    }
                }
                else
                {
                    DisplayCommands.Clear();
                }
                OnPropertyChanged(nameof(IsListBoxVisible));
                return;
            }

            if (_newEntryType == CommandType.WindowSwitcher)
            {
                // Window search logic
                var allTitles = _windowService.GetAllVisibleWindowTitles();
                var filtered = allTitles.Where(t => t.Contains(query, StringComparison.OrdinalIgnoreCase)).ToList();
                
                DisplayCommands.Clear();
                foreach (var t in filtered)
                {
                    DisplayCommands.Add(new CommandEntry { Name = t, Value = "TYPE_TEMPLATE", Type = CommandType.Command });
                }
                OnPropertyChanged(nameof(IsListBoxVisible));
                return;
            }

            try
            {
                await Task.Delay(150, ct);

                var rootsToSearch = new List<string>();
                string filter;

                if (Path.IsPathRooted(query))
                {
                    string root;
                    if (query.EndsWith(Path.DirectorySeparatorChar) || query.EndsWith(Path.AltDirectorySeparatorChar))
                    {
                        root = query;
                        filter = string.Empty;
                    }
                    else
                    {
                        root = Path.GetDirectoryName(query) ?? (query.Length >= 2 ? query.Substring(0, 3) : "C:\\");
                        filter = Path.GetFileName(query) ?? string.Empty;
                    }
                    if (Directory.Exists(root)) rootsToSearch.Add(root);
                }
                else
                {
                    string userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                    if (Directory.Exists(userProfile)) rootsToSearch.Add(userProfile);

                    rootsToSearch.Add(AppDomain.CurrentDomain.BaseDirectory);
                    rootsToSearch.Add(Environment.GetFolderPath(Environment.SpecialFolder.Desktop));
                    rootsToSearch.Add(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments));

                    string currentDir = Directory.GetCurrentDirectory();
                    if (!rootsToSearch.Contains(currentDir)) rootsToSearch.Add(currentDir);

                    filter = query;
                }

                if (rootsToSearch.Count == 0)
                {
                    var drives = DriveInfo.GetDrives().Where(d => d.IsReady).ToList();
                    DisplayCommands.Clear();
                    foreach (var d in drives) DisplayCommands.Add(new CommandEntry { Name = d.Name, Value = "TYPE_TEMPLATE", Type = CommandType.Command });
                    OnPropertyChanged(nameof(IsListBoxVisible));
                    return;
                }

                var results = await Task.Run(() =>
                {
                    var found = new List<CommandEntry>();
                    var queue = new Queue<string>();
                    foreach (var root in rootsToSearch)
                    {
                        if (Directory.Exists(root)) queue.Enqueue(root);
                    }
                    int dirCount = 0;

                    var blacklist = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                    {
                        "Windows", "$Recycle.Bin", "System Volume Information", "ProgramData"
                    };

                    while (queue.Count > 0 && found.Count < 30 && dirCount < 1000)
                    {
                        if (ct.IsCancellationRequested) return found;
                        var currentDir = queue.Dequeue();
                        dirCount++;

                        try
                        {
                            foreach (var entry in Directory.EnumerateFileSystemEntries(currentDir))
                            {
                                if (ct.IsCancellationRequested) return found;
                                string name = Path.GetFileName(entry);

                                if (blacklist.Contains(name)) continue;

                                if (name.Contains(filter, StringComparison.OrdinalIgnoreCase))
                                {
                                    found.Add(new CommandEntry { Name = entry, Value = "TYPE_TEMPLATE", Type = CommandType.Command });
                                    if (found.Count >= 30) return found;
                                }

                                if (Directory.Exists(entry))
                                {
                                    queue.Enqueue(entry);
                                }
                            }
                        }
                        catch { /* Access denied */ }
                    }
                    return found;
                }, ct);

                if (ct.IsCancellationRequested) return;

                if (CurrentStep != InputStep.EnteringValue) return;

                int? previousIndex = SelectedItem != null ? DisplayCommands.IndexOf(SelectedItem) : (int?)null;
                DisplayCommands.Clear();
                foreach (var res in results) DisplayCommands.Add(res);

                if (previousIndex.HasValue)
                {
                    if (previousIndex.Value < DisplayCommands.Count && previousIndex.Value >= 0)
                        SelectedItem = DisplayCommands[previousIndex.Value];
                    else if (DisplayCommands.Count > 0)
                        SelectedItem = DisplayCommands.Last();
                    else
                        SelectedItem = null;
                }
                else
                {
                    SelectedItem = null;
                }
            }
            catch (Exception) { /* Ignored */ }
            finally
            {
                OnPropertyChanged(nameof(IsListBoxVisible));
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
