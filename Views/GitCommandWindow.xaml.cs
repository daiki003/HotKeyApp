using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using HotKeyCommandApp.Services;
using HotKeyCommandApp.ViewModels;
using HotKeyCommandApp.Models;


namespace HotKeyCommandApp.Views
{
    public partial class GitCommandWindow : Window
    {
        private readonly MainViewModel _mainVm;
        private TimeSpan _lastRenderingTime = TimeSpan.Zero;
        private bool _isDiffWindowOpen;
        private bool _isCommitDialogOpen;
        private bool _isConsoleAtBottom = true;

        public GitCommandWindow(string repositoryPath, MainViewModel mainVm)
        {
            _mainVm = mainVm;
            InitializeComponent();
            var vm = new GitCommandViewModel(repositoryPath, _mainVm);
            DataContext = vm;
            vm.PropertyChanged += (s, e) =>
            {
                if (e.PropertyName == nameof(GitCommandViewModel.IsFrontMode))
                {
                    this.Topmost = vm.IsFrontMode;
                }
                else if (e.PropertyName == nameof(GitCommandViewModel.ConsoleOutput))
                {
                    TryAutoScrollConsole();
                }
                else if (e.PropertyName == nameof(GitCommandViewModel.IsOperateMode) && vm.IsOperateMode)
                {
                    _branchAction = 0;
                    _isCurrentBranchSelected = false;
                    _isAddBranchSelected = BranchList.Items.Count == 0;
                    BranchList.SelectedIndex = BranchList.Items.Count > 0 ? 0 : -1;
                    if (vm.IsStashMode) FocusSelectedBranchButton();
                    else Dispatcher.BeginInvoke(() => BranchSearchBox.Focus(), System.Windows.Threading.DispatcherPriority.Loaded);
                }
            };

            vm.RequestArgumentInput += (command, prompt) =>
            {
                var dialog = new ArgumentInputDialog(command, prompt) { Owner = this };
                dialog.WindowStartupLocation = WindowStartupLocation.CenterOwner;
                if (dialog.ShowDialog() == true)
                {
                    return dialog.InputText;
                }
                return null;
            };

            vm.RequestSelectInput += (command, prompt, options) =>
            {
                var dialog = new ArgumentSelectDialog(command, prompt, options) { Owner = this };
                dialog.WindowStartupLocation = WindowStartupLocation.CenterOwner;
                if (dialog.ShowDialog() == true)
                {
                    return dialog.SelectedOption;
                }
                return null;
            };
            CompositionTarget.Rendering += OnRendering;

            if (!double.IsNaN(_mainVm.GitWindowTop) && !double.IsNaN(_mainVm.GitWindowLeft))
            {
                this.WindowStartupLocation = WindowStartupLocation.Manual;
                this.Top = _mainVm.GitWindowTop;
                this.Left = _mainVm.GitWindowLeft;
            }

            if (!double.IsNaN(_mainVm.GitWindowWidth) && _mainVm.GitWindowWidth > 0)
            {
                this.Width = _mainVm.GitWindowWidth;
            }

            if (!double.IsNaN(_mainVm.GitWindowHeight) && _mainVm.GitWindowHeight > 0)
            {
                this.Height = _mainVm.GitWindowHeight;
            }

            this.Loaded += (s, e) => 
            {
                CommandInputBox.Focus();
                WindowHelper.EnableWindowDragMove(this);
                Dispatcher.BeginInvoke(() => ConsoleScrollViewer.ScrollToEnd(), System.Windows.Threading.DispatcherPriority.Loaded);
            };

            this.Activated += (s, e) =>
            {
                if (DataContext is GitCommandViewModel vm && !vm.IsMoveMode && !vm.IsHistoryMode && !vm.IsOperateMode)
                {
                    Dispatcher.BeginInvoke(() => CommandInputBox.Focus(), System.Windows.Threading.DispatcherPriority.Input);
                }
            };

            this.LocationChanged += (s, e) =>
            {
                _mainVm.GitWindowTop = this.Top;
                _mainVm.GitWindowLeft = this.Left;
            };

            this.SizeChanged += (s, e) =>
            {
                _mainVm.GitWindowWidth = this.Width;
                _mainVm.GitWindowHeight = this.Height;
            };

            this.PreviewMouseLeftButtonDown += (s, e) =>
            {
                if (DataContext is GitCommandViewModel vm && !vm.IsMoveMode && !vm.IsHistoryMode && !vm.IsOperateMode)
                {
                    Dispatcher.BeginInvoke(() => CommandInputBox.Focus(), System.Windows.Threading.DispatcherPriority.Input);
                }
            };

            this.Closing += (s, e) =>
            {
                if (DataContext is GitCommandViewModel vm)
                {
                    vm.CancelCurrentExecution();
                }

                CompositionTarget.Rendering -= OnRendering;
                _mainVm.SaveWindowBounds();
            };
        }

