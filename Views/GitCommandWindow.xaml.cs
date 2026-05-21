using System;
using System.Windows;
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
            };

            this.Activated += (s, e) =>
            {
                if (DataContext is GitCommandViewModel vm && !vm.IsMoveMode && !vm.IsHistoryMode)
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
                if (DataContext is GitCommandViewModel vm && !vm.IsMoveMode && !vm.IsHistoryMode)
                {
                    Dispatcher.BeginInvoke(() => CommandInputBox.Focus(), System.Windows.Threading.DispatcherPriority.Input);
                }
            };

            this.Closing += (s, e) =>
            {
                CompositionTarget.Rendering -= OnRendering;
                _mainVm.SaveWindowBounds();
            };
        }

        private DateTime _lastShiftPressTime = DateTime.MinValue;

        private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.LeftShift || e.Key == Key.RightShift)
            {
                var now = DateTime.Now;
                if ((now - _lastShiftPressTime).TotalMilliseconds <= 400)
                {
                    if (DataContext is GitCommandViewModel vmMove && !vmMove.IsMoveMode)
                    {
                        vmMove.IsMoveMode = true;
                        vmMove.SetConsoleOutput("ウィンドウ移動モード", "・矢印: ウィンドウ移動\n・Shift+矢印: サイズ変更\n・Esc: 移動モード終了");
                        e.Handled = true;
                        _lastShiftPressTime = DateTime.MinValue;
                        return;
                    }
                }
                _lastShiftPressTime = now;
            }

            if (DataContext is GitCommandViewModel vm)
            {
                if (vm.IsHistoryMode)
                {
                    if (e.Key == Key.Escape)
                    {
                        vm.IsHistoryMode = false;
                        Dispatcher.BeginInvoke(() => CommandInputBox.Focus(), System.Windows.Threading.DispatcherPriority.Input);
                        e.Handled = true;
                        return;
                    }
                }

                if (vm.IsFrontMode)
                {
                    if (e.Key == Key.Escape)
                    {
                        vm.IsFrontMode = false;
                        vm.ConsoleOutput = "[最前面固定モード終了]\n\n" + vm.ConsoleOutput;
                        Dispatcher.BeginInvoke(() => CommandInputBox.Focus(), System.Windows.Threading.DispatcherPriority.Input);
                        e.Handled = true;
                        return;
                    }
                }

                if (vm.IsMoveMode)
                {
                    if (e.Key == Key.Escape)
                    {
                        vm.IsMoveMode = false;
                        vm.ConsoleOutput = "[ウィンドウ移動モード終了]\n\n" + vm.ConsoleOutput;
                        Dispatcher.BeginInvoke(() => CommandInputBox.Focus(), System.Windows.Threading.DispatcherPriority.Input);
                        e.Handled = true;
                        return;
                    }

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

            if (e.Key == Key.Escape)
            {
                this.Close();
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
                    Dispatcher.BeginInvoke(() => CommandInputBox.CaretIndex = CommandInputBox.Text.Length);
                    e.Handled = true;
                }
                else if (e.Key == Key.Down)
                {
                    vm.NavigateHistory(up: false);
                    Dispatcher.BeginInvoke(() => CommandInputBox.CaretIndex = CommandInputBox.Text.Length);
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
