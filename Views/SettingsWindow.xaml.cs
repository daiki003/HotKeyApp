using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using HotKeyCommandApp.ViewModels;
using HotKeyCommandApp.Services;
using System.Windows.Interop;

namespace HotKeyCommandApp.Views
{
    public partial class SettingsWindow : Window
    {
        private MainViewModel _viewModel;

        public SettingsWindow(MainViewModel viewModel)
        {
            InitializeComponent();
            _viewModel = viewModel;
            this.DataContext = _viewModel;

            this.Loaded += (s, e) =>
            {
                FontSizeTextBox.Focus();
                FontSizeTextBox.SelectAll();
            };
        }

        private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (_viewModel.IsCapturingHotkey)
            {
                if (e.Key == Key.Escape)
                {
                    CancelCapture();
                    e.Handled = true;
                    return;
                }

                Key key = (e.Key == Key.ImeProcessed) ? e.ImeProcessedKey : e.Key;
                if (key == Key.System) key = e.SystemKey;

                // 修飾キーのみの場合
                if (key == Key.LeftCtrl || key == Key.RightCtrl ||
                    key == Key.LeftAlt || key == Key.RightAlt ||
                    key == Key.LeftShift || key == Key.RightShift ||
                    key == Key.LWin || key == Key.RWin)
                {
                    e.Handled = true;
                    return;
                }

                var parts = new System.Collections.Generic.List<string>();
                bool winPressed = Keyboard.Modifiers.HasFlag(ModifierKeys.Windows) ||
                                 Keyboard.IsKeyDown(Key.LWin) || Keyboard.IsKeyDown(Key.RWin);

                if (winPressed) parts.Add("Win");
                if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control)) parts.Add("Ctrl");
                if (Keyboard.Modifiers.HasFlag(ModifierKeys.Alt)) parts.Add("Alt");
                if (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift)) parts.Add("Shift");

                string keyStr = key.ToString();
                
                // Key.OemPlus, Key.OemCommaなどをわかりやすく変換
                if (key == Key.OemPlus) keyStr = "Plus";
                else if (key == Key.OemComma) keyStr = "Comma";
                else if (key == Key.OemMinus) keyStr = "Minus";
                else if (key == Key.OemPeriod) keyStr = "Period";

                parts.Add(keyStr);

                string finalHotkey = string.Join("+", parts);

                switch (_viewModel.CaptureTarget)
                {
                    case "Global":
                        _viewModel.EditingGlobalHotkey = finalHotkey;
                        break;
                    case "Settings":
                        _viewModel.EditingSettingsShortcut = finalHotkey;
                        break;
                    case "Create":
                        _viewModel.EditingCreateButtonShortcut = finalHotkey;
                        break;
                }

                _viewModel.IsCapturingHotkey = false;
                _viewModel.CaptureTarget = string.Empty;
                e.Handled = true;
                return;
            }

            if (e.Key == Key.Enter)
            {
                if (FocusManager.GetFocusedElement(this) is Button) return; // ボタン自身に処理させる
                SaveAndClose();
                e.Handled = true;
            }
            else if (e.Key == Key.Escape)
            {
                CancelAndClose();
                e.Handled = true;
            }
            else if (e.Key == Key.PageDown)
            {
                if (SettingsTabControl.SelectedIndex < SettingsTabControl.Items.Count - 1)
                {
                    SettingsTabControl.SelectedIndex++;
                }
                else
                {
                    SettingsTabControl.SelectedIndex = 0; // ループさせる場合
                }
                e.Handled = true;
            }
            else if (e.Key == Key.PageUp)
            {
                if (SettingsTabControl.SelectedIndex > 0)
                {
                    SettingsTabControl.SelectedIndex--;
                }
                else
                {
                    SettingsTabControl.SelectedIndex = SettingsTabControl.Items.Count - 1; // ループさせる場合
                }
                e.Handled = true;
            }
        }

        private void CancelCapture()
        {
            _viewModel.IsCapturingHotkey = false;
            switch (_viewModel.CaptureTarget)
            {
                case "Global":
                    _viewModel.EditingGlobalHotkey = _viewModel.GlobalHotkey;
                    break;
                case "Settings":
                    _viewModel.EditingSettingsShortcut = _viewModel.SettingsShortcut;
                    break;
                case "Create":
                    _viewModel.EditingCreateButtonShortcut = _viewModel.CreateButtonShortcut;
                    break;
            }
            _viewModel.CaptureTarget = string.Empty;
        }

        private void GlobalHotkeyButton_Click(object sender, RoutedEventArgs e)
        {
            _viewModel.IsCapturingHotkey = true;
            _viewModel.CaptureTarget = "Global";
            _viewModel.EditingGlobalHotkey = "キーを押してください...";
        }

        private void SettingsHotkeyButton_Click(object sender, RoutedEventArgs e)
        {
            _viewModel.IsCapturingHotkey = true;
            _viewModel.CaptureTarget = "Settings";
            _viewModel.EditingSettingsShortcut = "キーを押してください...";
        }

        private void CreateButtonHotkeyButton_Click(object sender, RoutedEventArgs e)
        {
            _viewModel.IsCapturingHotkey = true;
            _viewModel.CaptureTarget = "Create";
            _viewModel.EditingCreateButtonShortcut = "キーを押してください...";
        }

        private void SaveButton_Click(object sender, RoutedEventArgs e)
        {
            SaveAndClose();
        }

        private void SaveAndClose()
        {
            if (_viewModel.SaveSettingsCommand.CanExecute(null))
            {
                _viewModel.SaveSettingsCommand.Execute(null);
            }
            this.DialogResult = true;
            this.Close();
        }

        private void CancelAndClose()
        {
            this.DialogResult = false;
            this.Close();
        }

        private void TextBox_GotFocus(object sender, RoutedEventArgs e)
        {
            if (sender is TextBox textBox)
            {
                textBox.CaretIndex = textBox.Text.Length;
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    if (textBox.IsFocused)
                        textBox.CaretIndex = textBox.Text.Length;
                }), System.Windows.Threading.DispatcherPriority.Background);
            }
        }

        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);
            WindowHelper.DisableSystemMenu(this);
            WindowHelper.EnableWindowMoveShortcut(this, () => !_viewModel.IsCapturingHotkey);
            WindowHelper.EnableWindowDragMove(this);
        }
    }
}
