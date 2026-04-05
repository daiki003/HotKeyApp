using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using System.Linq;
using System.IO;
using HotKeyCommandApp.Models;
using HotKeyCommandApp.Services;
using Newtonsoft.Json;

namespace HotKeyCommandApp.ViewModels
{
    public enum InputStep
    {
        EnteringName,
        SelectingType,
        EnteringValue,
        EnteringAppPath,
        None
    }

    public class CommandTypeOption
    {
        public string Name { get; set; } = string.Empty;
        public CommandType Type { get; set; }
    }

    public class MainViewModel : INotifyPropertyChanged
    {
        private readonly ConfigService _configService;
        private readonly ActionRunner _actionRunner;
        private readonly WindowService _windowService;
        private AppSettings _appSettings;
        public AppSettings AppSettings => _appSettings;

        private readonly Stack<(CommandEntry? Parent, CommandEntry? LastSelected, string Title)> _navigationHistory = new();
        private readonly List<NavigationState> _history = new();
        private int _historyIndex = -1;
        private bool _isNavigatingHistory = false;
        private System.Threading.CancellationTokenSource? _iconLoadCts;

        private class NavigationState
        {
            public string Title { get; init; } = "";
            public CommandEntry? SelectedItem { get; init; }
            public CommandType? SelectedType { get; init; } // 入力モード用
            public List<(CommandEntry? Parent, CommandEntry? LastSelected, string Title)> NavigationStack { get; init; } = new();

            // どの親を表示していたかの参照
            public CommandEntry? ParentMenu { get; init; }
            public bool IsRoot { get; init; }

            // 入力モード
            public bool IsInputMode { get; init; }
            public InputStep CurrentStep { get; init; }
            public string InputText { get; init; } = "";
            public string InputPrompt { get; init; } = "";
            public CommandEntry? EditingCommand { get; init; }
            public string NewEntryName { get; init; } = "";
            public CommandType NewEntryType { get; init; }
        }

        private List<CommandEntry> _rootCommands = new();
        public ObservableCollection<CommandEntry> DisplayCommands { get; } = new();

        private string _title = "Quick Actions";
        public string Title
        {
            get => _title;
            set { _title = value; OnPropertyChanged(); }
        }

        private bool _canGoBack;
        public bool CanGoBack
        {
            get => _canGoBack;
            set { _canGoBack = value; OnPropertyChanged(); }
        }

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

        private CommandEntry? _currentParent;
        public CommandEntry? CurrentParent
        {
            get => _currentParent;
            set { _currentParent = value; OnPropertyChanged(); }
        }

        private bool _isInputMode;
        public bool IsInputMode
        {
            get => _isInputMode;
            set
            {
                _isInputMode = value;
                OnPropertyChanged();
                UpdateListBoxVisibility();
            }
        }

        private bool _isListBoxVisible = true;
        public bool IsListBoxVisible
        {
            get => _isListBoxVisible;
            set { _isListBoxVisible = value; OnPropertyChanged(); }
        }

        private bool _isOnHierarchyButton;
        public bool IsOnHierarchyButton
        {
            get => _isOnHierarchyButton;
            set { _isOnHierarchyButton = value; OnPropertyChanged(); }
        }

        public CommandEntry? TargetHierarchyItem { get; private set; }
        public int LastMoveDirection { get; private set; }

        private void UpdateListBoxVisibility()
        {
            // ListBox is visible if NOT in InputMode, OR if in FileSearchMode, OR if we're picking a Type (shared with entering name)
            // OR if we have hints/results to show during InputMode
            // But NOT if we are in SettingsMode
            IsListBoxVisible = (!IsInputMode || IsFileSearchMode || CurrentStep == InputStep.EnteringName || (IsInputMode && DisplayCommands.Count > 0));
            OnPropertyChanged(nameof(IsListBoxVisible));
        }

        private string _inputText = string.Empty;
        public string InputText
        {
            get => _inputText;
            set
            {
                _inputText = value;
                OnPropertyChanged();
                if (IsFileSearchMode) PerformFileSearch(value);
                else if (IsInputMode && CurrentStep == InputStep.EnteringValue) PerformIncrementalSearch(value);
            }
        }

        private string _inputPrompt = string.Empty;

        public List<CommandTypeOption> AvailableCommandTypes { get; } = new()
        {
            new CommandTypeOption { Name = "URLを開く", Type = CommandType.URL },
            new CommandTypeOption { Name = "フォルダを開く", Type = CommandType.Folder },
            new CommandTypeOption { Name = "バッチ実行", Type = CommandType.Batch },
            new CommandTypeOption { Name = "ファイルを開く", Type = CommandType.File },
            new CommandTypeOption { Name = "ウィンドウを前面へ", Type = CommandType.Window },
            new CommandTypeOption { Name = "階層の親", Type = CommandType.Menu }
        };

        public bool ShowArgumentOption => CurrentStep == InputStep.EnteringValue && (_newEntryType == CommandType.URL || _newEntryType == CommandType.Folder || _newEntryType == CommandType.Batch || _newEntryType == CommandType.File);
        public bool ShowFileSearchOption => (CurrentStep == InputStep.EnteringValue || CurrentStep == InputStep.EnteringAppPath) && (_newEntryType == CommandType.Folder || _newEntryType == CommandType.File);
        public bool ShowAppPathInput => false; // Hide manual input field for app path
        public bool ShowMainBrowseButton => CurrentStep == InputStep.EnteringValue && (_newEntryType == CommandType.Folder || _newEntryType == CommandType.File || _newEntryType == CommandType.Batch);
        public bool ShowValueInput => CurrentStep == InputStep.EnteringName || CurrentStep == InputStep.EnteringValue || IsFileSearchMode;

        public bool IsSelectingType => false; // Obsolete but kept for safety if referenced elsewhere temporarily

        public string InputPrompt
        {
            get => _inputPrompt;
            set { _inputPrompt = value; OnPropertyChanged(); }
        }

        public event Action<string>? RequestControlFocus;

        private InputStep _currentStep = InputStep.None;
        public InputStep CurrentStep
        {
            get => _currentStep;
            set
            {
                _currentStep = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(ShowArgumentOption));
                OnPropertyChanged(nameof(ShowFileSearchOption));
                OnPropertyChanged(nameof(ShowValueInput));
                OnPropertyChanged(nameof(ShowAppPathInput));
                OnPropertyChanged(nameof(ShowMainBrowseButton));
                OnPropertyChanged(nameof(IsSelectingType));
                UpdateListBoxVisibility();

                // Signal focus based on step
                switch (value)
                {
                    case InputStep.EnteringName: RequestControlFocus?.Invoke("InputTextBox"); break;
                    case InputStep.EnteringValue: RequestControlFocus?.Invoke("InputTextBox"); break;
                    case InputStep.EnteringAppPath: RequestControlFocus?.Invoke("CommandListBox"); break;
                }
            }
        }

        public event Action<CommandEntry>? RequestShortcutAssignment;
        public event Action<CommandEntry>? RequestDeleteConfirmation;
        public event Action<CommandEntry?, double, double>? RequestButtonCreation;

        private string _newEntryName = string.Empty;
        private CommandType _newEntryType;
        private CommandEntry? _editingCommand; // null = 新規追加, not null = 既存編集

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

