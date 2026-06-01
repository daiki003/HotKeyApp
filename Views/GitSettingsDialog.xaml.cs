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

namespace HotKeyCommandApp.Views
{
    public partial class GitSettingsDialog : Window
    {
        private GitSettingsManager _settingsManager;
        private GitSettings _workingSettings;
        private GitAliasTab? _selectedAliasTab;
        private bool _isUpdatingUI = false;
        private bool _keepFocusOnAliasTabs;

        private ListBox? AliasTabsControl =>
            (AliasesPairEditor.HeaderBottomContent as StackPanel)?.Children.OfType<ListBox>().FirstOrDefault();

        private Button? AddAliasTabButton =>
            (AliasesPairEditor.HeaderBottomContent as StackPanel)?.Children.OfType<Button>().FirstOrDefault();

        public GitSettingsDialog()
        {
            InitializeComponent();

            _settingsManager = new GitSettingsManager();

            // クローンを作成して編集用にする
            var json = System.Text.Json.JsonSerializer.Serialize(_settingsManager.CurrentSettings);
            _workingSettings = System.Text.Json.JsonSerializer.Deserialize<GitSettings>(json) ?? new GitSettings();
            _workingSettings.EnsureAliasTabs();
            DataContext = _workingSettings;
            MappingsPairEditor.AddItemCommand = new ViewModels.RelayCommand<object>(_ => _workingSettings.RepositoryNameMappings.Add(new RepositoryNameMapping { Path = "C:\\path\\to\\repo", OverwrittenName = "MyRepo" }));
            MappingsPairEditor.FolderItemsSource = _workingSettings.RepositoryNameMappingFolders;

            this.Loaded += (s, e) =>
            {
                WindowHelper.EnableWindowDragMove(this);
                WindowHelper.EnableWindowMoveShortcut(this);
                if (AliasTabsControl != null)
                {
                    AliasTabsControl.SelectedIndex = 0;
                }
                BindSelectedAliasTab();
                MappingsPairEditor.ResetToRoot();
                FocusFirstAliasRow();
            };

            MainTabControl.SelectionChanged += (s, e) =>
            {
                if (e.Source == MainTabControl)
                {
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
            };

            this.PreviewKeyDown += (s, e) =>
            {
                bool isInteractiveFocused = Keyboard.FocusedElement is System.Windows.Controls.Primitives.TextBoxBase ||
                                            Keyboard.FocusedElement is System.Windows.Controls.Primitives.ButtonBase ||
                                            Keyboard.FocusedElement is System.Windows.Controls.ListBoxItem;

                if (e.Key == Key.Escape)
                {
                    if (IsAliasEditorTextInputFocused())
                    {
                        return;
                    }

                    if (IsAliasEditorButtonFocused())
                    {
                        this.Close();
                        e.Handled = true;
                        return;
                    }

                    if (!isInteractiveFocused)
                    {
                        this.Close();
                    }
                    else
                    {
                        this.Close();
                    }
                    e.Handled = true;
                    return;
                }

                if (e.Key == Key.PageUp || e.Key == Key.PageDown)
                {
                    int tabCount = MainTabControl.Items.Count;
                    if (tabCount > 0)
                    {
                        if (e.Key == Key.PageDown)
                        {
                            MainTabControl.SelectedIndex = (MainTabControl.SelectedIndex + 1) % tabCount;
                        }
                        else if (e.Key == Key.PageUp)
                        {
                            MainTabControl.SelectedIndex = (MainTabControl.SelectedIndex - 1 + tabCount) % tabCount;
                        }
                    }
                    e.Handled = true;
                    return;
                }

                if (Keyboard.Modifiers == ModifierKeys.Control && (e.Key == Key.OemPlus || e.Key == Key.Add))
                {
                    if (MainTabControl.SelectedIndex == 0)
                        AliasesPairEditor.AddNewItem();
                    else if (MainTabControl.SelectedIndex == 2)
                        MappingsPairEditor.AddNewItem();
                    else
                        AddFunction_Click(null, null);
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

                if (!isInteractiveFocused && MainTabControl.SelectedIndex == 0)
                {
                    if (e.Key == Key.Up || e.Key == Key.Down || e.Key == Key.Left || e.Key == Key.Right)
                    {
                        FocusFirstAliasRow();
                        e.Handled = true;
                        return;
                    }
                }

                var currentFocus = Keyboard.FocusedElement as DependencyObject;

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
                    if ((currentFocus == SaveAndCloseButton || currentFocus == CancelButton) && MainTabControl.SelectedIndex == 0)
                    {
                        if (ShouldFocusAliasTabsOnUp())
                        {
                            FocusSelectedAliasTab();
                        }
                        else
                        {
                            AliasesPairEditor.FocusFirstNavigableButton();
                        }
                        e.Handled = true;
                        return;
                    }

                    if ((currentFocus == SaveAndCloseButton || currentFocus == CancelButton) && MainTabControl.SelectedIndex == 2)
                    {
                        MappingsPairEditor.FocusFirstNavigableButton();
                        e.Handled = true;
                        return;
                    }
                }

                if (e.Key == Key.Up && MainTabControl.SelectedIndex == 0 && IsFocusInAliasEditor())
                {
                    if (ShouldFocusAliasTabsOnUp() || TryFocusAliasTabsFromTopRow())
                    {
                        FocusSelectedAliasTab();
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
                    if (e.Key == Key.Left) { this.Width = Math.Max(300, this.Width - step); e.Handled = true; }
                    else if (e.Key == Key.Right) { this.Width = Math.Min(1600, this.Width + step); e.Handled = true; }
                    else if (e.Key == Key.Up) { this.Height = Math.Max(200, this.Height - step); e.Handled = true; }
                    else if (e.Key == Key.Down) { this.Height = Math.Min(1200, this.Height + step); e.Handled = true; }
                }
                else if (Keyboard.Modifiers == ModifierKeys.Control)
                {
                    if (e.Key == Key.Left || e.Key == Key.Right || e.Key == Key.Up || e.Key == Key.Down)
                    {
                        e.Handled = true;
                    }
                }
            };

            RefreshFunctionsList();
        }

        private void FocusFirstAliasRow()
        {
            Dispatcher.BeginInvoke(() =>
            {
                AliasesPairEditor.ResetToRoot();
                if (AliasesPairEditor.FocusFirstRowButton())
                {
                    return;
                }
                AliasesPairEditor.FocusAddButton();
            }, System.Windows.Threading.DispatcherPriority.Loaded);
        }

        private void BindSelectedAliasTab()
        {
            _selectedAliasTab = AliasTabsControl?.SelectedItem as GitAliasTab ?? _workingSettings.GetAllAliasTabs().FirstOrDefault();

            if (_selectedAliasTab == null)
            {
                return;
            }

            AliasesPairEditor.ItemsSource = _selectedAliasTab.Aliases;
            AliasesPairEditor.FolderItemsSource = _selectedAliasTab.Folders;
            AliasesPairEditor.AddItemCommand = new ViewModels.RelayCommand<object>(_ =>
                _selectedAliasTab.Aliases.Add(new GitAliasEntry { Alias = "new_alias", TargetCommand = "command" }));
        }

        private bool TryFocusAliasTabsFromTopRow()
        {
            if (Keyboard.FocusedElement is not DependencyObject focusedElement)
            {
                return false;
            }

            var rowButton = FindAncestor<Button>(focusedElement);
            if (rowButton == null || rowButton.Name != "RowButton")
            {
                return false;
            }

            if (rowButton.DataContext is not IPairEntryEditable item)
            {
                return false;
            }

            if (AliasesPairEditor.VisibleItems.FirstOrDefault() != item)
            {
                return false;
            }

            return FocusSelectedAliasTab();
        }

        private bool FocusSelectedAliasTab()
        {
            if (AliasTabsControl == null)
            {
                return false;
            }

            if (AliasTabsControl.SelectedIndex < 0)
            {
                AliasTabsControl.SelectedIndex = 0;
            }

            if (AliasTabsControl.ItemContainerGenerator.ContainerFromIndex(AliasTabsControl.SelectedIndex) is ListBoxItem item)
            {
                return item.Focus();
            }

            Dispatcher.BeginInvoke(new Action(() =>
            {
                if (AliasTabsControl.ItemContainerGenerator.ContainerFromIndex(AliasTabsControl.SelectedIndex) is ListBoxItem generatedItem)
                {
                    generatedItem.Focus();
                }
            }), System.Windows.Threading.DispatcherPriority.Loaded);

            return true;
        }

        private bool ShouldFocusAliasTabsOnUp()
        {
            if (MainTabControl.SelectedIndex != 0 || !IsAliasTabEmpty())
            {
                return false;
            }

            return Keyboard.FocusedElement == SaveAndCloseButton ||
                   Keyboard.FocusedElement == CancelButton ||
                   Keyboard.FocusedElement == AliasesPairEditor.FindName("AddItemButton") ||
                   Keyboard.FocusedElement == AliasesPairEditor.FindName("AddFolderButton");
        }

        private bool IsAliasTabEmpty()
        {
            return AliasesPairEditor.VisibleItems.Count == 0;
        }

        private bool IsFocusInAliasEditor()
        {
            return Keyboard.FocusedElement is DependencyObject focusedElement && IsDescendantOf(focusedElement, AliasesPairEditor);
        }

        private static T? FindAncestor<T>(DependencyObject? current) where T : DependencyObject
        {
            while (current != null)
            {
                if (current is T matched)
                {
                    return matched;
                }

                current = VisualTreeHelper.GetParent(current);
            }

            return null;
        }

        private void FocusFirstMappingRow()
        {
            Dispatcher.BeginInvoke(() =>
            {
                if (MappingsPairEditor.FocusFirstRowButton())
                {
                    return;
                }
                MappingsPairEditor.FocusAddButton();
            }, System.Windows.Threading.DispatcherPriority.Loaded);
        }

        private void RefreshFunctionsList()
        {
            _isUpdatingUI = true;
            object selected = FunctionsListBox.SelectedItem;
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
            if (_isUpdatingUI) return;

            if (FunctionsListBox.SelectedItem is GitFunctionEntry entry)
            {
                _isUpdatingUI = true;
                FunctionNameTextBox.Text = entry.Name;
                FunctionDescTextBox.Text = entry.Description;
                FunctionCommandsTextBox.Text = string.Join(Environment.NewLine, entry.Commands ?? new List<string>());
                _isUpdatingUI = false;
            }
            else
            {
                _isUpdatingUI = true;
                FunctionNameTextBox.Text = string.Empty;
                FunctionDescTextBox.Text = string.Empty;
                FunctionCommandsTextBox.Text = string.Empty;
                _isUpdatingUI = false;
            }
        }

        private void FunctionTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (_isUpdatingUI) return;

            if (FunctionsListBox.SelectedItem is GitFunctionEntry entry)
            {
                entry.Name = FunctionNameTextBox.Text;
                entry.Description = FunctionDescTextBox.Text;
                entry.Commands = FunctionCommandsTextBox.Text.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.RemoveEmptyEntries)
                                     .Select(cmd => cmd.Trim()).Where(cmd => !string.IsNullOrEmpty(cmd)).ToList();
                FunctionsListBox.Items.Refresh();
            }
        }

