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
            public string? SelectedTemplateId { get; init; }
            public List<(CommandEntry? Parent, CommandEntry? LastSelected, string Title)> NavigationStack { get; init; } = new();

            public CommandEntry? ParentMenu { get; init; }
            public bool IsRoot { get; init; }

            public bool IsInputMode { get; init; }
            public string InputText { get; init; } = "";
            public string InputPrompt { get; init; } = "";
        }

        private List<CommandEntry> _rootCommands = new();
        public ObservableCollection<CommandEntry> DisplayCommands { get; } = new();

        public ObservableCollection<CommandEntry> WindowSwitcherItems { get; } = new();

        private CommandEntry? _selectedWindowItem;
        public CommandEntry? SelectedWindowItem
        {
            get => _selectedWindowItem;
            set { _selectedWindowItem = value; OnPropertyChanged(); }
        }

        private CommandEntry? _activeWindowSwitcherCommand;
        public CommandEntry? ActiveWindowSwitcherCommand
        {
            get => _activeWindowSwitcherCommand;
            set { _activeWindowSwitcherCommand = value; OnPropertyChanged(); }
        }

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
            set
            {
                _currentParent = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(IsWindowSwitcherMode));
                OnPropertyChanged(nameof(IsNormalListMode));
            }
        }

        /// <summary>ウィンドウ切替モード（サムネイル表示）かどうか</summary>
        public bool IsWindowSwitcherMode => CurrentParent?.Category == CommandCategory.WindowSwitcher;

        /// <summary>通常のリスト表示モードかどうか</summary>
        public bool IsNormalListMode => !IsWindowSwitcherMode;

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
            IsListBoxVisible = (!IsInputMode || IsFileSearchMode || (IsInputMode && DisplayCommands.Count > 0));
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

            }
        }

        private string _inputPrompt = string.Empty;

        public bool ShowFileSearchOption => IsFileSearchMode;
        public bool ShowValueInput => IsFileSearchMode;


        public string InputPrompt
        {
            get => _inputPrompt;
            set { _inputPrompt = value; OnPropertyChanged(); }
        }

        public event Action<string>? RequestControlFocus;



        public event Action<CommandEntry>? RequestShortcutAssignment;
        public event Action<CommandEntry>? RequestDeleteConfirmation;
        public event Action<CommandEntry?, double, double>? RequestButtonCreation;
        public event Action<MainViewModel, bool>? RequestWindowSwitcher;
        public event Action<CommandEntry>? RequestGitWindow;

        public event Func<CommandEntry, string, string?>? RequestArgumentInput;
        public delegate string? RequestSelectInputEventHandler(CommandEntry command, string prompt, List<string> options);
        public event RequestSelectInputEventHandler? RequestSelectInput;
        public event Action? RequestSettings;

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

        public double GitWindowTop
        {
            get => _appSettings.GitWindowTop;
            set { _appSettings.GitWindowTop = value; OnPropertyChanged(); }
        }

        public double GitWindowLeft
        {
            get => _appSettings.GitWindowLeft;
            set { _appSettings.GitWindowLeft = value; OnPropertyChanged(); }
        }

        public double GitWindowWidth
        {
            get => _appSettings.GitWindowWidth;
            set { _appSettings.GitWindowWidth = value; OnPropertyChanged(); }
        }

        public double GitWindowHeight
        {
            get => _appSettings.GitWindowHeight;
            set { _appSettings.GitWindowHeight = value; OnPropertyChanged(); }
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

        public string SettingsShortcut
        {
            get => _appSettings.SettingsShortcut;
            set { _appSettings.SettingsShortcut = value; OnPropertyChanged(); }
        }

        public string CreateButtonShortcut
        {
            get => _appSettings.CreateButtonShortcut;
            set { _appSettings.CreateButtonShortcut = value; OnPropertyChanged(); }
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

        private string _editingSettingsShortcut = string.Empty;
        public string EditingSettingsShortcut
        {
            get => _editingSettingsShortcut;
            set { _editingSettingsShortcut = value; OnPropertyChanged(); }
        }

        private string _editingCreateButtonShortcut = string.Empty;
        public string EditingCreateButtonShortcut
        {
            get => _editingCreateButtonShortcut;
            set { _editingCreateButtonShortcut = value; OnPropertyChanged(); }
        }

        private bool _isCapturingHotkey;
        public bool IsCapturingHotkey
        {
            get => _isCapturingHotkey;
            set { _isCapturingHotkey = value; OnPropertyChanged(); }
        }

        private string _captureTarget = string.Empty;
        public string CaptureTarget
        {
            get => _captureTarget;
            set { _captureTarget = value; OnPropertyChanged(); }
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

        private ObservableCollection<ConstantEntry> _editingConstants = new();
        public ObservableCollection<ConstantEntry> EditingConstants
        {
            get => _editingConstants;
            set { _editingConstants = value; OnPropertyChanged(); }
        }

        private ObservableCollection<PairEntryFolder> _editingConstantFolders = new();
        public ObservableCollection<PairEntryFolder> EditingConstantFolders
        {
            get => _editingConstantFolders;
            set { _editingConstantFolders = value; OnPropertyChanged(); }
        }

        private ObservableCollection<SelectTemplate> _editingSelectTemplates = new();
        public ObservableCollection<SelectTemplate> EditingSelectTemplates
        {
            get => _editingSelectTemplates;
            set { _editingSelectTemplates = value; OnPropertyChanged(); }
        }

        private ObservableCollection<PairEntryFolder> _editingSelectTemplateFolders = new();
        public ObservableCollection<PairEntryFolder> EditingSelectTemplateFolders
        {
            get => _editingSelectTemplateFolders;
            set { _editingSelectTemplateFolders = value; OnPropertyChanged(); }
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
            list.RemoveAll(c => c.IsSystemButton);

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
            }

            // Ensure "Add Button" for hierarchy levels (if not in special input/search modes)
            // ウィンドウ切替モード時（動的階層）はボタン追加を表示しない
            bool isWindowSwitcher = parent?.Category == CommandCategory.WindowSwitcher;
            if (!IsInputMode && !IsFileSearchMode && !isWindowSwitcher)
            {
                if (!DisplayCommands.Any(c => (c.Value?.StartsWith("ADD_") ?? false)))
                {
                    DisplayCommands.Add(new CommandEntry { Name = "ボタン追加", IsSystem = true, Value = "ADD_BUTTON" });
                }
            }

            Title = title;
            CurrentParent = parent;
            CanGoBack = _navigationHistory.Count > 0;
            SelectedItem = itemToSelect ?? DisplayCommands.FirstOrDefault();

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
                SelectedTemplateId = (IsInputMode && SelectedItem != null) ? (SelectedItem.Tag as CommandPreset)?.Id : null,
                NavigationStack = _navigationHistory.ToList(),
                ParentMenu = parent,
                IsRoot = isRoot,
                IsInputMode = IsInputMode,
                InputText = InputText,
                InputPrompt = InputPrompt
            };

            // 直前の履歴と同じ状態なら記録しない
            if (_history.Count > 0)
            {
                var last = _history.Last();
                if (last.Title == state.Title &&
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

            try
            {

                // 0. 動的に生成されたウィンドウアイテムの処理
                if (command.WindowHandle != IntPtr.Zero)
                {
                    _windowService.FocusWindow(command.WindowHandle);
                    RequestHide?.Invoke();
                    return;
                }

                // 1. Check for system buttons (ADD_BUTTON, search results, etc.)
                if (command.IsSystemButton)
                {

                    if (command.Value == "ADD_BUTTON")
                    {
                        RequestButtonCreation?.Invoke(null, WindowWidth, WindowHeight);
                        return;
                    }
                    return;
                }

                // 1.5 特定のウィンドウ切替（動的階層生成）
                if (command.Category == CommandCategory.WindowSwitcher && command.WindowHandle == IntPtr.Zero)
                {
                    // すでにこのウィンドウ切替の一覧が表示されている場合は、選択を次に進める（連打対応）
                    if (ActiveWindowSwitcherCommand == command && WindowSwitcherItems.Any())
                    {
                        bool isShiftPressed = (Keyboard.Modifiers & ModifierKeys.Shift) != 0;
                        int idx = (SelectedWindowItem != null) ? WindowSwitcherItems.IndexOf(SelectedWindowItem) : -1;

                        if (isShiftPressed)
                        {
                            // 逆送り
                            int nextIdx = (idx <= 0) ? WindowSwitcherItems.Count - 1 : idx - 1;
                            SelectedWindowItem = WindowSwitcherItems[nextIdx];
                        }
                        else
                        {
                            // 順送り
                            int nextIdx = (idx >= WindowSwitcherItems.Count - 1) ? 0 : idx + 1;
                            SelectedWindowItem = WindowSwitcherItems[nextIdx];
                        }
                        return;
                    }

                    var windows = _windowService.GetVisibleWindowsByTitle(command.Value);
                    if (windows.Any())
                    {
                        var windowItems = windows.Select(w =>
                        {
                            string displayName = w.Title;
                            if (!string.IsNullOrWhiteSpace(command.Value))
                            {
                                var filterParts = command.Value.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                                foreach (var part in filterParts)
                                {
                                    int idx = displayName.IndexOf(part, StringComparison.OrdinalIgnoreCase);
                                    if (idx >= 0)
                                    {
                                        displayName = displayName.Remove(idx, part.Length);
                                    }
                                }
                                // 前後の不要な記号や空白を掃除
                                displayName = displayName.Trim(' ', '-', '—', '－', '|', '｜', '・', ':', '：');
                            }

                            if (string.IsNullOrWhiteSpace(displayName)) displayName = w.Title;

                            return new CommandEntry
                            {
                                Name = displayName,
                                Value = w.Title,
                                Category = CommandCategory.WindowSwitcher,
                                WindowHandle = w.Handle,
                                IconSource = w.Icon,
                                WindowWidth = w.Width,
                                WindowHeight = w.Height
                            };
                        }).ToList();

                        // 切替専用プロパティを更新（通常プロパティは触らない）
                        WindowSwitcherItems.Clear();
                        foreach (var item in windowItems) WindowSwitcherItems.Add(item);
                        ActiveWindowSwitcherCommand = command;

                        // 現在のフォーカスウィンドウを取得（パレット自身がフォーカスされている場合はその後ろのウィンドウ）
                        IntPtr foregroundHWnd = NativeMethods.GetForegroundWindow();
                        uint foregroundPid;
                        NativeMethods.GetWindowThreadProcessId(foregroundHWnd, out foregroundPid);
                        uint myPid = (uint)System.Diagnostics.Process.GetCurrentProcess().Id;

                        if (foregroundPid == myPid)
                        {
                            IntPtr prevWindow = foregroundHWnd;
                            while ((prevWindow = NativeMethods.GetWindow(prevWindow, NativeMethods.GW_HWNDNEXT)) != IntPtr.Zero)
                            {
                                if (!NativeMethods.IsWindowVisible(prevWindow)) continue;
                                uint pid;
                                NativeMethods.GetWindowThreadProcessId(prevWindow, out pid);
                                if (pid == myPid) continue;

                                foregroundHWnd = NativeMethods.GetAncestor(prevWindow, NativeMethods.GA_ROOT);
                                break;
                            }
                        }

                        // 最初に対象を選択するか判定
                        bool isShiftPressed = (Keyboard.Modifiers & ModifierKeys.Shift) != 0;
                        int initialIndex = 0;

                        // 1つ目が現在のフォーカスウィンドウなら、順送りなら2つ目、逆送りなら最後を選択
                        if (windowItems.Any() && windowItems[0].WindowHandle == foregroundHWnd)
                        {
                            if (isShiftPressed)
                            {
                                initialIndex = WindowSwitcherItems.Count - 1;
                            }
                            else if (WindowSwitcherItems.Count >= 2)
                            {
                                initialIndex = 1;
                            }
                        }
                        else
                        {
                            // 現在のウィンドウがリストにない、または1つ目でない場合
                            if (isShiftPressed)
                            {
                                initialIndex = WindowSwitcherItems.Count - 1;
                            }
                            else
                            {
                                initialIndex = 0;
                            }
                        }

                        if (WindowSwitcherItems.Count > initialIndex)
                        {
                            SelectedWindowItem = WindowSwitcherItems[initialIndex];
                        }
                        else
                        {
                            SelectedWindowItem = WindowSwitcherItems.FirstOrDefault();
                        }

                        // 専用ウィンドウの表示をリクエスト
                        RequestWindowSwitcher?.Invoke(this, true); // true = Peek mode
                        RequestSync?.Invoke();
                        return;
                    }
                    else
                    {
                        return;
                    }
                }

                // 1.8 Check for Git operations
                if (command.Category == CommandCategory.Git)
                {
                    RequestGitWindow?.Invoke(command);
                    return;
                }

                // 2. Check for Menu and enter it
                if (command.Category == CommandCategory.Hierarchy)
                {
                    if (command.Children == null) command.Children = new List<CommandEntry>();
                    if (IsFileSearchMode) CancelInput(); // Exit search if navigating

                    // 親階層の特定（現在の親の直下にあるメニューか判定）
                    bool isChildOfCurrent = (CurrentParent == null)
                        ? _rootCommands.Contains(command)
                        : (CurrentParent.Children?.Contains(command) ?? false);

                    if (isChildOfCurrent)
                    {
                        // 同一階層内または直下への移動なら通常どおりプッシュ
                        _navigationHistory.Push((CurrentParent, SelectedItem, Title));
                    }
                    else
                    {
                        // ショートカットなどによるジャンプ時：
                        // 実際の階層構造に合わせてナビゲーションスタックを再構築する
                        ReconstructNavigationHistory(command);
                    }

                    UpdateDisplay(command.Children, command.Name, null, command);
                    RequestShow?.Invoke();
                    return;
                }

                // 3. Handle File Search
                if (command.Behavior.IsFileSearchEnabled && !IsFileSearchMode)
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

                string? argument = null;
                // 4. Handle RequiresArgument (Modal Dialog)
                if (command.Behavior.RequiresArgument)
                {
                    argument = RequestArgumentInput?.Invoke(command, $"[{command.Name}] の引数を入力してください:（右キーで引数追加）");
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
                }

                // 4.5 Handle {select:...} placeholders
                bool hasSelect = false;
                string tempValue = command.Value ?? string.Empty;
                string tempAppPath = command.AppPath ?? string.Empty;

                hasSelect |= ProcessSelectPlaceholders(command, ref tempValue);
                hasSelect |= ProcessSelectPlaceholders(command, ref tempAppPath);

                if (hasSelect)
                {
                    var resolvedCommand = new CommandEntry
                    {
                        Name = command.Name,
                        Value = tempValue,
                        AppPath = tempAppPath,
                        Category = command.Category,
                        Behavior = command.Behavior,
                        WindowHandle = command.WindowHandle,
                        Hotkey = command.Hotkey,
                        HotkeyType = command.HotkeyType
                    };
                    RunActionAndHandleVisibility(resolvedCommand, argument);
                    return;
                }

                // 5. Run the actual action
                RunActionAndHandleVisibility(command, argument);
            }
            catch (OperationCanceledException)
            {
                // Canceled by user via dialog
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error in Execute: {ex}");
            }
        }

        private bool ProcessSelectPlaceholders(CommandEntry command, ref string text)
        {
            if (string.IsNullOrEmpty(text)) return false;
            
            bool wasReplaced = false;
            var regex = new System.Text.RegularExpressions.Regex(@"\{select:(.*?)\}");
            var matches = regex.Matches(text).Cast<System.Text.RegularExpressions.Match>().ToList();

            foreach (var match in matches)
            {
                string inner = match.Groups[1].Value;
                List<string> options;

                var template = _appSettings?.SelectTemplates?.FirstOrDefault(t => t.Name == inner);
                if (template != null)
                {
                    options = template.Options;
                }
                else
                {
                    options = inner.Split(',').Select(s => s.Trim()).Where(s => !string.IsNullOrEmpty(s)).ToList();
                }

                if (options.Count == 0) continue;

                string? selected = RequestSelectInput?.Invoke(command, $"[{command.Name}] の引数を選択してください:", options);
                if (selected == null)
                {
                    throw new OperationCanceledException("Selection canceled by user.");
                }

                text = text.Replace(match.Value, selected);
                wasReplaced = true;
            }

            return wasReplaced;
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
                    foreach (var d in drives) DisplayCommands.Add(new CommandEntry { Name = d.Name, Value = "TYPE_TEMPLATE", IsSystem = true });
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
                                    found.Add(new CommandEntry { Name = entry, Value = "TYPE_TEMPLATE", IsSystem = true });
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
                        Category = CommandCategory.Open
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
            if (command == null || command.IsSystemButton) return;
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

        private void ReconstructNavigationHistory(CommandEntry target)
        {
            // 階層パスを取得
            var path = new List<CommandEntry>();
            if (FindPathToCommand(_rootCommands, target, path))
            {
                // ターゲット本体もパスに含める（スタックには親・自分・タイトルのセットが必要なため）
                path.Add(target);

                _navigationHistory.Clear();

                CommandEntry? currentParent = null;
                string currentTitle = "Quick Actions";

                // パス上の祖先（および自身）を順にスタックに積む
                // 最後の要素（ターゲット自身）に対するプッシュが、ターゲットから「戻る」際の状態になる
                for (int i = 0; i < path.Count; i++)
                {
                    var itemInPath = path[i];
                    _navigationHistory.Push((currentParent, itemInPath, currentTitle));

                    currentParent = itemInPath;
                    currentTitle = itemInPath.Name;
                }
            }
        }

        private bool FindPathToCommand(List<CommandEntry> list, CommandEntry target, List<CommandEntry> path)
        {
            foreach (var item in list)
            {
                if (item == target) return true;
                if (item.Children != null)
                {
                    path.Add(item);
                    if (FindPathToCommand(item.Children, target, path)) return true;
                    path.RemoveAt(path.Count - 1);
                }
            }
            return false;
        }

        private readonly Dictionary<int, CommandEntry> _globalHotkeyMapping = new();
        public event Action<int, string>? RequestRegisterGlobalHotkey;
        public event Action? RequestUnregisterCommandHotkeys;

        public CommandEntry? ExecuteHotkeyById(int id)
        {
            if (_globalHotkeyMapping.TryGetValue(id, out var command))
            {
                Execute(command);
                return command;
            }
            return null;
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

                    // ウィンドウ切替の場合は、Shift付きも自動登録して逆送りをサポートする
                    if (command.Category == CommandCategory.WindowSwitcher &&
                        !command.Hotkey.Split('+').Any(p => p.Trim().Equals("Shift", StringComparison.OrdinalIgnoreCase)))
                    {
                        int shiftId = nextId++;
                        _globalHotkeyMapping[shiftId] = command;
                        string shiftHotkey = "Shift + " + command.Hotkey;
                        RequestRegisterGlobalHotkey?.Invoke(shiftId, shiftHotkey);
                    }
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
            if (command.IsSystemButton && command.Value.StartsWith("ADD_")) return;

            // Note: Users should be able to delete their own buttons even if named "再ビルド" or "JSON同期"
            // unless they are for the system reload logic.
            if (command.Value == "reload.bat" && command.Category == CommandCategory.Open && (command.Name == "再ビルド" || command.Name == "Reload App")) return;

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

            // Hierarchy stop logic
            bool stateJustCleared = false;
            if (IsOnHierarchyButton)
            {
                if (direction == LastMoveDirection)
                {
                    // Second press in same direction: move past it
                    if (TargetHierarchyItem != null) TargetHierarchyItem.IsTargetedForMove = false;
                    IsOnHierarchyButton = false;
                    stateJustCleared = true;
                }
                else
                {
                    // Moved away from the stop point
                    if (TargetHierarchyItem != null) TargetHierarchyItem.IsTargetedForMove = false;
                    IsOnHierarchyButton = false;
                    UpdateDisplay(currentList, Title, command, CurrentParent);
                    return;
                }
            }

            int newIndex = index + direction;
            if (newIndex < 0 || newIndex >= currentList.Count) return;

            // System commands at the end (Add... buttons) should not be moved past
            if (currentList[newIndex].IsSystemButton || currentList[index].IsSystemButton)
            {
                // But allow reordering among regular items if they are not system
                if (currentList[newIndex].IsSystemButton && direction > 0) return;
                if (currentList[index].IsSystemButton) return;
            }

            if (!stateJustCleared && !IsOnHierarchyButton)
            {
                // Check if target is a menu
                if (currentList[newIndex].Category == CommandCategory.Hierarchy)
                {
                    IsOnHierarchyButton = true;
                    TargetHierarchyItem = currentList[newIndex];
                    TargetHierarchyItem.IsTargetedForMove = true;
                    LastMoveDirection = direction;
                    UpdateDisplay(currentList, Title, command, CurrentParent);
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
                targetList.AddRange(currentList.Where(c => !c.IsSystemButton));
            }
            _configService.SaveCommands(_rootCommands);

            // Refresh UI
            UpdateDisplay(currentList, Title, command, CurrentParent);
        }



        private void FinishInputFlow_Deprecrated() { }

        private void Duplicate(CommandEntry? command)
        {
            if (command == null || command.IsSystemButton) return;

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


        private void CommitInput()
        {
            _searchCts?.Cancel();

            if (IsFileSearchMode)
            {
                if (SelectedItem != null)
                {
                    Execute(SelectedItem);
                }
                else if (!string.IsNullOrWhiteSpace(InputText) && _targetFolderCommand != null)
                {
                    RunActionAndHandleVisibility(_targetFolderCommand, InputText);
                }
            }
        }

        public void AddOrUpdateCommand(CommandEntry command, CommandEntry? originalCommand)
        {
            if (originalCommand != null)
            {
                // Edit case: 既存のコマンドを新しいインスタンスで置き換える
                if (ReplaceCommandInHierarchy(_rootCommands, originalCommand, command))
                {
                    _configService.SaveCommands(_rootCommands);

                    // 現在表示中のリストも更新
                    var currentList = DisplayCommands.ToList();
                    int index = currentList.IndexOf(originalCommand);
                    if (index >= 0)
                    {
                        currentList[index] = command;
                    }
                    UpdateDisplay(currentList, Title, command, CurrentParent);
                }
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

        private bool ReplaceCommandInHierarchy(List<CommandEntry> list, CommandEntry target, CommandEntry newCommand)
        {
            int index = list.IndexOf(target);
            if (index >= 0)
            {
                list[index] = newCommand;
                return true;
            }
            foreach (var item in list)
            {
                if (item.Children != null && ReplaceCommandInHierarchy(item.Children, target, newCommand)) return true;
            }
            return false;
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
                UpdateDisplay(_preSearchCommands, Title, null, CurrentParent);
            }

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
            InputText = state.InputText;
            InputPrompt = state.InputPrompt;

            // 階層スタックの復元
            _navigationHistory.Clear();
            for (int i = state.NavigationStack.Count - 1; i >= 0; i--)
            {
                _navigationHistory.Push(state.NavigationStack[i]);
            }
            CanGoBack = _navigationHistory.Count > 0;

            // 表示リストの再構築
            if (state.IsRoot)
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

            if (SelectedItem != null && SelectedItem.Category == CommandCategory.Hierarchy)
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
            int addIndex = targetMenu.Children.FindIndex(c => c.IsSystemButton && c.Value.StartsWith("ADD_"));
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
                    int addIdx = DisplayCommands.ToList().FindIndex(c => c.IsSystemButton && (c.Value?.StartsWith("ADD_") ?? false));
                    if (addIdx >= 0) DisplayCommands.Insert(addIdx, itemToExit);
                    else DisplayCommands.Add(itemToExit);
                }
            }
            else
            {
                // Root: just insert before "+"
                int addIdx = DisplayCommands.ToList().FindIndex(c => c.IsSystemButton && (c.Value?.StartsWith("ADD_") ?? false));
                if (addIdx >= 0) DisplayCommands.Insert(addIdx, itemToExit);
                else DisplayCommands.Add(itemToExit);
            }

            // Persist the change to the persistent structure
            var parentList = CurrentParent == null ? _rootCommands : CurrentParent.Children;
            if (parentList != null)
            {
                parentList.Clear();
                // Filter out the system buttons from the persistent list
                var itemsToPersist = DisplayCommands.Where(c => !c.IsSystemButton || (c.Value != null && !c.Value.StartsWith("ADD_"))).ToList();
                parentList.AddRange(itemsToPersist);
            }
            _configService.SaveCommands(_rootCommands);

            SelectedItem = itemToExit;
            RequestSync?.Invoke();
        }

        public void ResetToRoot()
        {

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
            EditingSettingsShortcut = SettingsShortcut;
            EditingCreateButtonShortcut = CreateButtonShortcut;
            EditingConstants = new ObservableCollection<ConstantEntry>(
                (_appSettings.Constants ?? new System.Collections.Generic.List<ConstantEntry>()).Select(c => new ConstantEntry { Name = c.Name, Value = c.Value, ParentFolderId = c.ParentFolderId, SortOrder = c.SortOrder })
            );
            EditingConstantFolders = new ObservableCollection<PairEntryFolder>(
                (_appSettings.ConstantFolders ?? new System.Collections.Generic.List<PairEntryFolder>()).Select(f => new PairEntryFolder { Id = f.Id, Name = f.Name, ParentFolderId = f.ParentFolderId, SortOrder = f.SortOrder })
            );
            EditingSelectTemplates = new ObservableCollection<SelectTemplate>(
                (_appSettings.SelectTemplates ?? new System.Collections.Generic.List<SelectTemplate>()).Select(t => new SelectTemplate { Name = t.Name, Options = new System.Collections.Generic.List<string>(t.Options), ParentFolderId = t.ParentFolderId, SortOrder = t.SortOrder })
            );
            EditingSelectTemplateFolders = new ObservableCollection<PairEntryFolder>(
                (_appSettings.SelectTemplateFolders ?? new System.Collections.Generic.List<PairEntryFolder>()).Select(f => new PairEntryFolder { Id = f.Id, Name = f.Name, ParentFolderId = f.ParentFolderId, SortOrder = f.SortOrder })
            );
            IsCapturingHotkey = false;
            CaptureTarget = string.Empty;
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
            SettingsShortcut = EditingSettingsShortcut;
            CreateButtonShortcut = EditingCreateButtonShortcut;
            _appSettings.Constants = EditingConstants.Select(c => new ConstantEntry { Name = c.Name, Value = c.Value, ParentFolderId = c.ParentFolderId, SortOrder = c.SortOrder }).ToList();
            _appSettings.ConstantFolders = EditingConstantFolders.Select(f => new PairEntryFolder { Id = f.Id, Name = f.Name, ParentFolderId = f.ParentFolderId, SortOrder = f.SortOrder }).ToList();
            _appSettings.SelectTemplates = EditingSelectTemplates.Select(t => new SelectTemplate { Name = t.Name, Options = new System.Collections.Generic.List<string>(t.Options), ParentFolderId = t.ParentFolderId, SortOrder = t.SortOrder }).ToList();
            _appSettings.SelectTemplateFolders = EditingSelectTemplateFolders.Select(f => new PairEntryFolder { Id = f.Id, Name = f.Name, ParentFolderId = f.ParentFolderId, SortOrder = f.SortOrder }).ToList();

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
