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
using HotKeyCommandApp.Views.Controls;

namespace HotKeyCommandApp.Views
{
    public partial class GitSettingsDialog : Window
    {
        private readonly GitSettingsManager _settingsManager;
        private readonly GitSettings _workingSettings;
        private bool _isUpdatingUI;

        public IPairEditorTabHost AliasTabHost { get; }

        public GitSettingsDialog()
        {
            InitializeComponent();

            _settingsManager = new GitSettingsManager();
            string json = System.Text.Json.JsonSerializer.Serialize(_settingsManager.CurrentSettings);
            _workingSettings = System.Text.Json.JsonSerializer.Deserialize<GitSettings>(json) ?? new GitSettings();
            _workingSettings.EnsureAliasTabs();
            AliasTabHost = new GitAliasTabHost(_workingSettings);
            DataContext = _workingSettings;

            MappingsPairEditor.AddItemCommand = new ViewModels.RelayCommand<object>(_ =>
                _workingSettings.RepositoryNameMappings.Add(new RepositoryNameMapping
                {
                    Path = "C:\\path\\to\\repo",
                    OverwrittenName = "MyRepo"
                }));
            MappingsPairEditor.FolderItemsSource = _workingSettings.RepositoryNameMappingFolders;

            Loaded += OnLoaded;
            MainTabControl.SelectionChanged += MainTabControl_SelectionChanged;
            PreviewKeyDown += GitSettingsDialog_PreviewKeyDown;

            RefreshFunctionsList();
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            WindowHelper.EnableWindowDragMove(this);
            WindowHelper.EnableWindowMoveShortcut(this);
            MappingsPairEditor.ResetToRoot();
            FocusFirstAliasRow();
        }

        private void MainTabControl_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (e.Source != MainTabControl)
            {
                return;
            }

            switch (MainTabControl.SelectedIndex)
            {
                case 0:
                    AliasesPairEditor.ResetToRoot();
                    FocusFirstAliasRow();
                    break;
                case 1:
                    Dispatcher.BeginInvoke(() => FunctionNameTextBox.Focus(), System.Windows.Threading.DispatcherPriority.Loaded);
                    break;
                case 2:
                    MappingsPairEditor.ResetToRoot();
                    FocusFirstMappingRow();
                    break;
            }
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
                int tabCount = MainTabControl.Items.Count;
                if (tabCount > 0)
                {
                    MainTabControl.SelectedIndex = e.Key == Key.PageDown
                        ? (MainTabControl.SelectedIndex + 1) % tabCount
                        : (MainTabControl.SelectedIndex - 1 + tabCount) % tabCount;
                }

                e.Handled = true;
                return;
            }