        private void AddFunction_Click(object? sender, RoutedEventArgs? e)
        {
            var newEntry = new GitFunctionEntry
            {
                Name = "new_func",
                Description = "",
                Commands = new List<string> { "echo test" }
            };
            _workingSettings.Functions.Add(newEntry);
            RefreshFunctionsList();
            FunctionsListBox.SelectedItem = newEntry;
            FunctionNameTextBox.Focus();
            FunctionNameTextBox.SelectAll();
        }

        private void AliasTabsControl_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (e.Source != AliasTabsControl)
            {
                return;
            }

            BindSelectedAliasTab();
            if (_keepFocusOnAliasTabs)
            {
                _keepFocusOnAliasTabs = false;
                FocusSelectedAliasTab();
                return;
            }

            FocusFirstAliasRow();
        }

        private void AddAliasTabButton_Click(object sender, RoutedEventArgs e)
        {
            var aliasTabs = _workingSettings.GetAllAliasTabs();
            var newTab = new GitAliasTab
            {
                Name = $"タブ{aliasTabs.Count + 1}"
            };

            aliasTabs.Add(newTab);
            if (AliasTabsControl != null)
            {
                _keepFocusOnAliasTabs = true;
                AliasTabsControl.Items.Refresh();
                AliasTabsControl.SelectedItem = newTab;
            }
            BindSelectedAliasTab();
            FocusSelectedAliasTab();
        }