        private DateTime _lastShiftPressTime = DateTime.MinValue;
        private int _branchAction;
        private bool _isAddBranchSelected;
        private bool _isCurrentBranchSelected;
        private int _repositoryActionIndex;

        private void FocusCurrentBranchButton()
        {
            Dispatcher.BeginInvoke(() =>
            {
                if (DataContext is not GitCommandViewModel vm || vm.IsBranchOperationBusy || vm.CurrentBranchItem == null) return;
                CurrentBranchRow.UpdateLayout();
                FindBranchButton(CurrentBranchRow, _branchAction == 0 ? "move" : "delete")?.Focus();
            }, System.Windows.Threading.DispatcherPriority.Loaded);
        }

        private void FocusRepositoryAction(int index)
        {
            _isCurrentBranchSelected = false;
            _isAddBranchSelected = false;
            _repositoryActionIndex = Math.Clamp(index, 0, 2);
            new[] { DiffButton, CommitButton, PushButton }[_repositoryActionIndex].Focus();
        }

        private void RepositoryActionButton_GotKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
        {
            _isCurrentBranchSelected = false;
            _isAddBranchSelected = false;
            _repositoryActionIndex = ReferenceEquals(sender, CommitButton) ? 1 : ReferenceEquals(sender, PushButton) ? 2 : 0;
        }

        private void FocusSelectedBranchButton(bool fromSearch = false)
        {
            Dispatcher.BeginInvoke(() =>
            {
                if (DataContext is not GitCommandViewModel vm || !vm.IsOperateMode || vm.IsBranchOperationBusy) return;
                if (!fromSearch && BranchSearchBox.IsKeyboardFocusWithin) return;
                if (_isCurrentBranchSelected) { FocusCurrentBranchButton(); return; }
                if (RepositoryActionButtons.IsKeyboardFocusWithin) return;
                if (_isAddBranchSelected)
                {
                    AddBranchButton.Focus();
                    return;
                }
                if (BranchList.SelectedItem == null)
                {
                    AddBranchButton.Focus();
                    return;
                }
                BranchList.ScrollIntoView(BranchList.SelectedItem);
                BranchList.UpdateLayout();
                if (BranchList.ItemContainerGenerator.ContainerFromItem(BranchList.SelectedItem) is ListBoxItem item)
                {
                    FindBranchButton(item, _branchAction == 0 ? "move" : "delete")?.Focus();
                }
            }, System.Windows.Threading.DispatcherPriority.Loaded);
        }

        private static Button? FindBranchButton(DependencyObject parent, string tag)
        {
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
            {
                var child = VisualTreeHelper.GetChild(parent, i);
                if (child is Button button && Equals(button.Tag, tag)) return button;
                var match = FindBranchButton(child, tag);
                if (match != null) return match;
            }
            return null;
        }

        private void BranchList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (BranchList.SelectedItem != null)
            {
                _isAddBranchSelected = false;
                if (BranchList.IsKeyboardFocusWithin) _isCurrentBranchSelected = false;
                if (!BranchSearchBox.IsKeyboardFocusWithin) FocusSelectedBranchButton();
            }
        }

        private void AddBranchButton_GotKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
        {
            _isAddBranchSelected = true;
            _isCurrentBranchSelected = false;
        }

