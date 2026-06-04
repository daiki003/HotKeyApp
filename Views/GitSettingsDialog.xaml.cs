using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using HotKeyCommandApp.Models;
using HotKeyCommandApp.Services;
using HotKeyCommandApp.ViewModels;
using HotKeyCommandApp.ViewModels.Settings;
using HotKeyCommandApp.Views.Controls;

namespace HotKeyCommandApp.Views
{
    public partial class GitSettingsDialog : Window
    {
        private const string AliasesSectionId = "Aliases";
        private const string FunctionsSectionId = "Functions";
        private const string MappingsSectionId = "Mappings";

        private readonly GitSettingsManager _settingsManager;
        private readonly GitSettingsEditorSession _editorSession;
        private readonly GitSettings _workingSettings;
        private readonly SettingsTabShellController _tabShell = new();
        private readonly GitAliasSettingsSectionViewModel _aliasesSectionViewModel;
        private readonly GitFunctionsSettingsSectionViewModel _functionsSectionViewModel;
        private readonly GitRepositoryMappingsSectionViewModel _mappingsSectionViewModel;

        public IPairEditorTabHost AliasTabHost { get; }

        public GitSettingsDialog()
        {
            InitializeComponent();

            _settingsManager = new GitSettingsManager();
            _editorSession = new GitSettingsEditorSession(_settingsManager.CurrentSettings);
            _workingSettings = _editorSession.WorkingCopy;
            SettingsShell.HeaderActionCommand = new RelayCommand<object>(_ => OpenGitSettings());
            AliasTabHost = new GitAliasTabHost(_workingSettings);
            _aliasesSectionViewModel = new GitAliasSettingsSectionViewModel(AliasTabHost);
            _functionsSectionViewModel = new GitFunctionsSettingsSectionViewModel(_workingSettings);
            _mappingsSectionViewModel = new GitRepositoryMappingsSectionViewModel(_workingSettings);
            DataContext = _workingSettings;
            AliasesPairEditor.DataContext = _aliasesSectionViewModel;
            FunctionsPanel.DataContext = _functionsSectionViewModel;
            MappingsPairEditor.DataContext = _mappingsSectionViewModel;
            MappingsPairEditor.AddItemCommand = _mappingsSectionViewModel.AddItemCommand;
            MappingsPairEditor.FolderItemsSource = _mappingsSectionViewModel.RepositoryNameMappingFolders;
            InitializeSections();
            _tabShell.ResetAll();

            Loaded += OnLoaded;
            MainTabControl.SelectionChanged += MainTabControl_SelectionChanged;
            PreviewKeyDown += GitSettingsDialog_PreviewKeyDown;

        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            WindowHelper.EnableWindowDragMove(this);
            WindowHelper.EnableWindowMoveShortcut(this);
            MappingsPairEditor.ResetToRoot();
            SelectSectionByIndex(0);
        }

        private void InitializeSections()
        {
            _aliasesSectionViewModel.SelectedAction = () =>
            {
                MainTabControl.SelectedIndex = 0;
                AliasesPairEditor.ResetToRoot();
                FocusFirstAliasRow();
            };
            _tabShell.AddSection(_aliasesSectionViewModel, _aliasesSectionViewModel.SelectedAction!);

            _functionsSectionViewModel.SelectedAction = () =>
            {
                MainTabControl.SelectedIndex = 1;
                Dispatcher.BeginInvoke(() => FunctionsPanel.FocusPrimaryInput(), System.Windows.Threading.DispatcherPriority.Loaded);
            };
            _tabShell.AddSection(_functionsSectionViewModel, _functionsSectionViewModel.SelectedAction!);

            _mappingsSectionViewModel.SelectedAction = () =>
            {
                MainTabControl.SelectedIndex = 2;
                MappingsPairEditor.ResetToRoot();
                FocusFirstMappingRow();
            };
            _tabShell.AddSection(_mappingsSectionViewModel, _mappingsSectionViewModel.SelectedAction!);
        }

        private void MainTabControl_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (e.Source != MainTabControl)
            {
                return;
            }

            SelectSectionByIndex(MainTabControl.SelectedIndex);
        }

        private void GitSettingsDialog_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            bool isInteractiveFocused = Keyboard.FocusedElement is System.Windows.Controls.Primitives.TextBoxBase ||
                                        Keyboard.FocusedElement is System.Windows.Controls.Primitives.ButtonBase ||
                                        Keyboard.FocusedElement is ListBoxItem;

            if (e.Key == Key.Escape)
            {
                if (IsAliasEditorTextInputFocused())
                {
                    return;
                }

                if (IsAliasEditorButtonFocused() || !isInteractiveFocused)
                {
                    Close();
                    e.Handled = true;
                    return;
                }

                Close();
                e.Handled = true;
                return;
            }

            if (e.Key == Key.PageUp || e.Key == Key.PageDown)
            {
                _tabShell.MoveSelection(e.Key == Key.PageDown ? 1 : -1);
                e.Handled = true;
                return;
            }

            if (Keyboard.Modifiers == ModifierKeys.Control && (e.Key == Key.OemPlus || e.Key == Key.Add))
            {
                switch (_tabShell.SelectedSection?.Id)
                {
                    case AliasesSectionId:
                        AliasesPairEditor.AddNewItem();
                        break;
                    case FunctionsSectionId:
                        _functionsSectionViewModel.AddFunctionCommand.Execute(null);
                        Dispatcher.BeginInvoke(() => FunctionsPanel.FocusPrimaryInput(), System.Windows.Threading.DispatcherPriority.Loaded);
                        break;
                    case MappingsSectionId:
                        MappingsPairEditor.AddNewItem();
                        break;
                }

                e.Handled = true;
                return;
            }

