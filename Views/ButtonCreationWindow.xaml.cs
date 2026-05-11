using System;
using System.Windows;
using System.Windows.Input;
using System.Windows.Controls;
using HotKeyCommandApp.ViewModels;
using HotKeyCommandApp.Services;
using HotKeyCommandApp.ViewModels.ButtonCreationSteps;
using HotKeyCommandApp.Models;
using R3;
using System.Windows.Interop;

namespace HotKeyCommandApp.Views
{
    public partial class ButtonCreationWindow : Window
    {
        private readonly ButtonCreationViewModel _viewModel;


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

            _viewModel.RequestControlFocus += HandleRequestControlFocus;

            // 選択アイテムが変更されたらスクロールする
            _viewModel.SelectedItem
                .Subscribe((CommandEntry? item) =>
                {
                    if (item != null)
                    {
                        Dispatcher.BeginInvoke(new Action(() =>
                        {
                            CommandListBox.ScrollIntoView(item);
                        }), System.Windows.Threading.DispatcherPriority.Loaded);
                    }
                });
        }

        private void HandleRequestControlFocus(string controlName)
        {
            // UI生成を確実に待つため、DispatcherPriority.Background を使用
            Dispatcher.BeginInvoke(new Action(() =>
            {
                if (controlName == "InputTextBox")
                {
                    InputTextBox.Focus();
                    if (!string.IsNullOrEmpty(InputTextBox.Text))
                    {
                        InputTextBox.CaretIndex = InputTextBox.Text.Length;
                    }
                }
                else if (controlName == "CommandListBox")
                {
                    CommandListBox.Focus();
                    
                    if (_viewModel.SelectedItem.Value != null)
                    {
                        CommandListBox.ScrollIntoView(_viewModel.SelectedItem.Value);
                        
                        // アイテム自体にフォーカスを移すことで確実に操作可能にする
                        var container = CommandListBox.ItemContainerGenerator.ContainerFromItem(_viewModel.SelectedItem.Value) as ListBoxItem;
                        if (container != null)
                        {
                            Keyboard.Focus(container);
                        }
                    }
                }
            }), System.Windows.Threading.DispatcherPriority.Background);
        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            // 起動時に現在のステップに応じて適切なコントロールにフォーカスを当てる
            string targetControl = "InputTextBox";
            if (_viewModel.CurrentStepObject.Value is PresetSelectionStep || 
                _viewModel.CurrentStepObject.Value is ListSelectionCreationStep)
            {
                targetControl = "CommandListBox";
            }

            HandleRequestControlFocus(targetControl);
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

            if (e.Key == Key.PageUp)
            {
                if (_viewModel.MoveToPreviousStepCommand.CanExecute(null))
                {
                    _viewModel.MoveToPreviousStepCommand.Execute(null);
                }
                e.Handled = true;
                return;
            }

            if (e.Key == Key.PageDown)
            {
                if (_viewModel.MoveToNextStepCommand.CanExecute(null))
                {
                    _viewModel.MoveToNextStepCommand.Execute(null);
                }
                e.Handled = true;
                return;
            }

            if (e.Key == Key.F1)
            {
                if (_viewModel.CurrentStepObject.Value is OptionsCreationStep os)
                {
                    os.IsRequiresArgumentChecked.Value = !os.IsRequiresArgumentChecked.Value;
                }
                e.Handled = true;
                return;
            }

            if (e.Key == Key.F2)
            {
                if (_viewModel.CurrentStepObject.Value is ListSelectionCreationStep lss && lss.ListSource == "registered_apps" && _viewModel.SelectedItem.Value != null &&
                    _viewModel.SelectedItem.Value.Value != "NONE" && _viewModel.SelectedItem.Value.Value != "ADD_REGISTERED_APP")
                {
                    _viewModel.EditRegisteredAppCommand.Execute(_viewModel.SelectedItem.Value);
                    e.Handled = true;
                    return;
                }

                if (_viewModel.CurrentStepObject.Value is OptionsCreationStep os)
                {
                    os.IsFileSearchChecked.Value = !os.IsFileSearchChecked.Value;
                }
                e.Handled = true;
                return;
            }

            if (e.Key == Key.F3)
            {
                if (_viewModel.CurrentStepObject.Value is OptionsCreationStep os)
                {
                    os.IsBatchModeChecked.Value = !os.IsBatchModeChecked.Value;
                }
                e.Handled = true;
                return;
            }

            if (e.Key == Key.F4)
            {
                if (_viewModel.CurrentStepObject.Value is OptionsCreationStep os)
                {
                    os.UseWindowFocusLogicChecked.Value = !os.UseWindowFocusLogicChecked.Value;
                }
                e.Handled = true;
                return;
            }

            if (e.Key == Key.Delete)
            {
                if (_viewModel.CurrentStepObject.Value is ListSelectionCreationStep lss2 && lss2.ListSource == "registered_apps" && _viewModel.SelectedItem.Value != null &&
                    _viewModel.SelectedItem.Value.Value != "NONE" && _viewModel.SelectedItem.Value.Value != "ADD_REGISTERED_APP")
                {
                    var dialog = new DeleteConfirmationWindow(_viewModel.SelectedItem.Value.Name) { Owner = this };
                    if (dialog.ShowDialog() == true)
                    {
                        _viewModel.DeleteRegisteredAppCommand.Execute(_viewModel.SelectedItem.Value);
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
            
            if (e.Key == Key.Left || e.Key == Key.Right)
            {
                // テキスト入力が不要なステップ、またはリストにフォーカスがある場合に左右キーで階層移動を試みる
                bool isTextInputActive = InputTextBox.Visibility == Visibility.Visible && InputTextBox.IsFocused;
                if (!isTextInputActive || !(_viewModel.CurrentStepObject.Value?.IsTextInputVisible ?? true))
                {
                    int direction = e.Key == Key.Right ? 1 : -1;
                    if (_viewModel.CurrentStepObject.Value?.HandleHorizontalNavigation(direction) == true)
                    {
                        e.Handled = true;
                        return;
                    }
                }
            }
            
            if (e.Key == Key.Enter)
            {
                // リストが表示されている場合は、選択アイテムの確定ロジック（Execute）を優先する
                if (_viewModel.IsListBoxVisible.Value && _viewModel.SelectedItem.Value != null)
                {
                    _viewModel.ExecuteCommand.Execute(_viewModel.SelectedItem.Value);
                }
                else
                {
                    _viewModel.CommitInputCommand.Execute(null);
                }
                e.Handled = true;
                return;
            }
        }
        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);
            WindowHelper.DisableSystemMenu(this);
            WindowHelper.EnableWindowMoveShortcut(this);
            WindowHelper.EnableWindowDragMove(this);
        }
    }
}
