using System;
using System.Windows;
using System.Windows.Input;
using System.Windows.Controls;
using System.Windows.Media;
using System.Collections.Generic;
using HotKeyCommandApp.ViewModels;
using HotKeyCommandApp.Services;
using HotKeyCommandApp.Models;
using System.Windows.Interop;

namespace HotKeyCommandApp.Views
{
    public partial class PaletteWindow : Window
    {
        private readonly HotKeyService _hotKeyService;
        private readonly GlobalKeyHookService _globalKeyHookService;
        private readonly HashSet<Key> _pressedKeys = new HashSet<Key>();
        private TimeSpan _lastRenderingTime = TimeSpan.Zero;
        private Point _dragStartPoint;
        private bool _isWaitingForAltRelease = false;
        private WindowSwitcherWindow? _activeSwitcherWindow;

        public PaletteWindow()
        {
            InitializeComponent();
            _hotKeyService = new HotKeyService();
            _hotKeyService.HotKeyPressed += (id) => OnHotKeyPressed(id);

            _globalKeyHookService = new GlobalKeyHookService();
            _globalKeyHookService.EscPressed += OnGlobalEscPressed;

            if (DataContext is MainViewModel vm)
            {
                vm.RequestHide += OnRequestHide;
                vm.RequestShow += OnRequestShow;
                vm.RequestSync += SyncSelection;
                vm.RequestControlFocus += (controlName) =>
                {
                    Dispatcher.BeginInvoke(new Action(() =>
                    {
                        if (controlName == "InputTextBox")
                        {
                            InputTextBox.Focus();
                        }
                        else if (controlName == "CommandListBox")
                        {
                            CommandListBox.Focus();
                        }
                    }));
                };

                vm.RequestArgumentInput += (command, prompt) =>
                {
                    vm.IsDialogActive = true;
                    try
                    {
                        var dialog = new ArgumentInputDialog(command, prompt) { Owner = this };
                        if (dialog.ShowDialog() == true)
                        {
                            return dialog.InputText;
                        }
                        return null;
                    }
                    finally
                    {
                        vm.IsDialogActive = false;
                    }
                };

                vm.RequestSettings += () =>
                {
                    vm.IsDialogActive = true;
                    try
                    {
                        var dialog = new SettingsWindow(vm) { Owner = this };
                        dialog.ShowDialog();
                    }
                    finally
                    {
                        vm.IsDialogActive = false;
                    }
                };

                vm.RequestShortcutAssignment += (command) =>
                {
                    vm.IsDialogActive = true;
                    try
                    {
                        var dialog = new ShortcutAssignmentWindow(command.Name, command.HotkeyType) { Owner = this };
                        if (dialog.ShowDialog() == true)
                        {
                            vm.AssignShortcut(dialog.SelectedHotkey, dialog.SelectedHotkeyType);
                        }
                        else
                        {
                            vm.CancelShortcutAssignmentCommand.Execute(null);
                        }
                    }
                    finally
                    {
                        vm.IsDialogActive = false;
                    }
                };

                vm.RequestRegisterGlobalHotkey += (id, hotkey) => _hotKeyService.UpdateHotKey(id, hotkey);
                vm.RequestUnregisterCommandHotkeys += () => _hotKeyService.UnregisterCommandHotkeys();

                vm.RequestButtonCreation += (command, width, height) =>
                {
                    vm.IsDialogActive = true;
                    try
                    {
                        var dialogViewModel = new ButtonCreationViewModel(vm.AppSettings, command);
                        var dialog = new ButtonCreationWindow(dialogViewModel)
                        {
                            Owner = this,
                            Width = width + 20,
                            Height = height + 30
                        };


                        dialog.ShowDialog();
                        if (dialogViewModel.ResultCommand != null)
                        {
                            vm.AddOrUpdateCommand(dialogViewModel.ResultCommand, command);
                        }
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show($"ダイアログの起動中にエラーが発生しました:\n{ex.Message}\n\n{ex.StackTrace}", "エラー", MessageBoxButton.OK, MessageBoxImage.Error);
                    }
                    finally
                    {
                        vm.IsDialogActive = false;
                    }
                };

                vm.RequestWindowSwitcher += (mainVm, isPeekMode) =>
                {
                    // 現在開いている切替ウィンドウがあるか確認
                    if (_activeSwitcherWindow != null && _activeSwitcherWindow.IsLoaded)
                    {
                        // 同じ種類の切替（同じコマンド）なら、既存ウィンドウのデータ更新を期待して何もしない
                        // (MainViewModel側でコレクションが更新されているため、Bindingにより表示は変わる)
                        if (_activeSwitcherWindow.ActiveCommand == mainVm.ActiveWindowSwitcherCommand)
                        {
                            return;
                        }

                        // 別の種類の切替なら、今のウィンドウを閉じる
                        _activeSwitcherWindow.Close();
                    }

                    this.Hide();
                    var switcher = new WindowSwitcherWindow(mainVm, isPeekMode, mainVm.ActiveWindowSwitcherCommand) { Owner = this };
                    _activeSwitcherWindow = switcher;

                    switcher.Closed += (s, e) =>
                    {
                        if (_activeSwitcherWindow == switcher) _activeSwitcherWindow = null;

                        // 他のウィンドウが既に別のコマンドで上書きしている可能性があるため、
                        // 自身が担当していたコマンドが今もアクティブである場合のみクリアする。
                        if (mainVm.ActiveWindowSwitcherCommand == switcher.ActiveCommand)
                        {
                            mainVm.ActiveWindowSwitcherCommand = null;
                            mainVm.WindowSwitcherItems.Clear();
                            mainVm.SelectedWindowItem = null;
                        }
                    };
                    // 1. まず画面外 (-20000) で実体化させる。これにより正確な ActualWidth が確定する。
                    switcher.Show();
                    
                    // 2. 確定したサイズを用いて、最右モニターの中央へ瞬時に移動（ワープ）させる。
                    WindowHelper.CenterOnRightmostMonitor(switcher);
                };

                vm.RequestDeleteConfirmation += (command) =>
                {
                    vm.IsDialogActive = true;
                    try
                    {
                        var dialog = new DeleteConfirmationWindow(command.Name) { Owner = this };
                        if (dialog.ShowDialog() == true)
                        {
                            vm.ConfirmDeleteCommand.Execute(command);
                        }
                        else
                        {
                            vm.CancelDeleteCommand.Execute(null);
                        }
                    }
                    finally
                    {
                        vm.IsDialogActive = false;
                    }
                };

                this.Loaded += (s, e) =>
                {
                    if (!double.IsNaN(vm.WindowWidth)) this.Width = vm.WindowWidth;
                    if (!double.IsNaN(vm.WindowHeight)) this.Height = vm.WindowHeight;

                    if (!double.IsNaN(vm.WindowTop) && !double.IsNaN(vm.WindowLeft))
                    {
                        this.WindowStartupLocation = WindowStartupLocation.Manual;
                        this.Top = vm.WindowTop;
                        this.Left = vm.WindowLeft;
                    }
                };

                vm.PropertyChanged += (s, ev) =>
                {
                    if (ev.PropertyName == nameof(MainViewModel.IsCapturingHotkey))
                    {
                        _hotKeyService.IsEnabled = !vm.IsCapturingHotkey;
                    }
                };

                this.Closing += (s, e) => SaveBoundsToViewModel();
                Application.Current.Exit += (s, e) => SaveBoundsToViewModel();
            }

            CompositionTarget.Rendering += OnRendering;
            this.Deactivated += (s, e) => _pressedKeys.Clear();
            this.IsVisibleChanged += (s, e) =>
            {
                if (!(bool)e.NewValue) _pressedKeys.Clear();
                else FocusListBox();
            };
            this.Activated += (s, e) => FocusListBox();
        }

