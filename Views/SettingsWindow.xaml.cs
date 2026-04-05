using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using HotKeyCommandApp.ViewModels;

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
                    _viewModel.IsCapturingHotkey = false;
                    _viewModel.EditingGlobalHotkey = _viewModel.GlobalHotkey;
                    e.Handled = true;
                    return;
                }

                // Hotkey capturing logic is handled by PaletteWindow's PreviewKeyDown 
                // but since this is modal, we need to handle it here too or move it to ViewModel.
                // For simplicity, let's handle the capture here.
                
                Key key = (e.Key == Key.ImeProcessed) ? e.ImeProcessedKey : e.Key;
                if (key == Key.System) key = e.SystemKey;

                // Modifiers only
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

                parts.Add(key.ToString());

                _viewModel.EditingGlobalHotkey = string.Join("+", parts);
                _viewModel.IsCapturingHotkey = false;
                e.Handled = true;
                return;
            }

            if (e.Key == Key.Enter)
            {
                if (FocusManager.GetFocusedElement(this) is Button) return; // Let button handle it
                SaveAndClose();
                e.Handled = true;
            }
            else if (e.Key == Key.Escape)
            {
                CancelAndClose();
                e.Handled = true;
            }
        }

        private void HotkeyCaptureButton_Click(object sender, RoutedEventArgs e)
        {
            _viewModel.IsCapturingHotkey = true;
            _viewModel.EditingGlobalHotkey = "キーを押してください...";
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
    }
}
