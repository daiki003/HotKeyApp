using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using HotKeyCommandApp.Models;
using HotKeyCommandApp.Services;

namespace HotKeyCommandApp.Views
{
    public partial class GitSettingsDialog : Window
    {
        private GitSettingsManager _settingsManager;
        private GitSettings _workingSettings;
        private bool _isUpdatingUI = false;

        public GitSettingsDialog()
        {
            InitializeComponent();

            _settingsManager = new GitSettingsManager();

            // クローンを作成して編集用にする
            var json = System.Text.Json.JsonSerializer.Serialize(_settingsManager.CurrentSettings);
            _workingSettings = System.Text.Json.JsonSerializer.Deserialize<GitSettings>(json) ?? new GitSettings();
            DataContext = _workingSettings;
            AliasesPairEditor.AddItemCommand = new ViewModels.RelayCommand<object>(_ => _workingSettings.Aliases.Add(new GitAliasEntry { Alias = "new_alias", TargetCommand = "command" }));
            MappingsPairEditor.AddItemCommand = new ViewModels.RelayCommand<object>(_ => _workingSettings.RepositoryNameMappings.Add(new RepositoryNameMapping { Path = "C:\\path\\to\\repo", OverwrittenName = "MyRepo" }));

            this.Loaded += (s, e) =>
            {
                WindowHelper.EnableWindowDragMove(this);
                WindowHelper.EnableWindowMoveShortcut(this);
                FocusFirstAliasTextBox();
            };

            MainTabControl.SelectionChanged += (s, e) =>
            {
                if (e.Source == MainTabControl)
                {
                    switch (MainTabControl.SelectedIndex)
                    {
                        case 0:
                            FocusFirstAliasTextBox();
                            break;
                        case 1:
                            Dispatcher.BeginInvoke(() => FunctionNameTextBox.Focus(), System.Windows.Threading.DispatcherPriority.Loaded);
                            break;
                        case 2:
                            FocusFirstMappingTextBox();
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
                    if (!isInteractiveFocused)
                    {
                        this.Close();
                    }
                    else
                    {
                        FocusManager.SetFocusedElement(this, null);
                        Keyboard.ClearFocus();
                        RootBorder.Focus();
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
                        FocusFirstAliasTextBox();
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
                        AliasesPairEditor.FocusLastRow(focusSecondColumn: true);
                        e.Handled = true;
                        return;
                    }

                    if (AliasesPairEditor.IsAddButtonFocused && MainTabControl.SelectedIndex == 0)
                    {
                        AliasesPairEditor.FocusLastRow(focusSecondColumn: false);
                        e.Handled = true;
                        return;
                    }

                    if ((currentFocus == SaveAndCloseButton || currentFocus == CancelButton) && MainTabControl.SelectedIndex == 2)
                    {
                        MappingsPairEditor.FocusLastRow(focusSecondColumn: true);
                        e.Handled = true;
                        return;
                    }

                    if (MappingsPairEditor.IsAddButtonFocused && MainTabControl.SelectedIndex == 2)
                    {
                        MappingsPairEditor.FocusLastRow(focusSecondColumn: false);
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

        private void FocusFirstAliasTextBox()
        {
            Dispatcher.BeginInvoke(() =>
            {
                if (AliasesPairEditor.FocusFirstTextBox())
                {
                    return;
                }
                AliasesPairEditor.FocusAddButton();
            }, System.Windows.Threading.DispatcherPriority.Loaded);
        }

        private void FocusFirstMappingTextBox()
        {
            Dispatcher.BeginInvoke(() =>
            {
                if (MappingsPairEditor.FocusFirstTextBox())
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
            _settingsManager.CurrentSettings.Aliases = _workingSettings.Aliases;
            _settingsManager.CurrentSettings.Functions = _workingSettings.Functions;
            _settingsManager.CurrentSettings.RepositoryNameMappings = _workingSettings.RepositoryNameMappings;
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
    }
}