        private void BranchButton_GotKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
        {
            if (sender is not Button button || (button.DataContext is not BranchListItem && button.DataContext is not StashListItem)) return;
            _isAddBranchSelected = false;
            _branchAction = Equals(button.Tag, "delete") ? 1 : 0;
            _isCurrentBranchSelected = button.DataContext is BranchListItem { IsCurrent: true };
            if (!_isCurrentBranchSelected) BranchList.SelectedItem = button.DataContext;
        }

        private async void AddBranchButton_Click(object sender, RoutedEventArgs e)
        {
            if (DataContext is not GitCommandViewModel vm || vm.IsBranchOperationBusy) return;
            _isAddBranchSelected = true;
            _isCurrentBranchSelected = false;
            var dialog = new BranchNameDialog(vm.IsStashMode) { Owner = this };
            if (dialog.ShowDialog() != true)
            {
                FocusSelectedBranchButton();
                return;
            }

            string branchName = dialog.BranchName;
            bool created = vm.IsStashMode ? await vm.OperateStashAsync(null, false, branchName) : await vm.CreateBranchAsync(branchName);
            if (!vm.IsOperateMode) return;
            if (created)
            {
                if (vm.IsStashMode && vm.Stashes.Count > 0)
                {
                    _isAddBranchSelected = false;
                    _branchAction = 0;
                    BranchList.SelectedIndex = 0;
                }
                else
                {
                    if (branchName.IndexOf(vm.BranchSearchText.Trim(), StringComparison.OrdinalIgnoreCase) < 0)
                        vm.BranchSearchText = string.Empty;
                    foreach (var branch in vm.Branches)
                    {
                        if (branch.Name == branchName)
                        {
                            _isAddBranchSelected = false;
                            _branchAction = 0;
                            BranchList.SelectedItem = branch;
                            break;
                        }
                    }
                }
            }
            FocusSelectedBranchButton();
        }

