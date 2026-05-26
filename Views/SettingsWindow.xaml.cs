using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using HotKeyCommandApp.ViewModels;
using HotKeyCommandApp.Services;
using System.Windows.Interop;
using HotKeyCommandApp.Models;

namespace HotKeyCommandApp.Views
{
    public partial class SettingsWindow : Window
    {
        private MainViewModel _viewModel;

        private enum SettingsPage
        {
            List,
            Size,
            Shortcuts,
            Others,
            Constants,
            SelectTemplates
        }

        private SettingsPage _currentPage = SettingsPage.List;

        public SettingsWindow(MainViewModel viewModel)
        {
            InitializeComponent();
            _viewModel = viewModel;
            this.DataContext = _viewModel;
            ConstantsPairEditor.AddItemCommand = new RelayCommand<object>(_ => _viewModel.EditingConstants.Add(new ConstantEntry { Name = "NEW_CONSTANT", Value = string.Empty }));
            SelectTemplatesPairEditor.AddItemCommand = new RelayCommand<object>(_ => _viewModel.EditingSelectTemplates.Add(new SelectTemplate { Name = "NEW_TEMPLATE", OptionsString = string.Empty }));

            this.Loaded += (s, e) =>
            {
                SwitchPage(SettingsPage.List);
            };
        }

        private void SwitchPage(SettingsPage page)
        {
            _currentPage = page;

            // 全パネル非表示
            CategoryListScrollViewer.Visibility = Visibility.Collapsed;
            SizePanel.Visibility = Visibility.Collapsed;
            ShortcutsPanel.Visibility = Visibility.Collapsed;
            OthersPanel.Visibility = Visibility.Collapsed;
            ConstantsPanel.Visibility = Visibility.Collapsed;
            SelectTemplatesPanel.Visibility = Visibility.Collapsed;
            BackButton.Visibility = Visibility.Collapsed;

            if (page == SettingsPage.List)
            {
                TitleTextBlock.Text = "設定";
                FooterHintTextBlock.Text = "矢印キーで選択 / Escで閉じる";
                CategoryListScrollViewer.Visibility = Visibility.Visible;

                Dispatcher.BeginInvoke(new Action(() =>
                {
                    SizeCategoryButton.Focus();
                }), System.Windows.Threading.DispatcherPriority.Loaded);
            }
            else
            {
                BackButton.Visibility = Visibility.Visible;
                FooterHintTextBlock.Text = "Alt+← または Esc で一覧に戻る";

                if (page == SettingsPage.Size)
                {
                    TitleTextBlock.Text = "サイズ調整";
                    SizePanel.Visibility = Visibility.Visible;
                    Dispatcher.BeginInvoke(new Action(() => { FontSizeTextBox.Focus(); FontSizeTextBox.SelectAll(); }), System.Windows.Threading.DispatcherPriority.Loaded);
                }
                else if (page == SettingsPage.Shortcuts)
                {
                    TitleTextBlock.Text = "ショートカット";
                    ShortcutsPanel.Visibility = Visibility.Visible;
                    Dispatcher.BeginInvoke(new Action(() => { GlobalHotkeyButton.Focus(); }), System.Windows.Threading.DispatcherPriority.Loaded);
                }
                else if (page == SettingsPage.Others)
                {
                    TitleTextBlock.Text = "その他";
                    OthersPanel.Visibility = Visibility.Visible;
                    Dispatcher.BeginInvoke(new Action(() => { MovementSpeedTextBox.Focus(); MovementSpeedTextBox.SelectAll(); }), System.Windows.Threading.DispatcherPriority.Loaded);
                }
                else if (page == SettingsPage.Constants)
                {
                    TitleTextBlock.Text = "定数定義";
                    ConstantsPanel.Visibility = Visibility.Visible;
                    Dispatcher.BeginInvoke(new Action(() =>
                    {
                        if (!ConstantsPairEditor.FocusFirstTextBox())
                        {
                            ConstantsPairEditor.FocusAddButton();
                        }
                    }), System.Windows.Threading.DispatcherPriority.Loaded);
                }
                else if (page == SettingsPage.SelectTemplates)
                {
                    TitleTextBlock.Text = "選択式テンプレート";
                    SelectTemplatesPanel.Visibility = Visibility.Visible;
                    Dispatcher.BeginInvoke(new Action(() =>
                    {
                        if (!SelectTemplatesPairEditor.FocusFirstTextBox())
                        {
                            SelectTemplatesPairEditor.FocusAddButton();
                        }
                    }), System.Windows.Threading.DispatcherPriority.Loaded);
                }
            }
        }

