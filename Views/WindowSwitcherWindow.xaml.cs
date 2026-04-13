using System;
using System.Windows;
using System.Windows.Input;
using HotKeyCommandApp.ViewModels;
using HotKeyCommandApp.Models;
using HotKeyCommandApp.Services;

namespace HotKeyCommandApp.Views
{
    public partial class WindowSwitcherWindow : Window
    {
        private readonly bool _isPeekMode;
        private bool _isClosing = false;

        public CommandEntry? ActiveCommand { get; }

        public static readonly DependencyProperty IsThumbnailEnabledProperty =
            DependencyProperty.Register("IsThumbnailEnabled", typeof(bool), typeof(WindowSwitcherWindow),
                new PropertyMetadata(false));

        public bool IsThumbnailEnabled
        {
            get => (bool)GetValue(IsThumbnailEnabledProperty);
            set => SetValue(IsThumbnailEnabledProperty, value);
        }

        public WindowSwitcherWindow(MainViewModel viewModel, bool isPeekMode, CommandEntry? activeCommand)
        {
            InitializeComponent();
            this.DataContext = viewModel;
            _isPeekMode = isPeekMode;
            this.ActiveCommand = activeCommand;

            this.Closing += (s, e) => _isClosing = true;

            this.SourceInitialized += (s, e) =>
            {
                // 表示前に位置を確定させているため、ここでは配置を行わない
            };

            this.ContentRendered += (s, e) =>
            {
                // サムネイルを有効化（背景の描画更新のタイミングを待って実行）
                // CompositionTarget.Rendering は次の描画フレームの直前に発生する
                EventHandler? handler = null;
                handler = (s2, e2) =>
                {
                    System.Windows.Media.CompositionTarget.Rendering -= handler;
                    this.IsThumbnailEnabled = true;

                    this.Activate();
                    this.Focus();
                    SwitcherListBox.Focus();
                };
                System.Windows.Media.CompositionTarget.Rendering += handler;
            };

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

            // ウィンドウ外クリックで閉じる
            this.Deactivated += (s, e) =>
            {
                if (!_isClosing && !this.IsOwnedByAnyDialog())
                {
                    SafeClose();
                }
            };

            // ウィンドウ移動ショートカットを有効化
            WindowHelper.EnableWindowMoveShortcut(this);
        }

        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);
            WindowHelper.DisableSystemMenu(this);
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

                // Alt押下中の移動をサポートするために Key.System もチェックする
                Key key = (e.Key == Key.System) ? e.SystemKey : e.Key;

                if (key == Key.Left || key == Key.Right || key == Key.Up || key == Key.Down)
                {
                    int count = SwitcherListBox.Items.Count;
                    if (count > 0)
                    {
                        int currentIndex = SwitcherListBox.SelectedIndex;
                        int nextIndex = currentIndex;

                        if (key == Key.Left)
                        {
                            nextIndex = (currentIndex - 1 + count) % count;
                        }
                        else if (key == Key.Right)
                        {
                            nextIndex = (currentIndex + 1) % count;
                        }
                        else
                        {
                            // 上下移動：現在の幅から1行あたりのアイテム数（列数）を推定
                            // アイテム幅 200 + マージン 5*2 = 210
                            double itemWidth = 210;
                            int columns = Math.Max(1, (int)(SwitcherListBox.ActualWidth / itemWidth));
                            int totalRows = (count + columns - 1) / columns;
                            int currentRow = currentIndex / columns;
                            int currentColumn = currentIndex % columns;

                            int nextRow;
                            if (key == Key.Up)
                            {
                                nextRow = currentRow - 1;
                                if (nextRow < 0) nextRow = totalRows - 1; // 最上段なら最下段へループ
                            }
                            else // Key.Down
                            {
                                nextRow = currentRow + 1;
                                if (nextRow >= totalRows) nextRow = 0; // 最下段なら最上段へループ
                            }

                            nextIndex = nextRow * columns + currentColumn;

                            // 移動先の位置にアイテムがない場合（末尾の行が欠けている場合）
                            if (nextIndex >= count)
                            {
                                nextIndex = count - 1; // その行内（＝全体の末尾）に吸着
                            }
                        }

                        if (nextIndex != currentIndex)
                        {
                            SwitcherListBox.SelectedIndex = nextIndex;
                            SwitcherListBox.ScrollIntoView(SwitcherListBox.SelectedItem);
                        }
                    }
                    e.Handled = true;
                    return;
                }
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
