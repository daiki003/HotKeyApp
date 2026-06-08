using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using HotKeyCommandApp.Models;
using HotKeyCommandApp.Services;
using HotKeyCommandApp.ViewModels;

namespace HotKeyCommandApp.Views
{
    public partial class PresetSelectionWindow : Window
    {
        private readonly PresetSelectionViewModel _viewModel;
        private readonly PresetCreationWizard _presetCreationWizard;

        public PresetSelectionWindow(PresetSelectionViewModel viewModel)
        {
            InitializeComponent();
            _viewModel = viewModel;
            _presetCreationWizard = new PresetCreationWizard(_viewModel.AppSettings);
            DataContext = _viewModel;

            _viewModel.RequestClose += accepted =>
            {
                DialogResult = accepted;
                Close();
            };
            _viewModel.RequestPresetCreation += OpenPresetCreationWizard;

            Loaded += (_, _) =>
            {
                FocusPresetList();
            };
        }

        private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape)
            {
                _viewModel.CancelCommand.Execute(null);
                e.Handled = true;
                return;
            }

            if (e.Key == Key.PageUp || e.Key == Key.PageDown)
            {
                _viewModel.MoveCategory(e.Key == Key.PageDown ? 1 : -1);
                CategoryTabControl.SelectedItem = _viewModel.SelectedCategory;
                FocusPresetList();
                e.Handled = true;
                return;
            }

            if (e.Key == Key.Up)
            {
                _viewModel.MovePreset(-1);
                PresetListBox.ScrollIntoView(_viewModel.SelectedPreset);
                e.Handled = true;
                return;
            }

            if (e.Key == Key.Down)
            {
                _viewModel.MovePreset(1);
                PresetListBox.ScrollIntoView(_viewModel.SelectedPreset);
                e.Handled = true;
                return;
            }

            if (e.Key == Key.Enter)
            {
                if (_viewModel.SelectedListItem?.Value == "ADD_BUTTON")
                {
                    OpenPresetCreationWizard();
                }
                else
                {
                    _viewModel.ConfirmCommand.Execute(null);
                }
                e.Handled = true;
                return;
            }

            if (MatchesCreateShortcut(e))
            {
                OpenPresetCreationWizard();
                e.Handled = true;
                return;
            }
        }

        private void FocusPresetList()
        {
            Dispatcher.BeginInvoke(new Action(() =>
            {
                PresetListBox.Focus();
                if (_viewModel.SelectedListItem != null &&
                    PresetListBox.ItemContainerGenerator.ContainerFromItem(_viewModel.SelectedListItem) is ListBoxItem item)
                {
                    item.Focus();
                }
            }), System.Windows.Threading.DispatcherPriority.Loaded);
        }

        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);
            WindowHelper.DisableSystemMenu(this);
            WindowHelper.EnableWindowMoveShortcut(this);
            WindowHelper.EnableWindowDragMove(this);
        }

        private void OpenPresetCreationWizard()
        {
            CommandPreset? basePreset = _viewModel.SelectedPreset;
            CommandEntry? createdCommand = _presetCreationWizard.ShowDialog(this, basePreset);
            if (createdCommand == null)
            {
                FocusPresetList();
                return;
            }

            _viewModel.SaveCreatedPreset(createdCommand, basePreset);
            FocusPresetList();
        }

        private bool MatchesCreateShortcut(KeyEventArgs e)
        {
            Key key = e.Key == Key.ImeProcessed ? e.ImeProcessedKey : e.Key;
            if (key == Key.System)
            {
                key = e.SystemKey;
            }

            string keyStr = key switch
            {
                Key.OemPlus => "Plus",
                Key.OemComma => "Comma",
                Key.OemMinus => "Minus",
                Key.OemPeriod => "Period",
                _ => key.ToString()
            };

            var hotkeyParts = new System.Collections.Generic.List<string>();
            bool winPressed = Keyboard.Modifiers.HasFlag(ModifierKeys.Windows) ||
                              Keyboard.IsKeyDown(Key.LWin) || Keyboard.IsKeyDown(Key.RWin);

            if (winPressed) hotkeyParts.Add("Win");
            if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control)) hotkeyParts.Add("Ctrl");
            if (Keyboard.Modifiers.HasFlag(ModifierKeys.Alt)) hotkeyParts.Add("Alt");
            if (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift)) hotkeyParts.Add("Shift");
            hotkeyParts.Add(keyStr);

            string currentHotkeyStr = string.Join("+", hotkeyParts);
            return !string.IsNullOrEmpty(_viewModel.AppSettings.CreateButtonShortcut) &&
                   currentHotkeyStr == _viewModel.AppSettings.CreateButtonShortcut;
        }
    }
}