            if (Keyboard.Modifiers == ModifierKeys.Control && (e.Key == Key.OemPlus || e.Key == Key.Add))
            {
                switch (MainTabControl.SelectedIndex)
                {
                    case 0:
                        AliasesPairEditor.AddNewItem();
                        break;
                    case 1:
                        AddFunction_Click(null, null);
                        break;
                    case 2:
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

            AliasPairEditorControl? activeEditor = GetActivePairEditor();
            if (activeEditor?.TryHandleTabKeyboardShortcut(e) == true)
            {
                return;
            }

            if (!isInteractiveFocused && activeEditor != null &&
                (e.Key == Key.Up || e.Key == Key.Down || e.Key == Key.Left || e.Key == Key.Right))
            {
                FocusEditorStart(activeEditor);
                e.Handled = true;
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
                if ((currentFocus == SaveAndCloseButton || currentFocus == CancelButton) && activeEditor != null)
                {
                    if (activeEditor.ShouldFocusTabsOnUp())
                    {
                        activeEditor.FocusSelectedTab();
                    }
                    else
                    {
                        activeEditor.FocusFirstNavigableButton();
                    }

                    e.Handled = true;
                    return;
                }

                if (activeEditor?.IsFocusInItemEditor() == true &&
                    (activeEditor.ShouldFocusTabsOnUp() || activeEditor.TryFocusTabsFromTopRow()))
                {
                    activeEditor.FocusSelectedTab();
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

        private AliasPairEditorControl? GetActivePairEditor()
        {
            return MainTabControl.SelectedIndex switch
            {
                0 => AliasesPairEditor,
                2 => MappingsPairEditor,
                _ => null
            };
        }

        private void FocusEditorStart(AliasPairEditorControl editor)
        {
            Dispatcher.BeginInvoke(new Action(() =>
            {
                editor.ResetToRoot();
                if (editor.FocusFirstRowButton())
                {
                    return;
                }

                editor.FocusAddButton();
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

        private void RefreshFunctionsList()
        {
            _isUpdatingUI = true;
            object? selected = FunctionsListBox.SelectedItem;
            FunctionsListBox.ItemsSource = null;
            FunctionsListBox.ItemsSource = _workingSettings.Functions;
            if (selected != null && _workingSettings.Functions.Contains(selected))
            {
                FunctionsListBox.SelectedItem = selected;
            }

            _isUpdatingUI = false;
        }

        private void FunctionsListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_isUpdatingUI)
            {
                return;
            }

            _isUpdatingUI = true;
            if (FunctionsListBox.SelectedItem is GitFunctionEntry entry)
            {
                FunctionNameTextBox.Text = entry.Name;
                FunctionDescTextBox.Text = entry.Description;
                FunctionCommandsTextBox.Text = string.Join(Environment.NewLine, entry.Commands ?? new List<string>());
            }
            else
            {
                FunctionNameTextBox.Text = string.Empty;
                FunctionDescTextBox.Text = string.Empty;
                FunctionCommandsTextBox.Text = string.Empty;
            }

            _isUpdatingUI = false;
        }

        private void FunctionTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (_isUpdatingUI || FunctionsListBox.SelectedItem is not GitFunctionEntry entry)
            {
                return;
            }

            entry.Name = FunctionNameTextBox.Text;
            entry.Description = FunctionDescTextBox.Text;
            entry.Commands = FunctionCommandsTextBox.Text
                .Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.RemoveEmptyEntries)
                .Select(cmd => cmd.Trim())
                .Where(cmd => !string.IsNullOrEmpty(cmd))
                .ToList();
            FunctionsListBox.Items.Refresh();
        }

        private void AddFunction_Click(object? sender, RoutedEventArgs? e)
        {
            var newEntry = new GitFunctionEntry
            {
                Name = "new_func",
                Description = string.Empty,
                Commands = new List<string> { "echo test" }
            };

            _workingSettings.Functions.Add(newEntry);
            RefreshFunctionsList();
            FunctionsListBox.SelectedItem = newEntry;
            FunctionNameTextBox.Focus();
            FunctionNameTextBox.SelectAll();
        }

        private void DeleteFunction_Click(object sender, RoutedEventArgs e)
        {
            if (FunctionsListBox.SelectedItem is GitFunctionEntry entry)
            {
                _workingSettings.Functions.Remove(entry);
                RefreshFunctionsList();
            }
        }

        private void SaveAndClose_Click(object? sender, RoutedEventArgs? e)
        {
            _workingSettings.EnsureAliasTabs();
            foreach (GitAliasTab tab in _workingSettings.GetAllAliasTabs())
            {
                for (int i = 0; i < tab.Aliases.Count; i++)
                {
                    tab.Aliases[i].ParentFolderId = string.Empty;
                    tab.Aliases[i].SortOrder = i;
                }

                tab.Folders = new List<PairEntryFolder>();
            }

            for (int i = 0; i < _workingSettings.RepositoryNameMappings.Count; i++)
            {
                _workingSettings.RepositoryNameMappings[i].ParentFolderId = string.Empty;
                _workingSettings.RepositoryNameMappings[i].SortOrder = i;
            }

            _workingSettings.RepositoryNameMappingFolders = new List<PairEntryFolder>();
            GitAliasTab generalTab = _workingSettings.GetAllAliasTabs().First();
            _workingSettings.Aliases = generalTab.Aliases;
            _workingSettings.AliasFolders = generalTab.Folders;

            _settingsManager.CurrentSettings.Aliases = _workingSettings.Aliases;
            _settingsManager.CurrentSettings.AliasFolders = _workingSettings.AliasFolders;
            _settingsManager.CurrentSettings.AliasTabs = _workingSettings.AliasTabs;
            _settingsManager.CurrentSettings.Functions = _workingSettings.Functions;
            _settingsManager.CurrentSettings.RepositoryNameMappings = _workingSettings.RepositoryNameMappings;
            _settingsManager.CurrentSettings.RepositoryNameMappingFolders = _workingSettings.RepositoryNameMappingFolders;
            _settingsManager.SaveSettings();

            Close();
        }

        private void OpenGitSettings_Click(object sender, RoutedEventArgs e)
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
