using System;
using System.Windows;
using System.Windows.Input;
using System.Windows.Controls;
using HotKeyCommandApp.ViewModels;
using HotKeyCommandApp.Services;
using System.Windows.Interop;

namespace HotKeyCommandApp.Views
{
    public partial class ButtonCreationWindow : Window
    {
        private readonly ButtonCreationViewModel _viewModel;

        // 一度でもステップを進めたか（最初のAlt+Leftで意図せず閉じないようにする）を追跡します
        private bool _canNavigateBack = false;

        public ButtonCreationWindow(ButtonCreationViewModel viewModel)
        {
            InitializeComponent();
            _viewModel = viewModel;
            DataContext = _viewModel;

            _viewModel.RequestClose += () =>
            {
                this.DialogResult = _viewModel.ResultCommand != null;
                this.Close();
            };

            _viewModel.RequestControlFocus += (controlName) =>
            {
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    if (controlName == "InputTextBox")
                    {
                        InputTextBox.Focus();
                        InputTextBox.CaretIndex = InputTextBox.Text.Length;
                    }
                    else if (controlName == "CommandListBox")
                    {
                        if (CommandListBox.SelectedIndex < 0 && CommandListBox.Items.Count > 0)
                            CommandListBox.SelectedIndex = 0;

                        var item = CommandListBox.ItemContainerGenerator.ContainerFromIndex(CommandListBox.SelectedIndex) as ListBoxItem;
                        if (item != null) Keyboard.Focus(item);
                        else CommandListBox.Focus();
                    }
                }), System.Windows.Threading.DispatcherPriority.Render);
            };

            // ステップが進んだら「戻る」ナビゲーションを許可する
            _viewModel.PropertyChanged += (s, e) =>
            {
                if (e.PropertyName == nameof(ButtonCreationViewModel.CurrentStep))
                {
                    _canNavigateBack = true;
                }

                // キーボードやコマンドで選択が変更された際に自動スクロールする
                if (e.PropertyName == nameof(ButtonCreationViewModel.SelectedItem))
                {
                    if (_viewModel.SelectedItem != null)
                    {
                        // ちらつきを防ぐため、次のレンダリング前に高い優先度でスクロールを実行する
                        Dispatcher.BeginInvoke(new Action(() =>
                        {
                            CommandListBox.ScrollIntoView(_viewModel.SelectedItem);
                        }), System.Windows.Threading.DispatcherPriority.Loaded);
                    }
                }
            };
        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            Dispatcher.BeginInvoke(new Action(() =>
            {
                if (InputTextBox.IsVisible)
                {
                    InputTextBox.Focus();
                    InputTextBox.SelectAll();
                }
            }), System.Windows.Threading.DispatcherPriority.Loaded);
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

        private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape)
            {
                _viewModel.CancelInputCommand.Execute(null);
                e.Handled = true;
                return;
            }

            if (e.KeyboardDevice.Modifiers == ModifierKeys.Alt)
            {
                if (e.SystemKey == Key.Left)
                {
                    if (_canNavigateBack)
                    {
                        _viewModel.NavigateBackCommand.Execute(null);
                    }
                    e.Handled = true;
                    return;
                }
                if (e.SystemKey == Key.Right)
                {
                    _viewModel.NavigateForwardCommand.Execute(null);
                    e.Handled = true;
                    return;
                }
            }

            if (e.Key == Key.F1)
            {
                _viewModel.IsRequiresArgumentChecked = !_viewModel.IsRequiresArgumentChecked;
                e.Handled = true;
                return;
            }

            if (e.Key == Key.F2)
            {
                if (_viewModel.CurrentStep == InputStep.EnteringAppPath && _viewModel.SelectedItem != null &&
                    _viewModel.SelectedItem.Value != "NONE" && _viewModel.SelectedItem.Value != "ADD_REGISTERED_APP")
                {
                    _viewModel.EditRegisteredAppCommand.Execute(_viewModel.SelectedItem);
                    e.Handled = true;
                    return;
                }

                _viewModel.IsFileSearchChecked = !_viewModel.IsFileSearchChecked;
                e.Handled = true;
                return;
            }

            if (e.Key == Key.Delete)
            {
                if (_viewModel.CurrentStep == InputStep.EnteringAppPath && _viewModel.SelectedItem != null &&
                    _viewModel.SelectedItem.Value != "NONE" && _viewModel.SelectedItem.Value != "ADD_REGISTERED_APP")
                {
                    var dialog = new DeleteConfirmationWindow(_viewModel.SelectedItem.Name) { Owner = this };
                    if (dialog.ShowDialog() == true)
                    {
                        _viewModel.DeleteRegisteredAppCommand.Execute(_viewModel.SelectedItem);
                    }
                    e.Handled = true;
                    return;
                }
            }

            if (e.Key == Key.Up || e.Key == Key.Down)
            {
                // InputTextBox または CommandListBox にフォーカスがある場合、リストの選択移動を行う
                if (InputTextBox.IsKeyboardFocusWithin || CommandListBox.IsKeyboardFocusWithin)
                {
                    if (e.Key == Key.Up)
                    {
                        _viewModel.MoveUpCommand.Execute(null);
                        e.Handled = true;
                        return;
                    }
                    if (e.Key == Key.Down)
                    {
                        _viewModel.MoveDownCommand.Execute(null);
                        e.Handled = true;
                        return;
                    }
                }
            }

            if (e.Key == Key.Enter)
            {
                // このウィンドウでは、フォーカスはほぼ常にInputTextBoxにあります。
                // CommitInputCommandが、入力されたテキストを使うか、選択されたアイテムを使うかのロジックを処理します。
                _viewModel.CommitInputCommand.Execute(null);
                e.Handled = true;
                return;
            }
        }
        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);
            WindowHelper.DisableSystemMenu(this);
            WindowHelper.EnableWindowMoveShortcut(this);
        }
    }
}