            if (Keyboard.Modifiers == ModifierKeys.Control && e.Key == Key.End)
            {
                SaveAndCloseButton.Focus();
                e.Handled = true;
                return;
            }

            if (Keyboard.Modifiers == ModifierKeys.Control && e.Key == Key.Enter)
            {
                SaveAndClose_Click(null, null);
                e.Handled = true;
                return;
            }

            ISettingsRecordListNavigator? activeNavigator = GetActiveRecordNavigator();
            if (activeNavigator?.TryHandleKeyboardShortcut(e) == true)
            {
                e.Handled = true;
                return;
            }

            if (SettingsDialogKeyboardNavigationHelper.TryFocusFirstRecordOnDirectionalKey(
                    e,
                    isInteractiveFocused,
                    activeNavigator != null,
                    () => FocusEditorStart(activeNavigator!),
                    includeHorizontalKeys: true))
            {
                return;
            }

            DependencyObject? currentFocus = Keyboard.FocusedElement as DependencyObject;

            if (e.Key == Key.Right && currentFocus == SaveAndCloseButton)
            {
                CancelButton.Focus();
                e.Handled = true;
                return;
            }

            if (e.Key == Key.Left && currentFocus == CancelButton)
            {
                SaveAndCloseButton.Focus();
                e.Handled = true;
                return;
            }

            if (e.Key == Key.Up)
            {
                if ((currentFocus == SaveAndCloseButton || currentFocus == CancelButton) && activeNavigator != null)
                {
                    activeNavigator.FocusFirstRecord();
                    e.Handled = true;
                    return;
                }

                if (activeNavigator?.TryFocusHeaderFromEditor() == true)
                {
                    e.Handled = true;
                    return;
                }
            }

            if (Keyboard.FocusedElement is System.Windows.Controls.Primitives.TextBoxBase)
            {
                return;
            }

            if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control | ModifierKeys.Shift))
            {
                const double step = 10.0;
                if (e.Key == Key.Left) { Width = Math.Max(300, Width - step); e.Handled = true; }
                else if (e.Key == Key.Right) { Width = Math.Min(1600, Width + step); e.Handled = true; }
                else if (e.Key == Key.Up) { Height = Math.Max(200, Height - step); e.Handled = true; }
                else if (e.Key == Key.Down) { Height = Math.Min(1200, Height + step); e.Handled = true; }
            }
            else if (Keyboard.Modifiers == ModifierKeys.Control &&
                     (e.Key == Key.Left || e.Key == Key.Right || e.Key == Key.Up || e.Key == Key.Down))
            {
                e.Handled = true;
            }
        }

        private ISettingsRecordListNavigator? GetActiveRecordNavigator()
        {
            return _tabShell.SelectedSection?.Id switch
            {
                AliasesSectionId => AliasesPairEditor,
                MappingsSectionId => MappingsPairEditor,
                _ => null
            };
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

        private void FocusEditorStart(ISettingsRecordListNavigator navigator)
        {
            Dispatcher.BeginInvoke(new Action(() =>
            {
                if (navigator is AliasPairEditorControl aliasEditor)
                {
                    aliasEditor.ResetToRoot();
                }

                navigator.FocusFirstRecord();
            }), System.Windows.Threading.DispatcherPriority.Loaded);
        }

        private void FocusFirstAliasRow()
        {
            FocusEditorStart(AliasesPairEditor);
        }

        private void FocusFirstMappingRow()
        {
            FocusEditorStart(MappingsPairEditor);
        }

        private void SaveAndClose_Click(object? sender, RoutedEventArgs? e)
        {
            _tabShell.SaveAll();
            GitSettings normalizedSettings = _editorSession.BuildSettingsForSave();
            _settingsManager.SaveSettings(normalizedSettings);

            Close();
        }

        private void OpenGitSettings()
        {
            try
            {
                string path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "git_settings.json");
                if (!File.Exists(path))
                {
                    MessageBox.Show("設定ファイルが見つかりません。", "エラー", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                Process.Start(new ProcessStartInfo
                {
                    FileName = path,
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                MessageBox.Show($"ファイルを開けませんでした: {ex.Message}", "エラー", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void OpenGitSettings_Click(object sender, RoutedEventArgs e)
        {
            OpenGitSettings();
        }

        private void Close_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        private bool IsAliasEditorButtonFocused()
        {
            if (Keyboard.FocusedElement is not DependencyObject focusedElement ||
                Keyboard.FocusedElement is System.Windows.Controls.Primitives.TextBoxBase)
            {
                return false;
            }

            return IsDescendantOf(focusedElement, AliasesPairEditor) || IsDescendantOf(focusedElement, MappingsPairEditor);
        }

        private bool IsAliasEditorTextInputFocused()
        {
            if (Keyboard.FocusedElement is not DependencyObject focusedElement ||
                Keyboard.FocusedElement is not System.Windows.Controls.Primitives.TextBoxBase)
            {
                return false;
            }

            return IsDescendantOf(focusedElement, AliasesPairEditor) || IsDescendantOf(focusedElement, MappingsPairEditor);
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

                current = VisualTreeHelper.GetParent(current);
            }

            return false;
        }
    }
}