        private async void BranchButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button button || (button.DataContext is not BranchListItem && button.DataContext is not StashListItem) ||
                DataContext is not GitCommandViewModel vm || vm.IsBranchOperationBusy) return;

            object item = button.DataContext;
            var branch = item as BranchListItem;
            var stash = item as StashListItem;
            bool delete = Equals(button.Tag, "delete");
            _isAddBranchSelected = false;
            _branchAction = delete ? 1 : 0;
            _isCurrentBranchSelected = branch?.IsCurrent == true;
            if (!_isCurrentBranchSelected) BranchList.SelectedItem = item;
            int previousIndex = BranchList.SelectedIndex;
            bool forceDelete = false;
            if (delete)
            {
                var dialog = new DeleteConfirmationWindow(branch?.Name ?? stash!.DisplayName, allowForceDelete: branch != null,
                    forceDelete: branch != null && vm.ForceDeleteBranches, onForceDeleteChanged: vm.SetForceDeleteBranches) { Owner = this };
                if (dialog.ShowDialog() != true)
                {
                    _branchAction = 1;
                    if (!_isCurrentBranchSelected) BranchList.SelectedItem = item;
                    FocusSelectedBranchButton();
                    return;
                }
                forceDelete = dialog.ForceDelete;
            }
            _branchAction = delete ? 1 : 0;
            if (!_isCurrentBranchSelected) BranchList.SelectedItem = item;
            if (branch != null) await vm.OperateBranchAsync(branch, delete, forceDelete);
            else await vm.OperateStashAsync(stash, delete);
            if (!vm.IsOperateMode) return;
            if (branch?.IsCurrent == true)
            {
                _isCurrentBranchSelected = true;
                FocusCurrentBranchButton();
                return;
            }
            _isCurrentBranchSelected = false;
            int matchingIndex = -1;
            for (int i = 0; i < BranchList.Items.Count; i++)
            {
                if (ReferenceEquals(BranchList.Items[i], item)) { matchingIndex = i; break; }
            }
            BranchList.SelectedIndex = matchingIndex >= 0 ? matchingIndex : Math.Min(previousIndex, BranchList.Items.Count - 1);
            FocusSelectedBranchButton();
        }

        private void DiffButton_Click(object sender, RoutedEventArgs e)
        {
            if (DataContext is not GitCommandViewModel vm || vm.IsBranchOperationBusy || _isDiffWindowOpen) return;
            _isDiffWindowOpen = true;
            try
            {
                var window = new GitDiffWindow(vm.RepositoryPath, vm.MovementSpeed) { Owner = this, Topmost = Topmost };
                window.ShowDialog();
            }
            finally
            {
                _isDiffWindowOpen = false;
                DiffButton.Focus();
            }
        }

        private async void CommitButton_Click(object sender, RoutedEventArgs e)
        {
            if (DataContext is not GitCommandViewModel vm || vm.IsBranchOperationBusy || _isCommitDialogOpen) return;
            _isCommitDialogOpen = true;
            try
            {
                var dialog = new CommitMessageDialog { Owner = this };
                if (dialog.ShowDialog() == true) await vm.CommitAllAsync(dialog.Message);
            }
            finally { _isCommitDialogOpen = false; CommitButton.Focus(); }
        }

        private async void PushButton_Click(object sender, RoutedEventArgs e)
        {
            if (DataContext is not GitCommandViewModel vm || vm.IsBranchOperationBusy) return;
            await vm.PushCurrentBranchAsync();
            PushButton.Focus();
        }

        private void FocusCommandInputBox()
        {
            Dispatcher.BeginInvoke(() =>
            {
                CommandInputBox.Focus();
                CommandInputBox.CaretIndex = CommandInputBox.Text.Length;
            }, System.Windows.Threading.DispatcherPriority.Input);
        }

        private void TryAutoScrollConsole()
        {
            if (!_isConsoleAtBottom)
            {
                return;
            }

            Dispatcher.BeginInvoke(() =>
            {
                ConsoleScrollViewer.ScrollToEnd();
                _isConsoleAtBottom = true;
            }, System.Windows.Threading.DispatcherPriority.Background);
        }

        private void ConsoleScrollViewer_ScrollChanged(object sender, ScrollChangedEventArgs e)
        {
            if (e.ExtentHeightChange == 0)
            {
                _isConsoleAtBottom = ConsoleScrollViewer.VerticalOffset + ConsoleScrollViewer.ViewportHeight >= ConsoleScrollViewer.ExtentHeight - 1.0;
            }
        }

        private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if ((e.Key == Key.LeftShift || e.Key == Key.RightShift) && !e.IsRepeat)
            {
                var now = DateTime.Now;
                if ((now - _lastShiftPressTime).TotalMilliseconds <= 400)
                {
                    if (DataContext is GitCommandViewModel vmMove && !vmMove.IsMoveMode && !vmMove.IsOperateMode)
                    {
                        vmMove.IsMoveMode = true;
                        e.Handled = true;
                        _lastShiftPressTime = DateTime.MinValue;
                        return;
                    }
                }
                _lastShiftPressTime = now;
            }

            if (DataContext is GitCommandViewModel vm)
            {
                if (e.Key == Key.Escape)
                {
                    if (vm.IsOperateMode)
                    {
                        if (!vm.IsBranchOperationBusy)
                        {
                            vm.IsOperateMode = false;
                            FocusCommandInputBox();
                        }
                        e.Handled = true;
                        return;
                    }
                    if (vm.IsMoveMode)
                    {
                        vm.IsMoveMode = false;
                        FocusCommandInputBox();
                        e.Handled = true;
                        return;
                    }

                    if (vm.IsHistoryMode)
                    {
                        vm.IsHistoryMode = false;
                        FocusCommandInputBox();
                        e.Handled = true;
                        return;
                    }

                    vm.CancelCurrentExecution();
                    this.Close();
                    e.Handled = true;
                    return;
                }

                if (vm.IsOperateMode)
                {
                    if (!vm.IsStashMode && e.Key == Key.D && Keyboard.Modifiers == ModifierKeys.Control)
                    {
                        e.Handled = true;
                        DiffButton_Click(DiffButton, new RoutedEventArgs());
                        return;
                    }
                    if (RepositoryActionButtons.IsKeyboardFocusWithin)
                    {
                        if (vm.IsBranchOperationBusy || Keyboard.Modifiers != ModifierKeys.None) return;
                        if (e.Key == Key.Left) { FocusRepositoryAction(_repositoryActionIndex - 1); e.Handled = true; }
                        else if (e.Key == Key.Right) { FocusRepositoryAction(_repositoryActionIndex + 1); e.Handled = true; }
                        else if (e.Key == Key.Up)
                        {
                            if (vm.CurrentBranchItem != null) { _isCurrentBranchSelected = true; _branchAction = 0; FocusCurrentBranchButton(); }
                            e.Handled = true;
                        }
                        else if (e.Key == Key.Down) { BranchSearchBox.Focus(); e.Handled = true; }
                        else if (e.Key == Key.Enter)
                        {
                            e.Handled = true;
                            if (!e.IsRepeat) new[] { DiffButton, CommitButton, PushButton }[_repositoryActionIndex].RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                        }
                        return;
                    }
                    if (_isCurrentBranchSelected && CurrentBranchRow.IsKeyboardFocusWithin)
                    {
                        if (vm.IsBranchOperationBusy || Keyboard.Modifiers != ModifierKeys.None) return;
                        if (e.Key == Key.Down) { FocusRepositoryAction(_repositoryActionIndex); e.Handled = true; }
                        else if (e.Key == Key.Left || e.Key == Key.Right)
                        {
                            _branchAction = e.Key == Key.Left ? 0 : 1;
                            FocusCurrentBranchButton();
                            e.Handled = true;
                        }
                        else if (e.Key == Key.Up) e.Handled = true;
                        else if (e.Key == Key.Enter)
                        {
                            e.Handled = true;
                            if (!e.IsRepeat) FindBranchButton(CurrentBranchRow, _branchAction == 0 ? "move" : "delete")?.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                        }
                        return;
                    }
                    if (!vm.IsStashMode && BranchSearchBox.IsKeyboardFocusWithin)
                    {
                        if (!vm.IsBranchOperationBusy && Keyboard.Modifiers == ModifierKeys.None && e.Key == Key.Up)
                        {
                            FocusRepositoryAction(_repositoryActionIndex);
                            e.Handled = true;
                            return;
                        }
                        if (!vm.IsBranchOperationBusy && Keyboard.Modifiers == ModifierKeys.None && (e.Key == Key.Down || e.Key == Key.Enter))
                        {
                            if (BranchList.Items.Count > 0)
                            {
                                BranchList.SelectedIndex = 0;
                                _isAddBranchSelected = false;
                                FocusSelectedBranchButton(fromSearch: true);
                            }
                            else if (e.Key == Key.Down)
                            {
                                _isAddBranchSelected = true;
                                FocusSelectedBranchButton(fromSearch: true);
                            }
                            e.Handled = true;
                        }
                        return;
                    }
                    if (!vm.IsBranchOperationBusy && Keyboard.Modifiers == ModifierKeys.None)
                    {
                        if (e.Key == Key.Enter && !e.IsRepeat && _isAddBranchSelected)
                        {
                            AddBranchButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                            e.Handled = true;
                            return;
                        }
                        if (_isAddBranchSelected && e.Key == Key.Up && BranchList.Items.Count > 0)
                        {
                            _isAddBranchSelected = false;
                            BranchList.SelectedIndex = BranchList.Items.Count - 1;
                            FocusSelectedBranchButton();
                            e.Handled = true;
                            return;
                        }
                        if (!vm.IsStashMode && _isAddBranchSelected && e.Key == Key.Up && BranchList.Items.Count == 0)
                        {
                            BranchSearchBox.Focus();
                            e.Handled = true;
                            return;
                        }
                        if (!_isAddBranchSelected && e.Key == Key.Down && BranchList.SelectedIndex == BranchList.Items.Count - 1)
                        {
                            _isAddBranchSelected = true;
                            FocusSelectedBranchButton();
                            e.Handled = true;
                            return;
                        }
                        if (_isAddBranchSelected && (e.Key == Key.Up || e.Key == Key.Down || e.Key == Key.Left || e.Key == Key.Right))
                        {
                            e.Handled = true;
                            return;
                        }
                        if (!vm.IsStashMode && !_isAddBranchSelected && e.Key == Key.Up && BranchList.SelectedIndex == 0)
                        {
                            BranchSearchBox.Focus();
                            e.Handled = true;
                            return;
                        }
                        if (e.Key == Key.Enter && !e.IsRepeat && BranchList.SelectedItem != null &&
                            BranchList.ItemContainerGenerator.ContainerFromItem(BranchList.SelectedItem) is ListBoxItem item)
                        {
                            FindBranchButton(item, _branchAction == 0 ? "move" : "delete")?.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                            e.Handled = true;
                            return;
                        }
                        if (e.Key == Key.Up && BranchList.SelectedIndex > 0) BranchList.SelectedIndex--;
                        else if (e.Key == Key.Down && BranchList.SelectedIndex < BranchList.Items.Count - 1) BranchList.SelectedIndex++;
                        else if (e.Key == Key.Left) _branchAction = 0;
                        else if (e.Key == Key.Right) _branchAction = 1;
                        if (e.Key == Key.Up || e.Key == Key.Down || e.Key == Key.Left || e.Key == Key.Right)
                        {
                            FocusSelectedBranchButton();
                            e.Handled = true;
                            return;
                        }
                    }
                    return;
                }

                if (vm.IsMoveMode)
                {
                    if (Keyboard.Modifiers == ModifierKeys.Shift)
                    {
                        const double step = 10.0;
                        if (e.Key == Key.Left)
                        {
                            this.Width = Math.Max(300, this.Width - step);
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
                            this.Height = Math.Max(200, this.Height - step);
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
                    else if (Keyboard.Modifiers == ModifierKeys.None)
                    {
                        if (e.Key == Key.Left || e.Key == Key.Right || e.Key == Key.Up || e.Key == Key.Down)
                        {
                            e.Handled = true;
                            return;
                        }
                    }
                }
            }

            if (e.Key == Key.OemComma && Keyboard.Modifiers == ModifierKeys.Control)
            {
                OpenGitSettingsDialog();
                e.Handled = true;
                return;
            }
        }

        private void SettingsButton_Click(object sender, RoutedEventArgs e)
        {
            OpenGitSettingsDialog();
        }

        private void OpenGitSettingsDialog()
        {
            var dialog = new GitSettingsDialog();
            dialog.Owner = this;
            dialog.WindowStartupLocation = WindowStartupLocation.CenterOwner;
            dialog.ShowDialog();
            
            // Reload settings
            if (DataContext is GitCommandViewModel vm)
            {
                vm.ReloadSettings();
            }
            Dispatcher.BeginInvoke(() => CommandInputBox.Focus(), System.Windows.Threading.DispatcherPriority.Input);
        }

        private void CommandInputBox_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (DataContext is GitCommandViewModel vm)
            {
                if (e.Key == Key.Up)
                {
                    vm.NavigateHistory(up: true);
                    FocusCommandInputBox();
                    e.Handled = true;
                }
                else if (e.Key == Key.Down)
                {
                    vm.NavigateHistory(up: false);
                    FocusCommandInputBox();
                    e.Handled = true;
                }
            }
        }

        private void OnRendering(object? sender, EventArgs e)
        {
            if (!this.IsVisible || !this.IsActive) return;
            if (DataContext is not GitCommandViewModel vm || !vm.IsMoveMode) return;

            if (e is not RenderingEventArgs args) return;

            if (_lastRenderingTime == args.RenderingTime) return;

            if (_lastRenderingTime == TimeSpan.Zero)
            {
                _lastRenderingTime = args.RenderingTime;
                return;
            }

            double deltaTime = (args.RenderingTime - _lastRenderingTime).TotalSeconds;
            _lastRenderingTime = args.RenderingTime;

            if (deltaTime > 0.1) deltaTime = 0.1;

            if (!Keyboard.IsKeyDown(Key.LeftShift) && !Keyboard.IsKeyDown(Key.RightShift))
            {
                double speed = vm.MovementSpeed;
                double distance = speed * deltaTime;

                if (Keyboard.IsKeyDown(Key.Left)) this.Left -= distance;
                if (Keyboard.IsKeyDown(Key.Right)) this.Left += distance;
                if (Keyboard.IsKeyDown(Key.Up)) this.Top -= distance;
                if (Keyboard.IsKeyDown(Key.Down)) this.Top += distance;
            }
        }
    }
}