        private void FocusListBox()
        {
            if (!(DataContext is MainViewModel vm)) return;

            if (vm.IsInputMode)
            {
                if (vm.CurrentStep == InputStep.EnteringAppPath)
                {
                    // EnteringAppPathの場合はListBoxをすぐにフォーカスさせる
                    CommandListBox.Focus();
                }
                else
                {
                    InputTextBox.Focus();
                }
                return;
            }

            CommandListBox.Focus();
            if (vm.SelectedItem == null && vm.DisplayCommands.Count > 0)
            {
                vm.SelectedItem = vm.DisplayCommands[0];
            }
        }

        private void OnGlobalEscPressed()
        {
            if (this.IsVisible && !this.IsActive)
            {
                if (DataContext is MainViewModel vm && vm.IsDialogActive) return;
                OnRequestHide();
            }
        }

        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);

            // Altキーによるシステムメニュー（移動、サイズ変更など）を抑制
            WindowHelper.DisableSystemMenu(this);
            WindowHelper.EnableWindowMoveShortcut(this, () => DataContext is MainViewModel vm && !vm.IsInputMode && !vm.IsDialogActive);
            WindowHelper.EnableWindowDragMove(this);

            string hotkey = "Win+Alt+Z";
            if (DataContext is MainViewModel vm)
            {
                hotkey = vm.GlobalHotkey;
                vm.SettingsSaved += () =>
                {
                    _hotKeyService.UpdateHotKey(vm.GlobalHotkey);
                    vm.IsCapturingHotkey = false;
                };
            }