        private void AliasTabsControl_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (Keyboard.FocusedElement is not ListBoxItem focusedItem)
            {
                return;
            }

            if (AliasTabsControl == null)
            {
                return;
            }

            int currentIndex = AliasTabsControl.ItemContainerGenerator.IndexFromContainer(focusedItem);
            if (currentIndex < 0)
            {
                return;
            }

            if (e.Key == Key.Right)
            {
                if (currentIndex == AliasTabsControl.Items.Count - 1)
                {
                    AddAliasTabButton?.Focus();
                }
                else if (AliasTabsControl.ItemContainerGenerator.ContainerFromIndex(currentIndex + 1) is ListBoxItem nextItem)
                {
                    _keepFocusOnAliasTabs = true;
                    nextItem.Focus();
                    nextItem.IsSelected = true;
                }

                e.Handled = true;
                return;
            }

            if (e.Key == Key.Home && AliasesPairEditor.FocusTopButton())
            {
                e.Handled = true;
                return;
            }

            if (e.Key == Key.End && AliasesPairEditor.FocusBottomButton())
            {
                e.Handled = true;
                return;
            }

            if (e.Key == Key.Delete)
            {
                DeleteSelectedAliasTab();
                e.Handled = true;
                return;
            }

            if (e.Key == Key.Left && currentIndex > 0)
            {
                if (AliasTabsControl.ItemContainerGenerator.ContainerFromIndex(currentIndex - 1) is ListBoxItem previousItem)
                {
                    _keepFocusOnAliasTabs = true;
                    previousItem.Focus();
                    previousItem.IsSelected = true;
                }

                e.Handled = true;
                return;
            }

