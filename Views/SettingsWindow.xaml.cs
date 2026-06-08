using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using HotKeyCommandApp.ViewModels;
using HotKeyCommandApp.ViewModels.Settings;
using HotKeyCommandApp.Views.Controls;
using HotKeyCommandApp.Services;
using System.Windows.Interop;
using HotKeyCommandApp.Models;

namespace HotKeyCommandApp.Views
{
    public partial class SettingsWindow : Window
    {
        private const string ShortcutsSectionId = "Shortcuts";
        private const string OthersSectionId = "Others";
        private const string ConstantsSectionId = "Constants";
        private const string SelectTemplatesSectionId = "SelectTemplates";

        private MainViewModel _viewModel;
        private readonly SettingsTabShellController _tabShell = new();
        private readonly SizeSettingsSectionViewModel _sizeSectionViewModel;
        private readonly ShortcutSettingsSectionViewModel _shortcutsSectionViewModel;
        private readonly OthersSettingsSectionViewModel _othersSectionViewModel;
        private readonly ConstantsSettingsSectionViewModel _constantsSectionViewModel;
        private readonly SelectTemplatesSettingsSectionViewModel _selectTemplatesSectionViewModel;

        public SettingsWindow(MainViewModel viewModel)
        {
            InitializeComponent();
            _viewModel = viewModel;
            this.DataContext = _viewModel;
            SettingsShell.HeaderActionCommand = new RelayCommand<object>(_ => OpenSettingsJson());
            _sizeSectionViewModel = new SizeSettingsSectionViewModel(_viewModel);
            _shortcutsSectionViewModel = new ShortcutSettingsSectionViewModel(_viewModel);
            _othersSectionViewModel = new OthersSettingsSectionViewModel(_viewModel);
            _constantsSectionViewModel = new ConstantsSettingsSectionViewModel(_viewModel);
            _selectTemplatesSectionViewModel = new SelectTemplatesSettingsSectionViewModel(_viewModel);
            SizePanel.DataContext = _sizeSectionViewModel;
            ShortcutsPanel.DataContext = _shortcutsSectionViewModel;
            OthersPanel.DataContext = _othersSectionViewModel;
            ConstantsPanel.DataContext = _constantsSectionViewModel;
            SelectTemplatesPanel.DataContext = _selectTemplatesSectionViewModel;
            InitializeSections();
            _tabShell.ResetAll();
            ConstantsPanel.Editor.AddItemCommand = _constantsSectionViewModel.AddItemCommand;
            SelectTemplatesPanel.Editor.AddItemCommand = _selectTemplatesSectionViewModel.AddItemCommand;
            ShortcutsPanel.GlobalHotkeyRequested += (_, _) => BeginGlobalHotkeyCapture();
            ShortcutsPanel.SettingsHotkeyRequested += (_, _) => BeginSettingsHotkeyCapture();
            ShortcutsPanel.CreateHotkeyRequested += (_, _) => BeginCreateHotkeyCapture();
            MainTabControl.SelectionChanged += MainTabControl_SelectionChanged;

            this.Loaded += (s, e) =>
            {
                SelectSectionByIndex(0);
            };
        }