            _hotKeyService.Register(this, hotkey);

            if (DataContext is MainViewModel mainVm)
            {
                mainVm.UpdateGlobalHotkeys();
            }
        }

        private void OnHotKeyPressed(int id)
        {
            if (id == 9000) // デフォルトのホットキー (Win+Alt+Z)
            {
                if (this.IsVisible)
                {
                    if (this.IsActive)
                    {
                        // すでにフォーカスがある場合は閉じる（トグル動作）
                        if (DataContext is MainViewModel vm)
                        {
                            vm.ResetToRoot();
                        }
                        SaveBoundsToViewModel();
                        this.Hide();
                    }
                    else
                    {
                        // 表示されているがフォーカスがない場合は再度フォーカスを当てる
                        // もし所有ダイアログがあれば、そちらをアクティブにする
                        bool dialogActivated = false;
                        foreach (Window owned in this.OwnedWindows)
                        {
                            if (owned.IsVisible)
                            {
                                owned.Activate();
                                owned.Focus();
                                dialogActivated = true;
                                break;
                            }
                        }

                        if (!dialogActivated)
                        {
                            this.Activate();
                            this.Focus();
                            Dispatcher.BeginInvoke(new Action(() =>
                            {
                                FocusListBox();
                            }), System.Windows.Threading.DispatcherPriority.Render);
                        }
                    }
                }
                else
                {
                    OnRequestShow();
                }
            }
            else
            {
                // 個別コマンドのグローバルショートカット実行
                if (DataContext is MainViewModel vm)
                {
                    // ウィンドウが非表示、かつ切替ウィンドウも開いていない場合は、ジャンプ前に状態をリセットする
                    if (!this.IsVisible && _activeSwitcherWindow == null)
                    {
                        vm.LoadCommands();
                    }
                    var command = vm.ExecuteHotkeyById(id);
                    if (command != null && command.Type == CommandType.WindowSwitcher)
                    {
                        // 専用ウィンドウ化されたため、パレットを表示する必要はない
                        // (vm.ExecuteHotkeyById 内で専用ウィンドウの起動イベントが発火する)
                        return;
                    }

                    // 通常コマンド（実行後にパレットを表示し続ける必要がある場合など）
                    if (command != null)
                    {
                        OnRequestShow();
                        if (command.Hotkey?.Contains("Alt") ?? false)
                        {
                            _isWaitingForAltRelease = true;
                        }
                    }
                }
            }
        }

        private void OnRequestShow()
        {
            if (!this.IsVisible)
            {
                this.Show();
            }

            this.Activate();
            this.Focus();

            // 描画準備が整ってから表示・フォーカス
            Dispatcher.BeginInvoke(new Action(() =>
            {
                CommandListBox.Visibility = Visibility.Visible;
                
                // 切り替え時などにフォーカスを奪われた際、強制的にアクティブにする
                this.Activate();
                this.Focus();
                
                FocusListBox();
                SyncSelection();
            }), System.Windows.Threading.DispatcherPriority.Render);
        }

        private void OnRequestHide()
        {
            if (DataContext is MainViewModel vm)
            {
                // 検索等をサポートしない状態の場合、残っている検索入力をクリアする
                if (!vm.IsInputMode && !vm.IsFileSearchMode && !vm.IsDialogActive)
                {
                    vm.InputText = string.Empty;
                }
                if (vm.IsInputMode)
                {
                    vm.CancelInputCommand.Execute(null);
                    return;
                }
                // IsSettingsModeのロジックは削除済み（別ウィンドウで処理）
                vm.ResetToRoot();
            }

            // 非表示にする前に確実にチラつきを抑える
            this.Visibility = Visibility.Collapsed;
            SaveBoundsToViewModel();
            _pressedKeys.Clear();
            this.Hide();
        }

        private void SyncSelection()
        {
            if (!(DataContext is MainViewModel vm)) return;

            if (vm.IsInputMode)
            {
                InputTextBox.Focus();
            }

            if (CommandListBox.SelectedItem != null)
            {
                CommandListBox.ScrollIntoView(CommandListBox.SelectedItem);

                // 入力モードではない場合にのみリストアイテムにフォーカスを当てる
                if (!vm.IsInputMode)
                {
                    // コンテナが生成されるようレイアウトを即座に更新する
                    CommandListBox.UpdateLayout();

                    var container = (ListBoxItem)CommandListBox.ItemContainerGenerator.ContainerFromItem(CommandListBox.SelectedItem);
                    if (container != null)
                    {
                        container.Focus();
                        Keyboard.Focus(container);
                        FocusManager.SetFocusedElement(this, container);
                    }
                    else
                    {
                        // 最も優先順位の低い「アイドル時」にフォーカスを試みる
                        Dispatcher.BeginInvoke(new Action(() =>
                        {
                            var c = (ListBoxItem)CommandListBox.ItemContainerGenerator.ContainerFromItem(CommandListBox.SelectedItem);
                            if (c != null)
                            {
                                c.Focus();
                                Keyboard.Focus(c);
                                FocusManager.SetFocusedElement(this, c);
                            }
                        }), System.Windows.Threading.DispatcherPriority.ApplicationIdle);
                    }
                }
            }
            else if (!vm.IsInputMode && vm.DisplayCommands.Count > 0)
            {
                // フォールバック: 選択がない場合は先頭を選択して再同期
                vm.SelectedItem = vm.DisplayCommands[0];
                SyncSelection();
            }
        }



        private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            Key key = (e.Key == Key.ImeProcessed) ? e.ImeProcessedKey : e.Key;
            if (key == Key.System) key = e.SystemKey;
            _pressedKeys.Add(key);

            if (DataContext is MainViewModel vm)
            {
                // ===== ホットキーキャプチャモード =====
                if (vm.IsCapturingHotkey)
                {
                    if (key == Key.Escape)
                    {
                        vm.IsCapturingHotkey = false;
                        vm.EditingGlobalHotkey = vm.GlobalHotkey; // 元に戻す
                        e.Handled = true;
                        return;
                    }

                    // 主キーに対するモディファを組み立てる
                    Key mainKey = (e.Key == Key.System) ? e.SystemKey : key;

                    // 修飾キーのみの場合は無視（Alt単体押しなどを考慮）
                    if (mainKey == Key.LeftCtrl || mainKey == Key.RightCtrl ||
                        mainKey == Key.LeftAlt || mainKey == Key.RightAlt ||
                        mainKey == Key.LeftShift || mainKey == Key.RightShift ||
                        mainKey == Key.LWin || mainKey == Key.RWin)
                    {
                        e.Handled = true;
                        return;
                    }

                    var parts = new System.Collections.Generic.List<string>();

                    // Windowsキーの判定を強化
                    bool winPressed = Keyboard.Modifiers.HasFlag(ModifierKeys.Windows) ||
                                     Keyboard.IsKeyDown(Key.LWin) || Keyboard.IsKeyDown(Key.RWin);

                    if (winPressed) parts.Add("Win");
                    if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control)) parts.Add("Ctrl");
                    if (Keyboard.Modifiers.HasFlag(ModifierKeys.Alt)) parts.Add("Alt");
                    if (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift)) parts.Add("Shift");

                    parts.Add(mainKey.ToString());

                    vm.EditingGlobalHotkey = string.Join("+", parts);
                    vm.IsCapturingHotkey = false;
                    e.Handled = true;
                    return;
                }

                // Alt+矢印キーなどの操作を行っても連動（Peekモード）を維持するように修正

                if (Keyboard.Modifiers.HasFlag(ModifierKeys.Alt))
                {
                    if (key == Key.Left) { vm.NavigateBackCommand.Execute(null); e.Handled = true; return; }
                    if (key == Key.Right) { vm.NavigateForwardCommand.Execute(null); e.Handled = true; return; }
                }

                // 何らかのダイアログがアクティブな場合は特殊なショートカット（並び替え、F1トグル、グローバルホットキー等）を無効化する
                bool isAnyDialogOpen = vm.IsInputMode || vm.IsDialogActive;

                // 2. 入力モード時のショートカットブロック
                if (isAnyDialogOpen)
                {
                    // 登録済みアプリの削除処理
                    if (key == Key.Delete && vm.CurrentStep == InputStep.EnteringAppPath && vm.SelectedItem != null &&
                        vm.SelectedItem.Value != "NONE" && vm.SelectedItem.Value != "ADD_REGISTERED_APP")
                    {
                        vm.IsDialogActive = true;
                        try
                        {
                            var dialog = new DeleteConfirmationWindow(vm.SelectedItem.Name) { Owner = this };
                            if (dialog.ShowDialog() == true)
                            {
                                vm.DeleteRegisteredAppCommand.Execute(vm.SelectedItem);
                            }
                        }
                        finally
                        {
                            vm.IsDialogActive = false;
                        }
                        e.Handled = true;
                        return;
                    }

                    // 必要に応じてダイアログコンテキスト内でのナビゲーションキーを許可する
                    // (ただし現在はInputFlowが独自のナビゲーションをCommitInput/MoveSelection経由で処理している)

                    // 値の入力中でなければDelete/Backを許可するべきか？
                    // いいえ、パレットレベルのほとんどのショートカットをブロックする。
                    if (e.Key == Key.F1 || e.Key == Key.Delete || e.Key == Key.Back)
                    {
                        // ただし何らかの入力特有の処理があれば許可する
                    }

                    // 並び替え用の特殊ショートカットはブロックされるべき
                    if ((Keyboard.Modifiers & ModifierKeys.Control) != 0 && (e.Key == Key.Up || e.Key == Key.Down))
                    {
                        e.Handled = true;
                        return;
                    }

                    // グローバルホットキーのトグルもブロックする
                    if (e.Key == Key.H && (Keyboard.Modifiers & ModifierKeys.Control) != 0)
                    {
                        e.Handled = true;
                        return;
                    }
                }

                // 3. ダイアログ展開時は背面のナビゲーションキーもブロック
                if (isAnyDialogOpen)
                {
                    if (vm.IsFileSearchMode || vm.CurrentStep == InputStep.EnteringName || vm.CurrentStep == InputStep.EnteringValue || vm.CurrentStep == InputStep.EnteringAppPath)
                    {
                        if (key == Key.Up) { vm.MoveSelection(-1); e.Handled = true; return; }
                        if (key == Key.Down) { vm.MoveSelection(1); e.Handled = true; return; }
                    }

                    if (key == Key.Up || key == Key.Down || key == Key.PageUp || key == Key.PageDown || key == Key.Home || key == Key.End)
                    {
                        // これらを背面のListBoxに渡さない
                        if (!vm.IsInputMode) e.Handled = true;
                        return;
                    }
                }

                // Shift + 矢印キーでの並び替え
                if (!isAnyDialogOpen && e.KeyboardDevice.Modifiers == ModifierKeys.Shift)
                {
                    if (key == Key.Up)
                    {
                        vm.MoveUpCommand.Execute(vm.SelectedItem);
                        e.Handled = true;
                        return;
                    }
                    if (key == Key.Down)
                    {
                        vm.MoveDownCommand.Execute(vm.SelectedItem);
                        e.Handled = true;
                        return;
                    }
                    if (key == Key.Right)
                    {
                        if (vm.IsOnHierarchyButton)
                        {
                            vm.EnterHierarchyWithSelectedItem();
                            e.Handled = true;
                            return;
                        }
                    }
                    if (key == Key.Left)
                    {
                        vm.ExitHierarchyWithSelectedItem();
                        e.Handled = true;
                        return;
                    }
                }

                if (vm.IsInputMode && key == Key.F1 && !vm.IsFileSearchMode)
                {
                    vm.IsRequiresArgumentChecked = !vm.IsRequiresArgumentChecked;
                    e.Handled = true;
                    return;
                }

                if (vm.IsInputMode && key == Key.F2 && !vm.IsFileSearchMode)
                {
                    if (vm.CurrentStep == InputStep.EnteringAppPath && vm.SelectedItem != null &&
                        vm.SelectedItem.Value != "NONE" && vm.SelectedItem.Value != "ADD_REGISTERED_APP")
                    {
                        vm.EditRegisteredAppCommand.Execute(vm.SelectedItem);
                        e.Handled = true;
                        return;
                    }

                    vm.IsFileSearchChecked = !vm.IsFileSearchChecked;
                    e.Handled = true;
                    return;
                }

                if (isAnyDialogOpen && key != Key.Escape && key != Key.Enter)
                {
                    if (vm.IsInputMode && (key == Key.Up || key == Key.Down))
                    {
                        vm.MoveSelection(key == Key.Down ? 1 : -1);
                        e.Handled = true;
                        return;
                    }

                    // 入力コントロールにはキーを通すが、グローバルショートカットとしては処理しない
                    return;
                }

                if (vm.IsInputMode && key == Key.Enter)
                {
                    // TextBoxに自身のバインディングでEnterを処理させる（IME互換性向上のため）
                    if (FocusManager.GetFocusedElement(this) is TextBox) return;

                    vm.CommitInputCommand.Execute(null);
                    e.Handled = true;
                    return;
                }

                // F2で選択中アイテムを編集
                if (!isAnyDialogOpen && key == Key.F2)
                {
                    vm.StartEditCommand.Execute(vm.SelectedItem);
                    e.Handled = true;
                    return;
                }

                // グローバルロジック：表示中のコマンドで、このキーに一致するホットキーがあるか確認する
                if (!isAnyDialogOpen && (e.KeyboardDevice.Modifiers == ModifierKeys.None || e.KeyboardDevice.Modifiers == ModifierKeys.Alt))
                {
                    // ナビゲーションと実行キーを直接処理（フォーカス関係の不具合回避）
                    if (key == Key.Up)
                    {
                        vm.MoveSelection(-1);
                        e.Handled = true;
                        return;
                    }
                    if (key == Key.Down)
                    {
                        vm.MoveSelection(1);
                        e.Handled = true;
                        return;
                    }
                    if (key == Key.Enter)
                    {
                        if (vm.SelectedItem != null)
                        {
                            vm.Execute(vm.SelectedItem);
                            e.Handled = true;
                            return;
                        }
                    }

                    if (key == Key.F1)
                    {
                        vm.StartShortcutAssignmentCommand.Execute(vm.SelectedItem);
                        e.Handled = true;
                        return;
                    }

                    // 矢印キーとEnter以外の場合のみ、グローバルホットキーとしての実行をチェック
                    if (key != Key.Left && key != Key.Right)
                    {
                        var parts = new System.Collections.Generic.List<string>();
                        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control)) parts.Add("Ctrl");
                        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Alt)) parts.Add("Alt");
                        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift)) parts.Add("Shift");
                        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Windows) || Keyboard.IsKeyDown(Key.LWin) || Keyboard.IsKeyDown(Key.RWin)) parts.Add("Win");
                        parts.Add(key.ToString());

                        string hotkeyStr = string.Join("+", parts);
                        if (vm.ExecuteHotkey(hotkeyStr))
                        {
                            e.Handled = true;
                            return;
                        }
                    }
                }
            }

            if (DataContext is not MainViewModel mainVm) return;

            if (key == Key.Escape)
            {
                if (mainVm.IsDialogActive)
                {
                    // ダイアログ側で処理させるか、ここでブロックする
                    e.Handled = true;
                    return;
                }
                if (mainVm.IsInputMode)
                {
                    mainVm.CancelInputCommand.Execute(null);
                    e.Handled = true;
                    return;
                }
                this.OnRequestHide();
                e.Handled = true;
                return;
            }

            // Ctrl+Shift+Arrowsでサイズ変更（段階的10px）
            if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control | ModifierKeys.Shift))
            {
                const double step = 10.0;
                if (e.Key == Key.Left)
                {
                    this.Width = Math.Max(200, this.Width - step);
                    e.Handled = true;
                    return;
                }
                else if (e.Key == Key.Right)
                {
                    this.Width = Math.Min(1600, this.Width + step);
                    e.Handled = true;
                    return;
                }
                else if (e.Key == Key.Up)
                {
                    this.Height = Math.Max(150, this.Height - step);
                    e.Handled = true;
                    return;
                }
                else if (e.Key == Key.Down)
                {
                    this.Height = Math.Min(1200, this.Height + step);
                    e.Handled = true;
                    return;
                }
            }

            // 動的ショートカット文字列の生成
            string checkKeyStr = key.ToString();
            if (key == Key.OemPlus) checkKeyStr = "Plus";
            else if (key == Key.OemComma) checkKeyStr = "Comma";
            else if (key == Key.OemMinus) checkKeyStr = "Minus";
            else if (key == Key.OemPeriod) checkKeyStr = "Period";

            var hotkeyParts = new System.Collections.Generic.List<string>();
            if (Keyboard.Modifiers.HasFlag(ModifierKeys.Windows) || Keyboard.IsKeyDown(Key.LWin) || Keyboard.IsKeyDown(Key.RWin)) hotkeyParts.Add("Win");
            if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control)) hotkeyParts.Add("Ctrl");
            if (Keyboard.Modifiers.HasFlag(ModifierKeys.Alt)) hotkeyParts.Add("Alt");
            if (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift)) hotkeyParts.Add("Shift");
            hotkeyParts.Add(checkKeyStr);

            string currentHotkeyStr = string.Join("+", hotkeyParts);

            // Settings Shortcut
            if (!string.IsNullOrEmpty(mainVm.SettingsShortcut) && currentHotkeyStr == mainVm.SettingsShortcut)
            {
                if (mainVm.OpenSettingsCommand.CanExecute(null))
                {
                    mainVm.OpenSettingsCommand.Execute(null);
                    e.Handled = true;
                    return;
                }
            }

            // Create Button Shortcut
            if (!string.IsNullOrEmpty(mainVm.CreateButtonShortcut) && currentHotkeyStr == mainVm.CreateButtonShortcut)
            {
                if (mainVm.AddNewButtonCommand.CanExecute(null))
                {
                    mainVm.AddNewButtonCommand.Execute(null);
                    e.Handled = true;
                    return;
                }
            }

            bool isAnyDialogOpenNow = mainVm.IsInputMode || mainVm.IsDialogActive;

            // Ctrl+D（複製）
            if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
            {
                if (e.Key == Key.D)
                {
                    if (!isAnyDialogOpenNow && mainVm.DuplicateCommand.CanExecute(mainVm.SelectedItem))
                    {
                        mainVm.DuplicateCommand.Execute(mainVm.SelectedItem);
                        e.Handled = true;
                        return;
                    }
                }

                if (e.Key == Key.Left || e.Key == Key.Right || e.Key == Key.Up || e.Key == Key.Down)
                {
                    // Ctrl+矢印で移動は WindowHelper で処理
                    e.Handled = true;
                }
            }
        }

        private void Window_KeyUp(object sender, KeyEventArgs e)
        {
            Key key = (e.Key == Key.ImeProcessed) ? e.ImeProcessedKey : e.Key;
            if (key == Key.System) key = e.SystemKey;
            _pressedKeys.Remove(key);

            if (DataContext is MainViewModel vm)
            {
                if ((key == Key.LeftShift || key == Key.RightShift) && vm.IsOnHierarchyButton)
                {
                    vm.EnterHierarchyWithSelectedItem();
                }

                // Altキーが離された場合、待機中であればウィンドウを閉じる
                if (key == Key.LeftAlt || key == Key.RightAlt)
                {
                    if (_isWaitingForAltRelease)
                    {
                        _isWaitingForAltRelease = false;
                        // Altを離した際に、現在選択されているアイテムを実行（確定）する
                        if (vm.SelectedItem != null)
                        {
                            vm.Execute(vm.SelectedItem);
                        }
                        else
                        {
                            OnRequestHide();
                        }
                    }
                }
            }
        }

        private void OnRendering(object? sender, EventArgs e)
        {
            // 移動処理は WindowHelper で行われるため、現在は最小限の更新のみ
            if (!this.IsVisible)
            {
                _lastRenderingTime = TimeSpan.Zero;
                return;
            }
        }

        protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
        {
            base.OnClosing(e);
            this.Hide();
        }

        private void ListBoxItem_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            _dragStartPoint = e.GetPosition(null);
        }

        private void ListBoxItem_MouseMove(object sender, MouseEventArgs e)
        {
            if (e.LeftButton == MouseButtonState.Pressed)
            {
                Point currentPoint = e.GetPosition(null);
                if (Math.Abs(currentPoint.X - _dragStartPoint.X) > SystemParameters.MinimumHorizontalDragDistance ||
                    Math.Abs(currentPoint.Y - _dragStartPoint.Y) > SystemParameters.MinimumVerticalDragDistance)
                {
                    if (sender is ListBoxItem listBoxItem && listBoxItem.DataContext is CommandEntry command)
                    {
                        // システムボタンはドラッグ不可
                        if (command.Type == CommandType.Command && (command.Value?.StartsWith("ADD_") ?? false)) return;

                        DragDrop.DoDragDrop(listBoxItem, command, DragDropEffects.Move);
                    }
                }
            }
        }

        private void ListBoxItem_Drop(object sender, DragEventArgs e)
        {
            if (e.Data.GetData(typeof(CommandEntry)) is CommandEntry droppedCommand)
            {
                if (sender is ListBoxItem listBoxItem && listBoxItem.DataContext is CommandEntry targetCommand)
                {
                    if (DataContext is MainViewModel vm)
                    {
                        int oldIndex = vm.DisplayCommands.IndexOf(droppedCommand);
                        int newIndex = vm.DisplayCommands.IndexOf(targetCommand);

                        if (oldIndex >= 0 && newIndex >= 0 && oldIndex != newIndex)
                        {
                            vm.MoveItem(droppedCommand, newIndex - oldIndex);
                        }
                    }
                }
            }
        }

        private void TextBox_GotFocus(object sender, RoutedEventArgs e)
        {
            if (sender is TextBox textBox)
            {
                Dispatcher.BeginInvoke(new Action(() => textBox.SelectAll()));
            }
        }

        private void SaveBoundsToViewModel()
        {
            if (DataContext is MainViewModel vm)
            {
                vm.WindowWidth = this.Width;
                vm.WindowHeight = this.Height;
                vm.WindowTop = this.Top;
                vm.WindowLeft = this.Left;
            }
        }
    }
}
