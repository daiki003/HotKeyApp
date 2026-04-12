using System;
using System.Windows;
using System.Windows.Input;
using HotKeyCommandApp.ViewModels;
using HotKeyCommandApp.Models;

namespace HotKeyCommandApp.Views
{
    public partial class WindowSwitcherWindow : Window
    {
        private readonly bool _isPeekMode;
        private bool _isClosing = false;

        public CommandEntry? ActiveCommand { get; }

        public WindowSwitcherWindow(MainViewModel viewModel, bool isPeekMode, CommandEntry? activeCommand)
        {
            InitializeComponent();
            this.DataContext = viewModel;
            _isPeekMode = isPeekMode;
            this.ActiveCommand = activeCommand;

            this.Closing += (s, e) => _isClosing = true;

            this.Loaded += (s, e) => {
                this.Activate();
                this.Focus();
                SwitcherListBox.Focus();
                
                // アイテムが選択されていない場合は初期アイテムを選択（Alt+Tab挙動）
                if (viewModel.SelectedWindowItem == null && viewModel.WindowSwitcherItems.Count > 0)
                {
                    if (viewModel.WindowSwitcherItems.Count >= 2)
                    {
                        viewModel.SelectedWindowItem = viewModel.WindowSwitcherItems[1];
                    }
                    else
                    {
                        viewModel.SelectedWindowItem = viewModel.WindowSwitcherItems[0];
                    }
                }
            };

            // ウィンドウ外クリックで閉じる
            this.Deactivated += (s, e) => {
                if (!_isClosing && !this.IsOwnedByAnyDialog())
                {
                    SafeClose();
                }
            };
        }

        private void SafeClose()
        {
            if (_isClosing) return;
            try
            {
                this.Close();
            }
            catch
            {
                // すでに閉じているなどのエラーは無視
            }
        }

        private bool IsOwnedByAnyDialog()
        {
            // 他のダイアログが開いているかなどのチェック（必要に応じて）
            return false;
        }

        private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (DataContext is MainViewModel vm)
            {
                if (e.Key == Key.Escape)
                {
                    SafeClose();
                    e.Handled = true;
                    return;
                }

                if (e.Key == Key.Enter)
                {
                    try
                    {
                        if (vm.SelectedWindowItem != null)
                        {
                            vm.ExecuteCommand.Execute(vm.SelectedWindowItem);
                        }
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"Error executing window switch: {ex.Message}");
                    }
                    SafeClose();
                    e.Handled = true;
                    return;
                }

                // 十字キーでの移動を WrapPanel に合わせて直感的にする
                // ListBox の標準挙動に任せる（WrapPanel の場合は自動で上下左右が判定される）
            }
        }

        private void Window_KeyUp(object sender, KeyEventArgs e)
        {
            Key key = (e.Key == Key.ImeProcessed) ? e.ImeProcessedKey : e.Key;
            if (key == Key.System) key = e.SystemKey;

            // Altキーが離された場合の確定処理（Peekモード時）
            if (_isPeekMode && (key == Key.LeftAlt || key == Key.RightAlt))
            {
                try
                {
                    if (DataContext is MainViewModel vm)
                    {
                        if (vm.SelectedWindowItem != null)
                        {
                            vm.ExecuteCommand.Execute(vm.SelectedWindowItem);
                        }
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Error executing window switch on Alt release: {ex.Message}");
                }
                SafeClose();
                e.Handled = true;
            }
        }
    }
}
