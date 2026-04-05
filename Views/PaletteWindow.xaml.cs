using System;
using System.Windows;
using System.Windows.Input;
using System.Windows.Controls;
using System.Windows.Media;
using System.Collections.Generic;
using HotKeyCommandApp.ViewModels;
using HotKeyCommandApp.Services;
using HotKeyCommandApp.Models;

namespace HotKeyCommandApp.Views
{
    public partial class PaletteWindow : Window
    {
        private readonly HotKeyService _hotKeyService;
        private readonly GlobalKeyHookService _globalKeyHookService;
        private readonly HashSet<Key> _pressedKeys = new HashSet<Key>();
        private TimeSpan _lastRenderingTime = TimeSpan.Zero;
        private Point _dragStartPoint;

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
                // In EnteringAppPath, we want to focus the ListBox items immediately
                if (vm.CurrentStep == InputStep.EnteringAppPath)
                {
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
            if (id == 9000) // Default Hotkey (Win+Alt+Z)
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
                        this.Activate();
                        this.Focus();
                        Dispatcher.BeginInvoke(new Action(() =>
                        {
                            FocusListBox();
                        }), System.Windows.Threading.DispatcherPriority.Render);
                    }
                }
                else
                {
                    OnRequestShow();
                }
            }
            else
            {
                // Global Shortcut for specific commands
                if (DataContext is MainViewModel vm)
                {
                    // ウィンドウが非表示の場合は、ジャンプ前に状態をリセットする
                    if (!this.IsVisible)
                    {
                        vm.LoadCommands();
                    }
                    vm.ExecuteHotkeyById(id);
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
                // Clear any lingering search if we're not in a state that supports it
                if (!vm.IsInputMode && !vm.IsFileSearchMode && !vm.IsDialogActive)
                {
                    vm.InputText = string.Empty;
                }
                if (vm.IsInputMode)
                {
                    vm.CancelInputCommand.Execute(null);
                    return;
                }
                // IsSettingsMode logic removed - already handled by separate window
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

                // Only focus the list item if we AREN'T in input mode
                if (!vm.IsInputMode)
                {
                    // Update layout immediately to ensure containers are generated
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

        private void Window_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.LeftButton == MouseButtonState.Pressed)
            {
                this.DragMove();
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

                if (Keyboard.Modifiers.HasFlag(ModifierKeys.Alt))
                {
                    if (key == Key.Left) { vm.NavigateBackCommand.Execute(null); e.Handled = true; return; }
                    if (key == Key.Right) { vm.NavigateForwardCommand.Execute(null); e.Handled = true; return; }
                }

                // Disable special shortcuts (reordering, F1 toggle, global hotkeys) when any dialog is active
                bool isAnyDialogOpen = vm.IsInputMode || vm.IsDialogActive;

                // 2. Shortcut blocking when in input mode
                if (isAnyDialogOpen)
                {
                    // Handle Delete for Registered Apps
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

                    // Allow navigation keys within the dialog context if needed
                    // (Though currently InputFlow handles its own navigation via CommitInput/MoveSelection)

                    // Allow Delete/Back only if not in value input? 
                    // No, let's just block most "palette" level shortcuts.
                    if (e.Key == Key.F1 || e.Key == Key.Delete || e.Key == Key.Back)
                    {
                        // But allow input-specific handling if any?
                    }

                    // Special reordering shortcuts should be blocked
                    if ((Keyboard.Modifiers & ModifierKeys.Control) != 0 && (e.Key == Key.Up || e.Key == Key.Down))
                    {
                        e.Handled = true;
                        return;
                    }

                    // Also block the "Global Hotkey" toggle
                    if (e.Key == Key.H && (Keyboard.Modifiers & ModifierKeys.Control) != 0)
                    {
                        e.Handled = true;
                        return;
                    }
                }

                // 2. Block background navigation keys when any dialog is open
                if (isAnyDialogOpen)
                {
                    if (vm.IsFileSearchMode || vm.CurrentStep == InputStep.EnteringName || vm.CurrentStep == InputStep.EnteringValue || vm.CurrentStep == InputStep.EnteringAppPath)
                    {
                        if (key == Key.Up) { vm.MoveSelection(-1); e.Handled = true; return; }
                        if (key == Key.Down) { vm.MoveSelection(1); e.Handled = true; return; }
                    }

                    if (key == Key.Up || key == Key.Down || key == Key.PageUp || key == Key.PageDown || key == Key.Home || key == Key.End)
                    {
                        // Don't let these through to the ListBox
                        if (!vm.IsInputMode) e.Handled = true;
                        return;
                    }
                }

                // Shift + Arrows for reordering
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

                    // Let the keys pass through to input controls, but don't process as global shortcuts
                    return;
                }

                if (vm.IsInputMode && key == Key.Enter)
                {
                    // Let TextBox handle Enter its own bindings (better IME compatibility)
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

                // Global logic: check if any displayed command has a hotkey that matches this key
                if (!isAnyDialogOpen && e.KeyboardDevice.Modifiers == ModifierKeys.None)
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
                    // Let the dialog handle it or just block it here
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
                    this.Width += step;
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
                    this.Height += step;
                    e.Handled = true;
                    return;
                }
            }
            // Ctrl+Arrows（移動および設定のショートカット）
            else if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
            {
                if (e.Key == Key.OemComma)
                {
                    if (mainVm.OpenSettingsCommand.CanExecute(null))
                    {
                        mainVm.OpenSettingsCommand.Execute(null);
                        e.Handled = true;
                        return;
                    }
                }

                if (e.Key == Key.Left || e.Key == Key.Right || e.Key == Key.Up || e.Key == Key.Down)
                {
                    // Ctrl+矢印で移動は Rendering で処理
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
            }
        }

        private void OnRendering(object? sender, EventArgs e)
        {
            if (!this.IsVisible)
            {
                _lastRenderingTime = TimeSpan.Zero;
                return;
            }

            var args = (RenderingEventArgs)e;
            if (_lastRenderingTime == args.RenderingTime) return;

            if (_lastRenderingTime == TimeSpan.Zero)
            {
                _lastRenderingTime = args.RenderingTime;
                return;
            }

            double deltaTime = (args.RenderingTime - _lastRenderingTime).TotalSeconds;
            _lastRenderingTime = args.RenderingTime;

            // Cap delta time to prevent huge jumps if the app freezes
            if (deltaTime > 0.1) deltaTime = 0.1;

            if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control) && !Keyboard.Modifiers.HasFlag(ModifierKeys.Shift))
            {
                double speed = 1200.0;
                if (DataContext is MainViewModel vm)
                {
                    speed = vm.MovementSpeed;
                }
                double distance = speed * deltaTime;

                // Ctrl+矢印で移動（既存）
                if (_pressedKeys.Contains(Key.Left)) { this.Left -= distance; }
                if (_pressedKeys.Contains(Key.Right)) { this.Left += distance; }
                if (_pressedKeys.Contains(Key.Up)) { this.Top -= distance; }
                if (_pressedKeys.Contains(Key.Down)) { this.Top += distance; }
            }
        }

        protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
        {
            e.Cancel = true;
            this.Hide();
        }

        private void ListBoxItem_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (sender is ListBoxItem item)
            {
                _dragStartPoint = e.GetPosition(null);
            }
        }

        private void ListBoxItem_MouseMove(object sender, MouseEventArgs e)
        {
            if (e.LeftButton == MouseButtonState.Pressed)
            {
                Point currentPosition = e.GetPosition(null);
                if (Math.Abs(currentPosition.X - _dragStartPoint.X) > SystemParameters.MinimumHorizontalDragDistance ||
                    Math.Abs(currentPosition.Y - _dragStartPoint.Y) > SystemParameters.MinimumVerticalDragDistance)
                {
                    if (sender is ListBoxItem item && item.DataContext is CommandEntry entry)
                    {
                        // Don't drag system commands
                        if (entry.Type == CommandType.Command) return;

                        DragDrop.DoDragDrop(item, entry, DragDropEffects.Move);
                    }
                }
            }
        }

        private void ListBoxItem_Drop(object sender, DragEventArgs e)
        {
            if (e.Data.GetData(typeof(CommandEntry)) is CommandEntry droppedEntry)
            {
                if (sender is ListBoxItem targetItem && targetItem.DataContext is CommandEntry targetEntry)
                {
                    if (DataContext is MainViewModel vm)
                    {
                        // Move item to new position
                        int oldIndex = vm.DisplayCommands.IndexOf(droppedEntry);
                        int newIndex = vm.DisplayCommands.IndexOf(targetEntry);

                        if (oldIndex >= 0 && newIndex >= 0 && oldIndex != newIndex)
                        {
                            vm.MoveItem(droppedEntry, newIndex - oldIndex);
                        }
                    }
                }
            }
        }


        private void TextBox_GotFocus(object sender, RoutedEventArgs e)
        {
            if (sender is TextBox textBox)
            {
                // Immediate move to end
                textBox.CaretIndex = textBox.Text.Length;

                // Using Background priority for smoother results when Tabbing
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    if (textBox.IsFocused)
                        textBox.CaretIndex = textBox.Text.Length;
                }), System.Windows.Threading.DispatcherPriority.Background);
            }
        }

        private void SaveBoundsToViewModel()
        {
            if (DataContext is MainViewModel vm)
            {
                if (!double.IsNaN(this.Width)) vm.WindowWidth = this.Width;
                if (!double.IsNaN(this.Height)) vm.WindowHeight = this.Height;
                vm.WindowTop = this.Top;
                vm.WindowLeft = this.Left;
                vm.SaveWindowBounds();
            }
        }

    }

    public class ImageSourceToVisibilityConverter : System.Windows.Data.IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
        {
            return value != null ? Visibility.Visible : Visibility.Collapsed;
        }
        public object ConvertBack(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}
