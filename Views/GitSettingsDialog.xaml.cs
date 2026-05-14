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
                    if (MainTabControl.SelectedIndex == 0)
                        FocusFirstAliasTextBox();
                    else
                        Dispatcher.BeginInvoke(() => FunctionNameTextBox.Focus(), System.Windows.Threading.DispatcherPriority.Loaded);
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
                    MainTabControl.SelectedIndex = MainTabControl.SelectedIndex == 0 ? 1 : 0;
                    e.Handled = true;
                    return;
                }

                if (Keyboard.Modifiers == ModifierKeys.Control && (e.Key == Key.OemPlus || e.Key == Key.Add))
                {
                    if (MainTabControl.SelectedIndex == 0)
                        AddAlias_Click(null, null);
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
                        FocusAliasTextBoxAtLastRow(isTargetCommand: true);
                        e.Handled = true;
                        return;
                    }

                    if (currentFocus == AddAliasButton && MainTabControl.SelectedIndex == 0)
                    {
                        FocusAliasTextBoxAtLastRow(isTargetCommand: false);
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

            RefreshAliasesList();
            RefreshFunctionsList();
        }

        private TextBox? FindFirstTextBox(DependencyObject parent)
        {
            if (parent == null) return null;
            for (int i = 0; i < System.Windows.Media.VisualTreeHelper.GetChildrenCount(parent); i++)
            {
                var child = System.Windows.Media.VisualTreeHelper.GetChild(parent, i);
                if (child is TextBox tb) return tb;
                var result = FindFirstTextBox(child);
                if (result != null) return result;
            }
            return null;
        }

        private void FocusFirstAliasTextBox()
        {
            Dispatcher.BeginInvoke(() =>
            {
                var tb = FindFirstTextBox(AliasesItemsControl);
                if (tb != null)
                {
                    tb.Focus();
                    tb.SelectAll();
                }
            }, System.Windows.Threading.DispatcherPriority.Loaded);
        }

        private void AliasTextBox_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (sender is TextBox textBox)
            {
                if (e.Key == Key.Up)
                {
                    textBox.MoveFocus(new TraversalRequest(FocusNavigationDirection.Up));
                    e.Handled = true;
                }
                else if (e.Key == Key.Down)
                {
                    textBox.MoveFocus(new TraversalRequest(FocusNavigationDirection.Down));
                    e.Handled = true;
                }
                else if (e.Key == Key.Left && textBox.CaretIndex == 0 && textBox.SelectionLength == 0)
                {
                    textBox.MoveFocus(new TraversalRequest(FocusNavigationDirection.Left));
                    e.Handled = true;
                }
                else if (e.Key == Key.Right && textBox.CaretIndex == textBox.Text.Length && textBox.SelectionLength == 0)
                {
                    textBox.MoveFocus(new TraversalRequest(FocusNavigationDirection.Right));
                    e.Handled = true;
                }
            }
        }

        private void RefreshAliasesList()
        {
            _isUpdatingUI = true;
            AliasesItemsControl.ItemsSource = null;
            AliasesItemsControl.ItemsSource = _workingSettings.Aliases;
            _isUpdatingUI = false;
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

        private void AddAlias_Click(object sender, RoutedEventArgs e)
        {
            var newEntry = new GitAliasEntry { Alias = "new_alias", TargetCommand = "command" };
            _workingSettings.Aliases.Add(newEntry);
            RefreshAliasesList();

            FocusAliasTextBoxAtLastRow(isTargetCommand: false, selectAll: true);
        }

        private void DeleteAliasInline_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.DataContext is GitAliasEntry entry)
            {
                int index = _workingSettings.Aliases.IndexOf(entry);
                if (index != -1)
                {
                    _workingSettings.Aliases.RemoveAt(index);
                    RefreshAliasesList();

                    int targetIndex = index < _workingSettings.Aliases.Count ? index : index - 1;
                    FocusAliasTargetCommandAtIndex(targetIndex);
                }
            }
        }

        private void FocusAliasTargetCommandAtIndex(int index)
        {
            Dispatcher.BeginInvoke(() =>
            {
                if (index < 0 || index >= _workingSettings.Aliases.Count) return;
                var container = AliasesItemsControl.ItemContainerGenerator.ContainerFromIndex(index) as DependencyObject;
                if (container != null)
                {
                    var textBoxes = new List<TextBox>();
                    FindAllTextBoxes(container, textBoxes);
                    if (textBoxes.Count > 1)
                    {
                        textBoxes[1].Focus();
                        textBoxes[1].CaretIndex = textBoxes[1].Text.Length;
                    }
                    else if (textBoxes.Count > 0)
                    {
                        textBoxes[0].Focus();
                        textBoxes[0].CaretIndex = textBoxes[0].Text.Length;
                    }
                }
            }, System.Windows.Threading.DispatcherPriority.Loaded);
        }

        private void FocusAliasTextBoxAtLastRow(bool isTargetCommand, bool selectAll = false)
        {
            Dispatcher.BeginInvoke(() =>
            {
                if (_workingSettings.Aliases.Count == 0) return;
                int lastIndex = _workingSettings.Aliases.Count - 1;
                var container = AliasesItemsControl.ItemContainerGenerator.ContainerFromIndex(lastIndex) as DependencyObject;
                if (container != null)
                {
                    var textBoxes = new List<TextBox>();
                    FindAllTextBoxes(container, textBoxes);
                    if (isTargetCommand && textBoxes.Count > 1)
                    {
                        textBoxes[1].Focus();
                        if (selectAll) textBoxes[1].SelectAll();
                        else textBoxes[1].CaretIndex = textBoxes[1].Text.Length;
                    }
                    else if (!isTargetCommand && textBoxes.Count > 0)
                    {
                        textBoxes[0].Focus();
                        if (selectAll) textBoxes[0].SelectAll();
                        else textBoxes[0].CaretIndex = textBoxes[0].Text.Length;
                    }
                }
            }, System.Windows.Threading.DispatcherPriority.Loaded);
        }

        private void FindAllTextBoxes(DependencyObject parent, List<TextBox> list)
        {
            if (parent == null) return;
            for (int i = 0; i < System.Windows.Media.VisualTreeHelper.GetChildrenCount(parent); i++)
            {
                var child = System.Windows.Media.VisualTreeHelper.GetChild(parent, i);
                if (child is TextBox tb) list.Add(tb);
                FindAllTextBoxes(child, list);
            }
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

        private void AddFunction_Click(object sender, RoutedEventArgs e)
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

        private void SaveAndClose_Click(object sender, RoutedEventArgs e)
        {
            _settingsManager.CurrentSettings.Aliases = _workingSettings.Aliases;
            _settingsManager.CurrentSettings.Functions = _workingSettings.Functions;
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