            if (e.Key == Key.Down)
            {
                FocusFirstAliasRow();
                e.Handled = true;
            }
        }

        private void DeleteSelectedAliasTab()
        {
            if (_selectedAliasTab == null)
            {
                return;
            }

            var dialog = new DeleteConfirmationWindow(_selectedAliasTab.Name)
            {
                Owner = this
            };

            if (dialog.ShowDialog() != true)
            {
                FocusSelectedAliasTab();
                return;
            }

            var aliasTabs = _workingSettings.GetAllAliasTabs();
            int currentIndex = aliasTabs.IndexOf(_selectedAliasTab);
            if (currentIndex < 0)
            {
                return;
            }

            if (aliasTabs.Count == 1)
            {
                aliasTabs[0] = new GitAliasTab
                {
                    Id = "general",
                    Name = "一般"
                };
                currentIndex = 0;
            }
            else
            {
                aliasTabs.RemoveAt(currentIndex);
                currentIndex = currentIndex > 0 ? currentIndex - 1 : 0;
            }

            if (AliasTabsControl != null)
            {
                _keepFocusOnAliasTabs = true;
                AliasTabsControl.Items.Refresh();
                AliasTabsControl.SelectedIndex = currentIndex;
            }

            BindSelectedAliasTab();
            FocusSelectedAliasTab();
        }

        private void AddAliasTabButton_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (AliasTabsControl != null && e.Key == Key.Left && AliasTabsControl.Items.Count > 0)
            {
                AliasTabsControl.SelectedIndex = AliasTabsControl.Items.Count - 1;
                FocusSelectedAliasTab();
                e.Handled = true;
                return;
            }

            if (e.Key == Key.Down)
            {
                FocusFirstAliasRow();
                e.Handled = true;
            }
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
            var generalTab = _workingSettings.GetAllAliasTabs().First();
            _workingSettings.Aliases = generalTab.Aliases;
            _workingSettings.AliasFolders = generalTab.Folders;

            _settingsManager.CurrentSettings.Aliases = _workingSettings.Aliases;
            _settingsManager.CurrentSettings.AliasFolders = _workingSettings.AliasFolders;
            _settingsManager.CurrentSettings.AliasTabs = _workingSettings.AliasTabs;
            _settingsManager.CurrentSettings.Functions = _workingSettings.Functions;
            _settingsManager.CurrentSettings.RepositoryNameMappings = _workingSettings.RepositoryNameMappings;
            _settingsManager.CurrentSettings.RepositoryNameMappingFolders = _workingSettings.RepositoryNameMappingFolders;
            _settingsManager.SaveSettings();

            this.Close();
        }

        private void OpenGitSettings_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                string path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "git_settings.json");
                if (File.Exists(path))
                {
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = path,
                        UseShellExecute = true
                    });
                }
                else
                {
                    MessageBox.Show("設定ファイルが見つかりません。", "エラー", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"ファイルを開けませんでした: {ex.Message}", "エラー", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void Close_Click(object sender, RoutedEventArgs e)
        {
            this.Close();
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

        private bool IsDescendantOf(DependencyObject child, DependencyObject ancestor)
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