        private void InitializeSections()
        {
            _tabShell.AddSection(_sizeSectionViewModel, () =>
            {
                MainTabControl.SelectedIndex = 0;
                Dispatcher.BeginInvoke(() => FocusFirstRecord(SizePanel), System.Windows.Threading.DispatcherPriority.Loaded);
            });

            _tabShell.AddSection(_shortcutsSectionViewModel, () =>
            {
                MainTabControl.SelectedIndex = 1;
                Dispatcher.BeginInvoke(() => FocusFirstRecord(ShortcutsPanel), System.Windows.Threading.DispatcherPriority.Loaded);
            });

            _tabShell.AddSection(_constantsSectionViewModel, () =>
            {
                MainTabControl.SelectedIndex = 2;
                ConstantsPanel.Editor.ResetToRoot();
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    if (!ConstantsPanel.Editor.FocusFirstRowButton())
                    {
                        ConstantsPanel.Editor.FocusAddButton();
                    }
                }), System.Windows.Threading.DispatcherPriority.Loaded);
            });

            _tabShell.AddSection(_selectTemplatesSectionViewModel, () =>
            {
                MainTabControl.SelectedIndex = 3;
                SelectTemplatesPanel.Editor.ResetToRoot();
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    if (!SelectTemplatesPanel.Editor.FocusFirstRowButton())
                    {
                        SelectTemplatesPanel.Editor.FocusAddButton();
                    }
                }), System.Windows.Threading.DispatcherPriority.Loaded);
            });

            _tabShell.AddSection(_othersSectionViewModel, () =>
            {
                MainTabControl.SelectedIndex = 4;
                Dispatcher.BeginInvoke(() => FocusFirstRecord(OthersPanel), System.Windows.Threading.DispatcherPriority.Loaded);
            });
        }

        private void MainTabControl_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (e.Source != MainTabControl)
            {
                return;
            }

            SelectSectionByIndex(MainTabControl.SelectedIndex);
        }

        private void SelectSectionByIndex(int index)
        {
            if (index < 0 || index >= _tabShell.Sections.Count)
            {
                return;
            }

            if (_tabShell.SelectedIndex == index)
            {
                return;
            }

            _tabShell.SelectByIndex(index);
        }

        private void OpenSettingsJson()
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

        private void OpenSettingsJsonButton_Click(object sender, RoutedEventArgs e)
        {
            OpenSettingsJson();
        }

        private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            // AltキーなどSystemキーと一緒に押されたキーはe.SystemKeyに格納されるため解決する
            Key actualKey = (e.Key == Key.ImeProcessed) ? e.ImeProcessedKey : e.Key;
            if (actualKey == Key.System) actualKey = e.SystemKey;
            bool isInteractiveFocused = Keyboard.FocusedElement is System.Windows.Controls.Primitives.TextBoxBase ||
                                        Keyboard.FocusedElement is System.Windows.Controls.Primitives.ButtonBase ||
                                        Keyboard.FocusedElement is ListBoxItem;

            if (_shortcutsSectionViewModel.IsCapturingHotkey)
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

                switch (_shortcutsSectionViewModel.CaptureTarget)
                {
                    case "Global":
                    case "Settings":
                    case "Create":
                        break;
                }

                _shortcutsSectionViewModel.CompleteCapture(finalHotkey);
                SyncCaptureStateToMainViewModel();
                e.Handled = true;
                return;
            }

            if (actualKey == Key.PageUp || actualKey == Key.PageDown)
            {
                MainTabControl.MoveSelection(actualKey == Key.PageDown ? 1 : -1);
                e.Handled = true;
                return;
            }

            ISettingsRecordSectionControl? activeSection = GetActiveRecordSectionControl();
            if (activeSection?.TryHandleKeyboardShortcut(e) == true)
            {
                e.Handled = true;
                return;
            }

            if (SettingsDialogKeyboardNavigationHelper.TryFocusFirstRecordOnDirectionalKey(
                    e,
                    isInteractiveFocused,
                    activeSection != null,
                    () => FocusFirstRecord(activeSection!)))
            {
                return;
            }

            if (Keyboard.Modifiers == ModifierKeys.Control && actualKey == Key.End)
            {
                SaveButton.Focus();
                e.Handled = true;
                return;
            }

            if (Keyboard.Modifiers == ModifierKeys.Control && actualKey == Key.Enter)
            {
                SaveAndClose();
                e.Handled = true;
                return;
            }

            if (actualKey == Key.Up)
            {
                if (Keyboard.FocusedElement == SaveButton && activeSection != null)
                {
                    activeSection.FocusFirstRecord();
                    e.Handled = true;
                    return;
                }
            }

            if (actualKey == Key.Escape)
            {
                if (IsEmbeddedEditorTextInputFocused())
                {
                    return;
                }

                CancelAndClose();
                e.Handled = true;
                return;
            }

            if (Keyboard.FocusedElement is System.Windows.Controls.Primitives.TextBoxBase)
            {
                return;
            }
        }

        private void CancelCapture()
        {
            _shortcutsSectionViewModel.CancelCapture();
            SyncCaptureStateToMainViewModel();
        }

        private void BeginGlobalHotkeyCapture()
        {
            _shortcutsSectionViewModel.BeginCapture("Global");
            SyncCaptureStateToMainViewModel();
        }

        private void BeginSettingsHotkeyCapture()
        {
            _shortcutsSectionViewModel.BeginCapture("Settings");
            SyncCaptureStateToMainViewModel();
        }

        private void BeginCreateHotkeyCapture()
        {
            _shortcutsSectionViewModel.BeginCapture("Create");
            SyncCaptureStateToMainViewModel();
        }

        private void SaveButton_Click(object sender, RoutedEventArgs e)
        {
            SaveAndClose();
        }

        private void Close_Click(object sender, RoutedEventArgs e)
        {
            CancelAndClose();
        }

        private void SaveSettings()
        {
            _tabShell.SaveAll();
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

        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);
            WindowHelper.DisableSystemMenu(this);
            WindowHelper.EnableWindowMoveShortcut(this, () => !_shortcutsSectionViewModel.IsCapturingHotkey);
            WindowHelper.EnableWindowDragMove(this);
        }

        private void SyncCaptureStateToMainViewModel()
        {
            _viewModel.IsCapturingHotkey = _shortcutsSectionViewModel.IsCapturingHotkey;
            _viewModel.CaptureTarget = _shortcutsSectionViewModel.CaptureTarget;
        }

        private static void FocusFirstRecord(ISettingsRecordSectionControl sectionControl)
        {
            sectionControl.FocusFirstRecord();
        }

        private ISettingsRecordSectionControl? GetActiveRecordSectionControl()
        {
            return _tabShell.SelectedSection?.Id switch
            {
                "Size" => SizePanel,
                ShortcutsSectionId => ShortcutsPanel,
                ConstantsSectionId => ConstantsPanel,
                SelectTemplatesSectionId => SelectTemplatesPanel,
                OthersSectionId => OthersPanel,
                _ => null
            };
        }

        private bool IsEmbeddedEditorTextInputFocused()
        {
            if (Keyboard.FocusedElement is not System.Windows.Controls.Primitives.TextBoxBase ||
                Keyboard.FocusedElement is not DependencyObject focusedElement)
            {
                return false;
            }

            return IsDescendantOf(focusedElement, SizePanel) ||
                   IsDescendantOf(focusedElement, OthersPanel) ||
                   IsDescendantOf(focusedElement, ConstantsPanel.Editor) ||
                   IsDescendantOf(focusedElement, SelectTemplatesPanel.Editor);
        }

        private static bool IsDescendantOf(DependencyObject child, DependencyObject ancestor)
        {
            DependencyObject? current = child;
            while (current != null)
            {
                if (ReferenceEquals(current, ancestor))
                {
                    return true;
                }

                current = System.Windows.Media.VisualTreeHelper.GetParent(current);
            }

            return false;
        }

    }
}