        public event Func<CommandEntry, string, string?>? RequestArgumentInput;
        public event Action? RequestSettings;

        private string _appPath = string.Empty;
        public string AppPath
        {
            get => _appPath;
            set
            {
                _appPath = value;
                OnPropertyChanged();
                // Do not perform search on AppPath anymore
            }
        }

        private bool _isFileSearchMode;
        public bool IsFileSearchMode
        {
            get => _isFileSearchMode;
            set
            {
                _isFileSearchMode = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(ShowValueInput));
                UpdateListBoxVisibility();
            }
        }

        private CommandEntry? _targetFolderCommand;
        private List<CommandEntry> _preSearchCommands = new();
        private System.Threading.CancellationTokenSource? _searchCts;


        public double WindowWidth
        {
            get => _appSettings.WindowWidth;
            set { _appSettings.WindowWidth = value; OnPropertyChanged(); }
        }

        public double WindowHeight
        {
            get => _appSettings.WindowHeight;
            set { _appSettings.WindowHeight = value; OnPropertyChanged(); }
        }

        public double WindowTop
        {
            get => _appSettings.WindowTop;
            set { _appSettings.WindowTop = value; OnPropertyChanged(); }
        }

        public double WindowLeft
        {
            get => _appSettings.WindowLeft;
            set { _appSettings.WindowLeft = value; OnPropertyChanged(); }
        }

        public double ButtonWidth
        {
            get => _appSettings.ButtonWidth;
            set { _appSettings.ButtonWidth = value; OnPropertyChanged(); }
        }

        public double ButtonHeight
        {
            get => _appSettings.ButtonHeight;
            set { _appSettings.ButtonHeight = value; OnPropertyChanged(); }
        }

        public double MovementSpeed
        {
            get => _appSettings.MovementSpeed;
            set { _appSettings.MovementSpeed = value; OnPropertyChanged(); }
        }

        public double FontSize
        {
            get => _appSettings.FontSize;
            set { _appSettings.FontSize = value; OnPropertyChanged(); }
        }

        public string GlobalHotkey
        {
            get => _appSettings.GlobalHotkey;
            set { _appSettings.GlobalHotkey = value; OnPropertyChanged(); }
        }

        private double _editingMovementSpeed;
        public double EditingMovementSpeed
        {
            get => _editingMovementSpeed;
            set { _editingMovementSpeed = value; OnPropertyChanged(); }
        }

        private double _editingFontSize;
        public double EditingFontSize
        {
            get => _editingFontSize;
            set { _editingFontSize = value; OnPropertyChanged(); }
        }

        private string _editingGlobalHotkey = string.Empty;
        public string EditingGlobalHotkey
        {
            get => _editingGlobalHotkey;
            set { _editingGlobalHotkey = value; OnPropertyChanged(); }
        }

        private bool _isCapturingHotkey;
        public bool IsCapturingHotkey
        {
            get => _isCapturingHotkey;
            set { _isCapturingHotkey = value; OnPropertyChanged(); }
        }

        private double _editingButtonWidth;
        public double EditingButtonWidth
        {
            get => _editingButtonWidth;
            set { _editingButtonWidth = value; OnPropertyChanged(); }
        }

        private double _editingButtonHeight;
        public double EditingButtonHeight
        {
            get => _editingButtonHeight;
            set { _editingButtonHeight = value; OnPropertyChanged(); }
        }



        private ICommand? _executeCommand;
        public ICommand ExecuteCommand => _executeCommand ??= new RelayCommand<CommandEntry>(Execute);

        private ICommand? _goBackCommand;
        public ICommand GoBackCommand => _goBackCommand ??= new RelayCommand<object>(_ => GoBack());

        private ICommand? _moveRightCommand;
        public ICommand MoveRightCommand => _moveRightCommand ??= new RelayCommand<object>(_ => MoveRight());

        private ICommand? _moveLeftCommand;
        public ICommand MoveLeftCommand => _moveLeftCommand ??= new RelayCommand<object>(_ => MoveLeft());

        private ICommand? _commitInputCommand;
        public ICommand CommitInputCommand => _commitInputCommand ??= new RelayCommand<object>(_ => CommitInput());

        private ICommand? _cancelInputCommand;
        public ICommand CancelInputCommand => _cancelInputCommand ??= new RelayCommand<object>(_ => CancelInput());

        private ICommand? _deleteCommand;
        public ICommand DeleteCommand => _deleteCommand ??= new RelayCommand<CommandEntry>(Delete);

        private ICommand? _startShortcutAssignmentCommand;
        public ICommand StartShortcutAssignmentCommand => _startShortcutAssignmentCommand ??= new RelayCommand<CommandEntry>(StartShortcutAssignment);

        private ICommand? _cancelShortcutAssignmentCommand;
        public ICommand CancelShortcutAssignmentCommand => _cancelShortcutAssignmentCommand ??= new RelayCommand<object>(_ => CancelShortcutAssignment());

        private ICommand? _confirmDeleteCommand;
        public ICommand ConfirmDeleteCommand => _confirmDeleteCommand ??= new RelayCommand<CommandEntry>(ConfirmDelete);

        private ICommand? _cancelDeleteCommand;
        public ICommand CancelDeleteCommand => _cancelDeleteCommand ??= new RelayCommand<object>(_ => CancelDelete());

        private ICommand? _moveUpCommand;
        public ICommand MoveUpCommand => _moveUpCommand ??= new RelayCommand<CommandEntry>(MoveUp);

        private ICommand? _moveDownCommand;
        public ICommand MoveDownCommand => _moveDownCommand ??= new RelayCommand<CommandEntry>(MoveDown);

        private ICommand? _openSettingsCommand;
        public ICommand OpenSettingsCommand => _openSettingsCommand ??= new RelayCommand<object>(_ => OpenSettings());

        private ICommand? _saveSettingsCommand;
        public ICommand SaveSettingsCommand => _saveSettingsCommand ??= new RelayCommand<object>(_ => SaveSettings());

        private ICommand? _deleteRegisteredAppCommand;
        public ICommand DeleteRegisteredAppCommand => _deleteRegisteredAppCommand ??= new RelayCommand<CommandEntry>(DeleteRegisteredApp);

        private ICommand? _editRegisteredAppCommand;
        public ICommand EditRegisteredAppCommand => _editRegisteredAppCommand ??= new RelayCommand<CommandEntry>(EditRegisteredApp);

        private ICommand? _duplicateCommand;
        public ICommand DuplicateCommand => _duplicateCommand ??= new RelayCommand<CommandEntry>(Duplicate);

        private bool _isDialogActive;
        public bool IsDialogActive
        {
            get => _isDialogActive;
            set { _isDialogActive = value; OnPropertyChanged(); }
        }

        public ICommand StartEditCommand => new RelayCommand<CommandEntry>(c => { RequestButtonCreation?.Invoke(c, WindowWidth, WindowHeight); });
        public ICommand NavigateBackCommand => new RelayCommand<object>(_ => NavigateBack());
        public ICommand NavigateForwardCommand => new RelayCommand<object>(_ => NavigateForward());
        public ICommand AddNewButtonCommand => new RelayCommand<object>(_ => { RequestButtonCreation?.Invoke(null, WindowWidth, WindowHeight); });

        private string? GetContextDirectory()
        {
            // Use current input/path as starting directory context
            string path = (CurrentStep == InputStep.EnteringAppPath) ? AppPath : InputText;
            if (string.IsNullOrWhiteSpace(path)) return null;

            try
            {
                if (Directory.Exists(path)) return path;
                if (File.Exists(path)) return Path.GetDirectoryName(path);

                // Check parent directory if partial path is entered
                string? dir = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(dir) && Directory.Exists(dir)) return dir;
            }
            catch { }
            return null;
        }

        private bool CanBrowseFile()
        {
            return CurrentStep == InputStep.EnteringValue && (_newEntryType == CommandType.Folder || _newEntryType == CommandType.File || _newEntryType == CommandType.Batch);
        }

        private bool CanBrowseApp()
        {
            return CurrentStep == InputStep.EnteringAppPath;
        }

        private void BrowseFile(string? initialDirectory = null)
        {
            IsDialogActive = true;
            try
            {
                if (_newEntryType == CommandType.Folder)
                {
                    var dialog = new Microsoft.Win32.OpenFolderDialog();
                    if (!string.IsNullOrEmpty(initialDirectory)) dialog.InitialDirectory = initialDirectory;
                    if (dialog.ShowDialog() == true)
                    {
                        InputText = dialog.FolderName;
                    }
                }
                else
                {
                    var dialog = new Microsoft.Win32.OpenFileDialog();
                    if (!string.IsNullOrEmpty(initialDirectory)) dialog.InitialDirectory = initialDirectory;
                    if (dialog.ShowDialog() == true)
                    {
                        InputText = dialog.FileName;
                    }
                }
            }
            finally
            {
                IsDialogActive = false;
                if (CurrentStep == InputStep.EnteringValue) RequestControlFocus?.Invoke("InputTextBox");
                else if (CurrentStep == InputStep.EnteringAppPath) RequestControlFocus?.Invoke("AppPathTextBox");
            }
        }

        private void BrowseApp(string? initialDirectory = null)
        {
            IsDialogActive = true;
            try
            {
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
                IsDialogActive = false;
                RequestControlFocus?.Invoke("AppPathTextBox");
            }
        }

        /// <summary>登録済みアプリ一覧をDisplayCommandsにセットする</summary>
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

        /// <summary>ファイルダイアログからアプリを選択して登録済みリストに追加する</summary>
        private void AddRegisteredApp()
        {
            IsDialogActive = true;
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
                IsDialogActive = false;
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

            IsDialogActive = true;
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
                        // パスを更新
                        appToEdit.Path = dialog.FileName;
                        
                        // 名前も更新（オプション）
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
                IsDialogActive = false;
                RequestControlFocus?.Invoke("AppPathTextBox");
            }
        }


        public MainViewModel()
        {
            _configService = new ConfigService();
            _actionRunner = new ActionRunner();
            _windowService = new WindowService();
            _appSettings = _configService.LoadSettings();
            LoadCommands();
        }

        public void LoadCommands()
        {
            _rootCommands = _configService.LoadCommands();

            // Clean up commands recursively
            CleanUpCommands(_rootCommands);

            _navigationHistory.Clear();
            UpdateDisplay(_rootCommands, "Quick Actions");
            UpdateGlobalHotkeys();
        }

        private void CleanUpCommands(List<CommandEntry> list)
        {
            foreach (var item in list)
            {


                if (item.Children != null)
                {
                    CleanUpCommands(item.Children);
                }
            }

            // Clean up any "Add Button" or system commands that shouldn't be persisted
            list.RemoveAll(c => c.Type == CommandType.Command && (c.Value?.StartsWith("ADD_") ?? false));

            foreach (var item in list)
            {
                if (item.Children != null)
                {
                    CleanUpCommands(item.Children);
                }
            }
        }

        private void UpdateDisplay(List<CommandEntry> commands, string title, CommandEntry? itemToSelect = null, CommandEntry? parent = null)
        {
            // Cancel previous icon loading
            _iconLoadCts?.Cancel();
            _iconLoadCts = new System.Threading.CancellationTokenSource();
            var ct = _iconLoadCts.Token;

            DisplayCommands.Clear();
            var windowCommands = new List<CommandEntry>();

            foreach (var item in commands)
            {
                DisplayCommands.Add(item);
                if (item.Type == CommandType.Window && !string.IsNullOrEmpty(item.Value))
                {
                    windowCommands.Add(item);
                }
            }

            // Ensure "Add Button" for hierarchy levels (if not in special input/search modes)
            if (!IsInputMode && !IsFileSearchMode)
            {
                if (!DisplayCommands.Any(c => c.Type == CommandType.Command && (c.Value?.StartsWith("ADD_") ?? false)))
                {
                    DisplayCommands.Add(new CommandEntry { Name = "ボタン追加", Type = CommandType.Command, Value = "ADD_BUTTON" });
                }
            }

            Title = title;
            CurrentParent = parent;
            CanGoBack = _navigationHistory.Count > 0;
            SelectedItem = itemToSelect ?? ((IsInputMode && CurrentStep != InputStep.EnteringName) ? null : DisplayCommands.FirstOrDefault());

            // Load icons asynchronously
            if (windowCommands.Any())
            {
                Task.Run(async () =>
                {
                    foreach (var item in windowCommands)
                    {
                        if (ct.IsCancellationRequested) break;

                        try
                        {
                            var icon = _windowService.GetWindowIconSource(item.Value);
                            if (icon != null && !ct.IsCancellationRequested)
                            {
                                System.Windows.Application.Current.Dispatcher.Invoke(() =>
                                {
                                    if (!ct.IsCancellationRequested)
                                    {
                                        item.IconSource = icon;
                                    }
                                });
                            }
                        }
                        catch (Exception ex)
                        {
                            System.Diagnostics.Debug.WriteLine($"Failed to get icon for {item.Name}: {ex.Message}");
                        }

                        // Add a tiny delay between requests to keep system snappy
                        await Task.Delay(10, ct);
                    }
                }, ct);
            }

            RecordHistory();
            RequestSync?.Invoke();
        }

        private void RecordHistory()
        {
            if (_isNavigatingHistory) return;

            // 現在のインデックス以降を削除
            if (_historyIndex >= 0 && _historyIndex < _history.Count - 1)
            {
                _history.RemoveRange(_historyIndex + 1, _history.Count - (_historyIndex + 1));
            }

            // 現在の「親」を特定
            CommandEntry? parent = null;
            bool isRoot = false;
            if (!IsInputMode && !IsFileSearchMode && !IsDialogActive)
            {
                if (_navigationHistory.Count > 0)
                {
                    // スタックのトップにある「次に実行したコマンド」の親…ではなく、
                    // 現在表示されているリストの「親」は _navigationHistory の Peek() にある情報の CommandEntry 本体
                    parent = _navigationHistory.Peek().Parent;
                }
                else
                {
                    isRoot = (Title == "Quick Actions");
                }
            }

            var state = new NavigationState
            {
                Title = Title,
                SelectedItem = SelectedItem,
                SelectedType = (IsInputMode && SelectedItem != null) ? SelectedItem.Type : (CommandType?)null,
                NavigationStack = _navigationHistory.ToList(),
                ParentMenu = parent,
                IsRoot = isRoot,
                IsInputMode = IsInputMode,
                CurrentStep = CurrentStep,
                InputText = InputText,
                InputPrompt = InputPrompt,
                EditingCommand = _editingCommand,
                NewEntryName = _newEntryName,
                NewEntryType = _newEntryType
            };

            // 直前の履歴と同じ状態なら記録しない
            if (_history.Count > 0)
            {
                var last = _history.Last();
                if (last.Title == state.Title &&
                    last.CurrentStep == state.CurrentStep &&
                    last.IsInputMode == state.IsInputMode &&
                    last.InputText == state.InputText &&
                    last.SelectedItem == state.SelectedItem)
                {
                    return;
                }
            }

            _history.Add(state);
            _historyIndex++;

            if (_history.Count > 50)
            {
                _history.RemoveAt(0);
                _historyIndex--;
            }
        }

        public void Execute(CommandEntry? command)
        {
            if (command == null) return;

            // 1. Check for CommandType.Command (Menu items like "ADD_BUTTON", etc.)
            if (command.Type == CommandType.Command)
            {
                if (command.Value == "TYPE_TEMPLATE")
                {
                    // Hint selection logic
                    if (CurrentStep == InputStep.EnteringValue) InputText = command.Name;
                    else if (CurrentStep == InputStep.EnteringAppPath) AppPath = command.Name;

                    if ((Directory.Exists(command.Name) || (command.Name.EndsWith(Path.DirectorySeparatorChar) && Directory.Exists(Path.GetDirectoryName(command.Name))))
                        && !command.Name.EndsWith(Path.DirectorySeparatorChar))
                    {
                        string val = command.Name + Path.DirectorySeparatorChar;
                        if (CurrentStep == InputStep.EnteringValue) InputText = val;
                        else if (CurrentStep == InputStep.EnteringAppPath) AppPath = val;
                    }
                    return;
                }

                if (command.Value == "NONE" ||
                    command.Value == "ADD_REGISTERED_APP" ||
                    command.Value == "REGISTERED_APP" ||
                    command.Value == "BROWSE_EXPLORER" ||
                    command.Value == "INSTALLED_APP")
                {
                    SelectedItem = command;
                    CommitInput();
                    return;
                }

                if (command.Value == "ADD_BUTTON")
                {
                    RequestButtonCreation?.Invoke(null, WindowWidth, WindowHeight);
                    return;
                }
                return;
            }

            // 2. Check for Menu and enter it
            if (command.Type == CommandType.Menu && command.Children != null)
            {
                if (IsFileSearchMode) CancelInput(); // Exit search if navigating

                // Push the PARENT of the CURRENT display list
                _navigationHistory.Push((CurrentParent, SelectedItem, Title));
                UpdateDisplay(command.Children, command.Name, null, command);
                RequestShow?.Invoke();
                return;
            }

            // 3. Handle File Search
            if (command.IsFileSearchEnabled && !IsFileSearchMode)
            {
                _targetFolderCommand = command;
                _preSearchCommands = DisplayCommands.ToList();
                IsFileSearchMode = true;
                InputPrompt = $"[{command.Name}] 内を検索:";
                InputText = string.Empty;
                IsInputMode = true;
                RequestSync?.Invoke();
                return;
            }

            // 4. Handle RequiresArgument (Modal Dialog)
            if (command.RequiresArgument)
            {
                string? argument = RequestArgumentInput?.Invoke(command, $"[{command.Name}] の引数を入力してください:");
                if (argument == null) return; // Canceled

                // Update history
                if (!string.IsNullOrWhiteSpace(argument))
                {
                    command.ArgumentsHistory.Remove(argument); // Remove duplicate if exists
                    command.ArgumentsHistory.Insert(0, argument);
                    if (command.ArgumentsHistory.Count > 10) // Limit to 10 items
                    {
                        command.ArgumentsHistory.RemoveAt(command.ArgumentsHistory.Count - 1);
                    }
                    _configService.SaveCommands(_rootCommands);
                }

                RunActionAndHandleVisibility(command, argument);
                return;
            }

            // 5. Run the actual action
            RunActionAndHandleVisibility(command);
        }

        private void RunActionAndHandleVisibility(CommandEntry command, string? argument = null)
        {
            if (_actionRunner.Run(command, argument))
            {
                ResetToRoot();
                RequestHide?.Invoke();
            }
            else
            {
                RequestShow?.Invoke();
            }
        }

        private async void PerformIncrementalSearch(string query)
        {
            _searchCts?.Cancel();
            _searchCts = new System.Threading.CancellationTokenSource();
            var ct = _searchCts.Token;

            if (string.IsNullOrWhiteSpace(query))
            {
                DisplayCommands.Clear();
                UpdateListBoxVisibility();
                return;
            }

            try
            {
                // Debounce search by 150ms to prevent lag while typing fast
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
                    // Add common search roots
                    string userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                    if (Directory.Exists(userProfile)) rootsToSearch.Add(userProfile);

                    rootsToSearch.Add(AppDomain.CurrentDomain.BaseDirectory);
                    rootsToSearch.Add(Environment.GetFolderPath(Environment.SpecialFolder.Desktop));
                    rootsToSearch.Add(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments));

                    // Also include the current directory if it's different from BaseDirectory
                    string currentDir = Directory.GetCurrentDirectory();
                    if (!rootsToSearch.Contains(currentDir)) rootsToSearch.Add(currentDir);

                    filter = query;
                }

                if (rootsToSearch.Count == 0)
                {
                    // Fallback to drives if nothing else found
                    var drives = DriveInfo.GetDrives().Where(d => d.IsReady).ToList();
                    DisplayCommands.Clear();
                    foreach (var d in drives) DisplayCommands.Add(new CommandEntry { Name = d.Name, Value = "TYPE_TEMPLATE", Type = CommandType.Command });
                    UpdateListBoxVisibility();
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

                                // Skip blacklisted or hidden/system folders
                                if (blacklist.Contains(name)) continue;

                                if (name.Contains(filter, StringComparison.OrdinalIgnoreCase))
                                {
                                    found.Add(new CommandEntry { Name = entry, Value = "TYPE_TEMPLATE", Type = CommandType.Command });
                                    if (found.Count >= 30) return found;
                                }

                                if (Directory.Exists(entry))
                                {
                                    // Basic depth check or just ignore massive system folders
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
                    if (previousIndex.Value < DisplayCommands.Count)
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
                UpdateListBoxVisibility();
            }
        }

        private void PerformFileSearch(string query)
        {
            if (_targetFolderCommand == null || string.IsNullOrEmpty(_targetFolderCommand.Value)) return;

            try
            {
                string path = _targetFolderCommand.Value;
                if (!Directory.Exists(path)) return;

                if (string.IsNullOrWhiteSpace(query))
                {
                    UpdateDisplay(new List<CommandEntry>(), $"検索: {query}", null, _targetFolderCommand);
                    return;
                }

                int? previousIndex = SelectedItem != null ? DisplayCommands.IndexOf(SelectedItem) : (int?)null;

                var entries = Directory.EnumerateFileSystemEntries(path)
                    .Select(p => new CommandEntry
                    {
                        Name = Path.GetFileName(p),
                        Value = p,
                        Type = Directory.Exists(p) ? CommandType.Folder : CommandType.URL
                    })
                    .Where(e => e.Name.Contains(query, StringComparison.OrdinalIgnoreCase))
                    .Take(30)
                    .ToList();

                UpdateDisplay(entries, $"検索: {query}", null, _targetFolderCommand);

                if (previousIndex.HasValue)
                {
                    if (previousIndex.Value < DisplayCommands.Count)
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
            catch (Exception) { /* Log or ignore */ }
        }

        private void StartShortcutAssignment(CommandEntry? command)
        {
            if (command == null || command.Type == CommandType.Command) return;
            RequestShortcutAssignment?.Invoke(command);
        }

        public bool ExecuteHotkey(string hotkey)
        {
            // 1. Hierarchy Local (in current display commands)
            var localCommand = DisplayCommands.FirstOrDefault(c => c.Hotkey == hotkey && c.HotkeyType == HotkeyType.HierarchyLocal);
            if (localCommand != null)
            {
                Execute(localCommand);
                return true;
            }

            // 2. Window Global (anywhere in the tree)
            var globalCommand = FindCommandByHotkeyRecursively(_rootCommands, hotkey, HotkeyType.WindowGlobal);
            if (globalCommand != null)
            {
                Execute(globalCommand);
                return true;
            }

            return false;
        }

        private CommandEntry? FindCommandByHotkeyRecursively(List<CommandEntry> commands, string hotkey, HotkeyType type)
        {
            foreach (var command in commands)
            {
                if (command.Hotkey == hotkey && command.HotkeyType == type) return command;
                if (command.Children != null)
                {
                    var found = FindCommandByHotkeyRecursively(command.Children, hotkey, type);
                    if (found != null) return found;
                }
            }
            return null;
        }

        private readonly Dictionary<int, CommandEntry> _globalHotkeyMapping = new();
        public event Action<int, string>? RequestRegisterGlobalHotkey;
        public event Action? RequestUnregisterCommandHotkeys;

        public void ExecuteHotkeyById(int id)
        {
            if (_globalHotkeyMapping.TryGetValue(id, out var command))
            {
                Execute(command);
            }
        }

        public void UpdateGlobalHotkeys()
        {
            RequestUnregisterCommandHotkeys?.Invoke();
            _globalHotkeyMapping.Clear();

            int nextId = 9001; // Start above default ID
            RegisterGlobalHotkeysRecursively(_rootCommands, ref nextId);
        }

        private void RegisterGlobalHotkeysRecursively(List<CommandEntry> commands, ref int nextId)
        {
            foreach (var command in commands)
            {
                if (command.HotkeyType == HotkeyType.Global && !string.IsNullOrEmpty(command.Hotkey))
                {
                    int id = nextId++;
                    _globalHotkeyMapping[id] = command;
                    RequestRegisterGlobalHotkey?.Invoke(id, command.Hotkey);
                }

                if (command.Children != null)
                {
                    RegisterGlobalHotkeysRecursively(command.Children, ref nextId);
                }
            }
        }

        public void AssignShortcut(string? hotkey, HotkeyType type)
        {
            if (SelectedItem != null)
            {
                SelectedItem.Hotkey = hotkey;
                SelectedItem.HotkeyType = type;
                _configService.SaveCommands(_rootCommands);

                // Update global registrations if needed
                UpdateGlobalHotkeys();

                // Refresh display
                var currentList = DisplayCommands.ToList();
                UpdateDisplay(currentList, Title, SelectedItem, CurrentParent);
            }
            CancelShortcutAssignment();
        }

        private void CancelShortcutAssignment()
        {
            RequestSync?.Invoke();
        }

        private void Delete(CommandEntry? command)
        {
            if (command == null) return;

            // Protection: cannot delete system commands (ADD_ buttons)
            if (command.Type == CommandType.Command && command.Value.StartsWith("ADD_")) return;

            // Note: Users should be able to delete their own buttons even if named "再ビルド" or "JSON同期"
            // unless they are for the system reload logic.
            if (command.Value == "reload.bat" && command.Type == CommandType.Batch && (command.Name == "再ビルド" || command.Name == "Reload App")) return;

            RequestDeleteConfirmation?.Invoke(command);
        }

        public void ConfirmDelete(CommandEntry? command)
        {
            if (command != null && RemoveFromCurrentList(command))
            {
                _configService.SaveCommands(_rootCommands);
                var currentList = DisplayCommands.ToList();
                currentList.Remove(command);
                UpdateDisplay(currentList, Title, null, CurrentParent);
                UpdateGlobalHotkeys();
            }
            CancelDelete();
        }

        private void CancelDelete()
        {
            RequestSync?.Invoke();
        }

        private void MoveUp(CommandEntry? command)
        {
            if (command == null) return;
            MoveItem(command, -1);
        }

        private void MoveDown(CommandEntry? command)
        {
            if (command == null) return;
            MoveItem(command, 1);
        }

        public void MoveItem(CommandEntry command, int direction)
        {
            var currentList = DisplayCommands.ToList();
            int index = currentList.IndexOf(command);
            if (index < 0) return;

            int newIndex = index + direction;
            if (newIndex < 0 || newIndex >= currentList.Count) return;

            // System commands at the end (Add... buttons) should not be moved past
            if (currentList[newIndex].Type == CommandType.Command || currentList[index].Type == CommandType.Command)
            {
                // But allow reordering among regular items if they are not system
                if (currentList[newIndex].Type == CommandType.Command && direction > 0) return;
                if (currentList[index].Type == CommandType.Command) return;
            }

            // Hierarchy stop logic
            if (IsOnHierarchyButton)
            {
                if (direction == LastMoveDirection)
                {
                    // Second press in same direction: move past it
                    if (TargetHierarchyItem != null) TargetHierarchyItem.IsTargetedForMove = false;
                    IsOnHierarchyButton = false;
                }
                else
                {
                    // Moved away from the stop point
                    if (TargetHierarchyItem != null) TargetHierarchyItem.IsTargetedForMove = false;
                    IsOnHierarchyButton = false;
                    return; 
                }
            }
            else
            {
                // Check if target is a menu
                if (currentList[newIndex].Type == CommandType.Menu)
                {
                    IsOnHierarchyButton = true;
                    TargetHierarchyItem = currentList[newIndex];
                    TargetHierarchyItem.IsTargetedForMove = true;
                    LastMoveDirection = direction;
                    return; // Stop here
                }
            }

            currentList.RemoveAt(index);
            currentList.Insert(newIndex, command);

            // Update persisted structure
            var targetList = (CurrentParent == null) ? _rootCommands : CurrentParent.Children;
            if (targetList != null)
            {
                targetList.Clear();
                // Filter out system buttons (ADD_BUTTON) when persisting
                targetList.AddRange(currentList.Where(c => c.Type != CommandType.Command || !(c.Value?.StartsWith("ADD_") ?? false)));
            }
            _configService.SaveCommands(_rootCommands);

            // Refresh UI
            UpdateDisplay(currentList, Title, command, CurrentParent);
        }

        private List<CommandEntry> _preInputCommands = new();
        private void SetupTypeSelectionDisplay()
        {
            var types = AvailableCommandTypes.Select(t => new CommandEntry
            {
                Name = t.Name,
                Type = t.Type,
                Value = "TYPE_TEMPLATE" // Marker 
            }).ToList();

            DisplayCommands.Clear();
            foreach (var t in types) DisplayCommands.Add(t);
        }

        public void StartInputFlow()
        {
            _editingCommand = null; // 新規追加モード
            _preInputCommands = DisplayCommands.ToList(); // Backup current list
            _newEntryName = string.Empty;
            InputText = string.Empty;
            AppPath = string.Empty;
            DisplayCommands.Clear();
            IsRequiresArgumentChecked = false;
            IsFileSearchChecked = false;
            IsInputMode = true;

            // Integrated Name/Type Selection: Show types in the ListBox immediately
            CurrentStep = InputStep.EnteringName;
            InputPrompt = "名前を入力し、機能を選んでください:";

            SetupTypeSelectionDisplay();
            SelectedItem = DisplayCommands.FirstOrDefault();
            RecordHistory();
            RequestSync?.Invoke();
        }

        private void StartEditFlow(CommandEntry? command)
        {
            if (command == null) return;
            // システムコマンドは編集不可
            if (command.Type == CommandType.Command) return;

            _editingCommand = command;
            _preInputCommands = DisplayCommands.ToList();
            _newEntryName = command.Name;
            _newEntryType = command.Type;
            IsRequiresArgumentChecked = command.RequiresArgument;
            IsFileSearchChecked = command.IsFileSearchEnabled;
            IsRequiresArgumentChecked = command.RequiresArgument;
            IsFileSearchChecked = command.IsFileSearchEnabled;
            IsInputMode = true;

            // 名前と種別の編集から開始
            CurrentStep = InputStep.EnteringName;
            InputPrompt = "名前を編集し、機能を選んでください:";
            InputText = command.Name;

            SetupTypeSelectionDisplay();
            // 現在の機能にフォーカスを当てる
            SelectedItem = DisplayCommands.FirstOrDefault(c => c.Type == command.Type) ?? DisplayCommands.FirstOrDefault();
            AppPath = command.AppPath ?? string.Empty;
            RecordHistory();
            RequestSync?.Invoke();
        }

        private void CommitInput()
        {
            // Cancel any pending search from the previous step so it doesn't overwrite UI later
            _searchCts?.Cancel();

            if (SelectedItem != null)
            {
                // In file search, execute immediately.
                if (IsFileSearchMode)
                {
                    Execute(SelectedItem);
                    return;
                }

                // In value input (adding button), copy to input field instead of executing directly.
                // Other steps (like selecting function type or selecting app) should execute immediately on Enter.
                if (CurrentStep == InputStep.EnteringValue)
                {
                    // Copy display name (path) to input field for search results
                    // Search results use "TYPE_TEMPLATE" as Value, actual path is in Name.
                    InputText = (SelectedItem.Value == "TYPE_TEMPLATE") ? SelectedItem.Name : (SelectedItem.Value ?? SelectedItem.Name);
                    SelectedItem = null; // Focus returns to InputTextBox
                    return;
                }
            }

            if (IsFileSearchMode && SelectedItem == null)
            {
                if (string.IsNullOrWhiteSpace(InputText)) return;
                // Run the action with the current input as argument
                if (_targetFolderCommand != null)
                {
                    RunActionAndHandleVisibility(_targetFolderCommand, InputText);
                }
                return;
            }

            if (CurrentStep == InputStep.EnteringName)
            {
                if (string.IsNullOrWhiteSpace(InputText)) return;
                if (SelectedItem == null) return;

                _newEntryName = InputText.Trim();
                _newEntryType = SelectedItem.Type;

                if (_newEntryType == CommandType.Menu)
                {
                    InputText = string.Empty;
                    FinishInputFlow();
                }
                else
                {
                    // 編集中の場合は現在の値をデフォルトで入れる
                    InputText = _editingCommand?.Value ?? string.Empty;
                    CurrentStep = InputStep.EnteringValue;
                    InputPrompt = "URLまたはパスを入力してください:";
                    DisplayCommands.Clear(); // 種別選択リストをクリア
                }
                RecordHistory();
                return;
            }

            if (IsInputMode && CurrentStep == InputStep.EnteringValue)
            {
                string path = InputText.Trim();
                bool isFolderHint = false;

                // If a hint is selected, use its path
                if (SelectedItem != null && SelectedItem.Value == "TYPE_TEMPLATE")
                {
                    path = SelectedItem.Name;
                    isFolderHint = Directory.Exists(path);
                }

                if (string.IsNullOrEmpty(path) && _newEntryType != CommandType.Menu) return;

                if (_newEntryType == CommandType.Folder)
                {
                    CurrentStep = InputStep.EnteringAppPath;
                    InputText = path;
                    InputPrompt = "開く際に使用するソフトを選択してください (任意):";
                    PopulateRegisteredApps();
                }
                else if (_newEntryType == CommandType.Batch || _newEntryType == CommandType.File)
                {
                    if (isFolderHint)
                    {
                        // Open file browser at this folder
                        BrowseFile(path);
                        return; // Stay in this step (BrowseFile will update InputText)
                    }
                    else
                    {
                        CurrentStep = InputStep.EnteringAppPath;
                        InputText = path;
                        InputPrompt = "開く際に使用するソフトを選択してください (任意):";
                        PopulateRegisteredApps();
                    }
                }
                else
                {
                    CurrentStep = InputStep.None;
                    InputText = path;
                    FinishInputFlow();
                }
                RecordHistory();
                return;
            }

            if (IsInputMode && CurrentStep == InputStep.EnteringAppPath)
            {
                // If a hint is selected, use its value
                if (SelectedItem != null)
                {
                    if (SelectedItem.Value == "NONE")
                    {
                        AppPath = string.Empty;
                        FinishInputFlow();
                        return;
                    }

                    if (SelectedItem.Value == "ADD_REGISTERED_APP")
                    {
                        AddRegisteredApp();
                        return;
                    }

                    if (SelectedItem.Value == "REGISTERED_APP")
                    {
                        AppPath = SelectedItem.Name; // Name has the display name, store path in Tag via CommandEntry
                        // Actually use the path stored in the entry's value or a custom field
                        // We store it temporarily in Name and use Value to differentiate
                        // Look up the actual path from settings
                        var regApp = _appSettings.RegisteredApps.FirstOrDefault(a => a.Name == SelectedItem.Name);
                        if (regApp != null) AppPath = regApp.Path;
                        FinishInputFlow();
                        return;
                    }

                    if (SelectedItem.Value == "TYPE_TEMPLATE")
                    {
                        string hintPath = SelectedItem.Name;
                        if (Directory.Exists(hintPath))
                        {
                            BrowseApp(hintPath);
                            return;
                        }
                        AppPath = hintPath;
                        FinishInputFlow();
                        return;
                    }
                }

                AppPath = AppPath.Trim();
                FinishInputFlow();
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

        private void FinishInputFlow()
        {
            string val = InputText.Trim();
            // chrome:// などのカスタムプロトコルには https:// を付与しない
            if (_newEntryType == CommandType.URL && !string.IsNullOrEmpty(val)
                && !val.StartsWith("http") && !val.Contains("://"))
            {
                val = "https://" + val;
            }

            if (_editingCommand != null)
            {
                // 編集モード：すべてのプロパティを更新
                _editingCommand.Name = _newEntryName;
                _editingCommand.Type = _newEntryType;
                _editingCommand.Value = val;
                _editingCommand.AppPath = (_newEntryType == CommandType.File || _newEntryType == CommandType.Folder) ? AppPath.Trim() : null;
                _editingCommand.RequiresArgument = IsRequiresArgumentChecked;
                _editingCommand.IsFileSearchEnabled = IsFileSearchChecked;

                // Menuへの変更/解除をハンドル
                if (_editingCommand.Type == CommandType.Menu && _editingCommand.Children == null)
                {
                    _editingCommand.Children = new List<CommandEntry>();
                }
                else if (_editingCommand.Type != CommandType.Menu)
                {
                    _editingCommand.Children = null;
                }

                _configService.SaveCommands(_rootCommands);

                var currentList = _preInputCommands.ToList();
                UpdateDisplay(currentList, Title, _editingCommand, CurrentParent);
                CancelInput(false);
                return;
            }

            var newEntry = new CommandEntry
            {
                Name = _newEntryName,
                Type = _newEntryType,
                Value = val,
                AppPath = (_newEntryType == CommandType.File || _newEntryType == CommandType.Folder) ? AppPath.Trim() : null,
                RequiresArgument = IsRequiresArgumentChecked,
                IsFileSearchEnabled = IsFileSearchChecked,
                Children = _newEntryType == CommandType.Menu ? new List<CommandEntry>() : null
            };

            // Add to CURRENT menu context
            var targetList = (CurrentParent == null) ? _rootCommands : CurrentParent.Children;
            if (targetList == null)
            {
                if (CurrentParent != null) CurrentParent.Children = new List<CommandEntry>();
                targetList = CurrentParent?.Children ?? _rootCommands;
            }

            targetList.Add(newEntry);
            _configService.SaveCommands(_rootCommands);

            // Refresh UI (UpdateDisplay will add the ADD_BUTTON back for UI)
            UpdateDisplay(targetList.ToList(), Title, newEntry, CurrentParent);
            CancelInput(false);
        }

        private void Duplicate(CommandEntry? command)
        {
            if (command == null || (command.Type == CommandType.Command && (command.Value?.StartsWith("ADD_") ?? false))) return;

            // Deep clone
            var json = JsonConvert.SerializeObject(command);
            var clone = JsonConvert.DeserializeObject<CommandEntry>(json);
            if (clone == null) return;

            clone.Name += " - コピー";
            clone.IsTargetedForMove = false;

            var targetList = (CurrentParent == null) ? _rootCommands : CurrentParent.Children;
            if (targetList == null)
            {
                if (CurrentParent != null) CurrentParent.Children = new List<CommandEntry>();
                targetList = CurrentParent?.Children ?? _rootCommands;
            }

            int index = targetList.IndexOf(command);
            if (index >= 0)
            {
                targetList.Insert(index + 1, clone);
            }
            else
            {
                targetList.Add(clone);
            }

            _configService.SaveCommands(_rootCommands);
            UpdateDisplay(targetList, Title, clone, CurrentParent);
        }

        private List<CommandEntry> ReconstructionFromDisplay()
        {
            // Simple approach: if we want to be robust, we need to update the _rootCommands properly.
            // Since we are recursive, let's stick to the current structure.
            return _rootCommands;
        }

        public void AddOrUpdateCommand(CommandEntry command, CommandEntry? originalCommand)
        {
            if (originalCommand != null)
            {
                // Edit case: properties are already updated in the original object
                _configService.SaveCommands(_rootCommands);
                UpdateDisplay(DisplayCommands.ToList(), Title, command, CurrentParent);
            }
            else
            {
                // Add case:
                var targetList = (CurrentParent == null) ? _rootCommands : CurrentParent.Children;
                if (targetList == null)
                {
                    if (CurrentParent != null) CurrentParent.Children = new List<CommandEntry>();
                    targetList = CurrentParent?.Children ?? _rootCommands;
                }

                targetList.Add(command);
                _configService.SaveCommands(_rootCommands);

                // Refresh UI
                UpdateDisplay(targetList.ToList(), Title, command, CurrentParent);
            }
        }

        private bool RemoveFromCurrentList(CommandEntry target)
        {
            // Update _rootCommands hierarchy
            return RemoveCommandFromHierarchy(_rootCommands, target);
        }

        private bool RemoveCommandFromHierarchy(List<CommandEntry> list, CommandEntry target)
        {
            if (list.Remove(target)) return true;
            foreach (var item in list)
            {
                if (item.Children != null && RemoveCommandFromHierarchy(item.Children, target)) return true;
            }
            return false;
        }

        private void CancelInput(bool restoreList = true)
        {
            _searchCts?.Cancel();

            if (IsFileSearchMode)
            {
                IsFileSearchMode = false;
                _targetFolderCommand = null;
                if (restoreList) UpdateDisplay(_preSearchCommands, Title, null, CurrentParent);
            }
            else if (restoreList)
            {
                // Restore commands that were there before "Add Button"
                if (_preInputCommands != null && _preInputCommands.Count > 0)
                {
                    UpdateDisplay(_preInputCommands, Title, null, CurrentParent);
                }
                else
                {
                    LoadCommands();
                }
            }
            CurrentStep = InputStep.None;
            IsInputMode = false;
            OnPropertyChanged(nameof(IsListBoxVisible));
            RequestSync?.Invoke();
        }

        private void GoBack()
        {
            if (_navigationHistory.Count > 0)
            {
                var (parent, previousSelected, title) = _navigationHistory.Pop();
                var commands = parent == null ? _rootCommands : parent.Children;
                UpdateDisplay(commands ?? new List<CommandEntry>(), title, previousSelected, parent);
            }
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

        private void RestoreState(NavigationState state)
        {
            // 状態の復元
            IsInputMode = state.IsInputMode;
            CurrentStep = state.CurrentStep;
            InputText = state.InputText;
            InputPrompt = state.InputPrompt;
            _editingCommand = state.EditingCommand;
            _newEntryName = state.NewEntryName;
            _newEntryType = state.NewEntryType;

            // 階層スタックの復元
            _navigationHistory.Clear();
            for (int i = state.NavigationStack.Count - 1; i >= 0; i--)
            {
                _navigationHistory.Push(state.NavigationStack[i]);
            }
            CanGoBack = _navigationHistory.Count > 0;

            // 表示リストの再構築
            if (IsInputMode && CurrentStep == InputStep.EnteringName)
            {
                SetupTypeSelectionDisplay();
                Title = state.Title;
                // 種別に基づいて選択アイテムを復元（オブジェクト参照ではなく種類で一致させる）
                if (state.SelectedType.HasValue)
                {
                    SelectedItem = DisplayCommands.FirstOrDefault(c => c.Type == state.SelectedType.Value);
                }
                else
                {
                    SelectedItem = DisplayCommands.FirstOrDefault();
                }
            }
            else if (IsInputMode && CurrentStep == InputStep.EnteringValue)
            {
                // リストは隠れているが、内部的には Step 1 のリストを保持（または空にする）
                DisplayCommands.Clear();
                Title = state.Title;
            }
            else if (state.IsRoot)
            {
                DisplayCommands.Clear();
                foreach (var cmd in _rootCommands) DisplayCommands.Add(cmd);
                Title = "Quick Actions";
                SelectedItem = state.SelectedItem;
            }
            else if (state.ParentMenu != null && state.ParentMenu.Children != null)
            {
                DisplayCommands.Clear();
                foreach (var cmd in state.ParentMenu.Children) DisplayCommands.Add(cmd);
                Title = state.Title;
                SelectedItem = state.SelectedItem;
            }
            else
            {
                // フォールバック（検索モードや定義外の状態）
                Title = state.Title;
                SelectedItem = state.SelectedItem;
            }

            OnPropertyChanged(nameof(IsListBoxVisible));
            RequestSync?.Invoke();
        }

        private void MoveLeft()
        {
            if (IsInputMode) { CancelInput(); return; }
            GoBack();
        }

        private void MoveRight()
        {
            if (IsOnHierarchyButton)
            {
                EnterHierarchyWithSelectedItem();
                return;
            }

            if (SelectedItem != null && SelectedItem.Type == CommandType.Menu)
            {
                Execute(SelectedItem);
            }
        }

        public void EnterHierarchyWithSelectedItem()
        {
            if (SelectedItem == null || TargetHierarchyItem == null) return;
            if (TargetHierarchyItem.Children == null) TargetHierarchyItem.Children = new List<CommandEntry>();

            var itemToEnter = SelectedItem;
            var targetMenu = TargetHierarchyItem;

            // Remove from current list
            if (!RemoveFromCurrentList(itemToEnter)) return;

            // Add to target menu
            // Insert before "Add Button" if it exists
            int addIndex = targetMenu.Children.FindIndex(c => c.Type == CommandType.Command && c.Value.StartsWith("ADD_"));
            if (addIndex >= 0)
            {
                targetMenu.Children.Insert(addIndex, itemToEnter);
            }
            else
            {
                targetMenu.Children.Add(itemToEnter);
            }

            // Save and Navigate
            _configService.SaveCommands(_rootCommands);
            targetMenu.IsTargetedForMove = false;
            IsOnHierarchyButton = false;
            
            // Set SelectedItem to the TARGET hierarchy button before entering it.
            // This ensures that when we go back (via Left key), the focus is restored to the hierarchy button.
            SelectedItem = targetMenu;
            Execute(targetMenu);
            
            // Focus the item inside the hierarchy
            SelectedItem = itemToEnter;
            RequestSync?.Invoke();
        }

        public void ExitHierarchyWithSelectedItem()
        {
            if (SelectedItem == null || CurrentParent == null) return;

            var itemToExit = SelectedItem;
            var currentMenu = CurrentParent; // e.g. Menu1
            
            // Remove from current list (item stays in our memory)
            if (!RemoveFromCurrentList(itemToExit)) return;

            // Go back first to restore the parent list in display
            // The item we are selecting back should be the menu entry we just came FROM
            var previousMenu = currentMenu;
            GoBack();

            if (previousMenu != null)
            {
                int idx = DisplayCommands.IndexOf(previousMenu);
                if (idx >= 0)
                {
                    // Item should be below the menu
                    DisplayCommands.Insert(idx + 1, itemToExit);
                }
                else
                {
                    // Fallback: insert before the "+" button
                    int addIdx = DisplayCommands.ToList().FindIndex(c => c.Type == CommandType.Command && (c.Value?.StartsWith("ADD_") ?? false));
                    if (addIdx >= 0) DisplayCommands.Insert(addIdx, itemToExit);
                    else DisplayCommands.Add(itemToExit);
                }
            }
            else
            {
                // Root: just insert before "+"
                int addIdx = DisplayCommands.ToList().FindIndex(c => c.Type == CommandType.Command && (c.Value?.StartsWith("ADD_") ?? false));
                if (addIdx >= 0) DisplayCommands.Insert(addIdx, itemToExit);
                else DisplayCommands.Add(itemToExit);
            }

            // Persist the change to the persistent structure
            var parentList = CurrentParent == null ? _rootCommands : CurrentParent.Children;
            if (parentList != null)
            {
                parentList.Clear();
                // Filter out the system buttons from the persistent list
                var itemsToPersist = DisplayCommands.Where(c => c.Type != CommandType.Command || (c.Value != null && !c.Value.StartsWith("ADD_"))).ToList();
                parentList.AddRange(itemsToPersist);
            }
            _configService.SaveCommands(_rootCommands);
            
            SelectedItem = itemToExit;
            RequestSync?.Invoke();
        }

        public void ResetToRoot()
        {
            CurrentStep = InputStep.None;
            IsInputMode = false;
            IsFileSearchMode = false;
            CurrentParent = null;
            if (TargetHierarchyItem != null) TargetHierarchyItem.IsTargetedForMove = false;
            IsOnHierarchyButton = false;
            _targetFolderCommand = null;
            if (_navigationHistory.Count > 0)
            {
                _navigationHistory.Clear();
                UpdateDisplay(_rootCommands, "Quick Actions");
            }
            else
            {
                SelectedItem = DisplayCommands.FirstOrDefault();
            }
        }

        private void OpenSettings()
        {
            EditingButtonWidth = ButtonWidth;
            EditingButtonHeight = ButtonHeight;
            EditingMovementSpeed = MovementSpeed;
            EditingFontSize = FontSize;
            EditingGlobalHotkey = GlobalHotkey;
            IsCapturingHotkey = false;
            RequestSettings?.Invoke();
        }

        public event Action? SettingsSaved;

        private void SaveSettings()
        {
            ButtonWidth = EditingButtonWidth;
            ButtonHeight = EditingButtonHeight;
            MovementSpeed = EditingMovementSpeed;
            FontSize = EditingFontSize;
            GlobalHotkey = EditingGlobalHotkey;

            _configService.SaveSettings(_appSettings);
            RequestSync?.Invoke();
            SettingsSaved?.Invoke();
        }

        public void SaveWindowBounds()
        {
            _configService.SaveSettings(_appSettings);
        }

        public void MoveSelection(int direction)
        {
            if (DisplayCommands.Count == 0) return;

            int index = SelectedItem != null ? DisplayCommands.IndexOf(SelectedItem) : -1;

            if (direction > 0) // Down
            {
                if (index == -1) 
                {
                    SelectedItem = DisplayCommands[0];
                }
                else if (index < DisplayCommands.Count - 1) 
                {
                    SelectedItem = DisplayCommands[index + 1];
                }
                else 
                {
                    // ファイル検索モードの場合は、一番下から入力ボックスへ移動
                    if (IsFileSearchMode)
                    {
                        SelectedItem = null;
                        RequestControlFocus?.Invoke("InputTextBox");
                    }
                    else
                    {
                        SelectedItem = DisplayCommands[0]; // Always wrap to top
                    }
                }
            }
            else // Up
            {
                if (index == 0) 
                {
                    // ファイル検索モードの場合は、一番上から入力ボックスへ戻る
                    if (IsFileSearchMode)
                    {
                        SelectedItem = null; // 選択を解除して入力ボックスへ戻る
                        RequestControlFocus?.Invoke("InputTextBox");
                    }
                    else
                    {
                        SelectedItem = DisplayCommands.Last(); // Wrap to bottom
                    }
                }
                else if (index == -1) 
                {
                    SelectedItem = DisplayCommands.Last(); // From none to last
                }
                else 
                {
                    SelectedItem = DisplayCommands[index - 1];
                }
            }
            RequestSync?.Invoke();
        }

        public event Action? RequestHide;
        public event Action? RequestShow;
        public event Action? RequestSync;
        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? name = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }
    }
}