        private void CategoryButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is string tag)
            {
                if (tag == "Size") SwitchPage(SettingsPage.Size);
                else if (tag == "Shortcuts") SwitchPage(SettingsPage.Shortcuts);
                else if (tag == "Others") SwitchPage(SettingsPage.Others);
                else if (tag == "Constants") SwitchPage(SettingsPage.Constants);
                else if (tag == "SelectTemplates") SwitchPage(SettingsPage.SelectTemplates);
            }
        }

        private void OpenSettingsJsonButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                SaveSettings();
                string path = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "settings.json");
                if (System.IO.File.Exists(path))
                {
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                    {
                        FileName = path,
                        UseShellExecute = true
                    });
                }
                else
                {
                    MessageBox.Show("settings.json がまだ生成されていません。", "通知", MessageBoxButton.OK, MessageBoxImage.Information);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"ファイルを開けませんでした: {ex.Message}", "エラー", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void BackButton_Click(object sender, RoutedEventArgs e)
        {
            SaveSettings();
            SwitchPage(SettingsPage.List);
        }

        private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            // AltキーなどSystemキーと一緒に押されたキーはe.SystemKeyに格納されるため解決する
            Key actualKey = (e.Key == Key.ImeProcessed) ? e.ImeProcessedKey : e.Key;
            if (actualKey == Key.System) actualKey = e.SystemKey;

            if (_viewModel.IsCapturingHotkey)
            {
                if (actualKey == Key.Escape)
                {
                    CancelCapture();
                    e.Handled = true;
                    return;
                }

                // 修飾キーのみの場合
                if (actualKey == Key.LeftCtrl || actualKey == Key.RightCtrl ||
                    actualKey == Key.LeftAlt || actualKey == Key.RightAlt ||
                    actualKey == Key.LeftShift || actualKey == Key.RightShift ||
                    actualKey == Key.LWin || actualKey == Key.RWin)
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

                string keyStr = actualKey.ToString();
                
                if (actualKey == Key.OemPlus) keyStr = "Plus";
                else if (actualKey == Key.OemComma) keyStr = "Comma";
                else if (actualKey == Key.OemMinus) keyStr = "Minus";
                else if (actualKey == Key.OemPeriod) keyStr = "Period";

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

            // Alt + ← で一覧に戻る (VsCodeスタイル)
            if (Keyboard.Modifiers.HasFlag(ModifierKeys.Alt) && actualKey == Key.Left)
            {
                if (_currentPage != SettingsPage.List)
                {
                    SaveSettings();
                    SwitchPage(SettingsPage.List);
                    e.Handled = true;
                    return;
                }
            }

            if (actualKey == Key.Up)
            {
                if (_currentPage == SettingsPage.Constants)
                {
                    if (ConstantsPairEditor.IsAddButtonFocused)
                    {
                        ConstantsPairEditor.FocusLastRow(focusSecondColumn: false);
                        e.Handled = true;
                        return;
                    }

                    if (Keyboard.FocusedElement == SaveButton)
                    {
                        ConstantsPairEditor.FocusLastRow(focusSecondColumn: true);
                        e.Handled = true;
                        return;
                    }
                }
                else if (_currentPage == SettingsPage.SelectTemplates)
                {
                    if (SelectTemplatesPairEditor.IsAddButtonFocused)
                    {
                        SelectTemplatesPairEditor.FocusLastRow(focusSecondColumn: false);
                        e.Handled = true;
                        return;
                    }

                    if (Keyboard.FocusedElement == SaveButton)
                    {
                        SelectTemplatesPairEditor.FocusLastRow(focusSecondColumn: true);
                        e.Handled = true;
                        return;
                    }
                }
            }

            if (actualKey == Key.Enter)
            {
                if (FocusManager.GetFocusedElement(this) is Button) return; // ボタン自身に処理させる

                if (_currentPage == SettingsPage.List)
                {
                    // 一覧画面でのEnter時、ボタン以外にフォーカスがあっても何もしない
                    return; 
                }
                else
                {
                    // 詳細画面でのEnterは設定を保存して一覧に戻る
                    SaveSettings();
                    SwitchPage(SettingsPage.List);
                    e.Handled = true;
                }
            }
            else if (actualKey == Key.Escape)
            {
                if (_currentPage == SettingsPage.List)
                {
                    CancelAndClose();
                }
                else
                {
                    SwitchPage(SettingsPage.List);
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

        private void SaveSettings()
        {
            if (_viewModel.SaveSettingsCommand.CanExecute(null))
            {
                _viewModel.SaveSettingsCommand.Execute(null);
            }
        }

        private void SaveAndClose()
        {
            SaveSettings();
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
