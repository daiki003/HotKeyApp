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
            RefreshMappingsList();
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

        private void FocusFirstMappingTextBox()
        {
            Dispatcher.BeginInvoke(() =>
            {
                var tb = FindFirstTextBox(MappingsItemsControl);
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
                if (Keyboard.Modifiers == ModifierKeys.Shift && (e.Key == Key.Up || e.Key == Key.Down))
                {
                    bool isUp = e.Key == Key.Up;
                    var currentItem = textBox.DataContext;

                    if (currentItem is GitAliasEntry aliasEntry)
                    {
                        int idx = _workingSettings.Aliases.IndexOf(aliasEntry);
                        int targetIdx = isUp ? idx - 1 : idx + 1;
                        if (targetIdx >= 0 && targetIdx < _workingSettings.Aliases.Count)
                        {
                            bool isTargetCommand = Grid.GetColumn(textBox) == 2;

                            _workingSettings.Aliases.RemoveAt(idx);
                            _workingSettings.Aliases.Insert(targetIdx, aliasEntry);
                            RefreshAliasesList();

                            FocusAliasTextBoxAtIndex(targetIdx, isTargetCommand);
                        }
                    }
                    else if (currentItem is RepositoryNameMapping mappingEntry)
                    {
                        int idx = _workingSettings.RepositoryNameMappings.IndexOf(mappingEntry);
                        int targetIdx = isUp ? idx - 1 : idx + 1;
                        if (targetIdx >= 0 && targetIdx < _workingSettings.RepositoryNameMappings.Count)
                        {
                            bool isOverwrittenName = Grid.GetColumn(textBox) == 2;

                            _workingSettings.RepositoryNameMappings.RemoveAt(idx);
                            _workingSettings.RepositoryNameMappings.Insert(targetIdx, mappingEntry);
                            RefreshMappingsList();

                            FocusMappingTextBoxAtIndex(targetIdx, isOverwrittenName);
                        }
                    }

                    e.Handled = true;
                    return;
                }

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

        private void RefreshMappingsList()
        {
            _isUpdatingUI = true;
            MappingsItemsControl.ItemsSource = null;
            MappingsItemsControl.ItemsSource = _workingSettings.RepositoryNameMappings;
            _isUpdatingUI = false;
        }

        private void AddAlias_Click(object? sender, RoutedEventArgs? e)
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

        private void FocusAliasTextBoxAtIndex(int index, bool isTargetCommand, bool selectAll = false)
        {
            Dispatcher.BeginInvoke(() =>
            {
                if (index < 0 || index >= _workingSettings.Aliases.Count) return;
                var container = AliasesItemsControl.ItemContainerGenerator.ContainerFromIndex(index) as DependencyObject;
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

        private void FocusMappingTextBoxAtIndex(int index, bool isOverwrittenName, bool selectAll = false)
        {
            Dispatcher.BeginInvoke(() =>
            {
                if (index < 0 || index >= _workingSettings.RepositoryNameMappings.Count) return;
                var container = MappingsItemsControl.ItemContainerGenerator.ContainerFromIndex(index) as DependencyObject;
                if (container != null)
                {
                    var textBoxes = new List<TextBox>();
                    FindAllTextBoxes(container, textBoxes);
                    if (isOverwrittenName && textBoxes.Count > 1)
                    {
                        textBoxes[1].Focus();
                        if (selectAll) textBoxes[1].SelectAll();
                        else textBoxes[1].CaretIndex = textBoxes[1].Text.Length;
                    }
                    else if (!isOverwrittenName && textBoxes.Count > 0)
                    {
                        textBoxes[0].Focus();
                        if (selectAll) textBoxes[0].SelectAll();
                        else textBoxes[0].CaretIndex = textBoxes[0].Text.Length;
                    }
                }
            }, System.Windows.Threading.DispatcherPriority.Loaded);
        }

        private void FocusAliasTargetCommandAtIndex(int index)
        {
            FocusAliasTextBoxAtIndex(index, true);
        }

        private void FocusAliasTextBoxAtLastRow(bool isTargetCommand, bool selectAll = false)
        {
            FocusAliasTextBoxAtIndex(_workingSettings.Aliases.Count - 1, isTargetCommand, selectAll);
        }

        // Drag and Drop reordering
        private Point _dragStartPoint;

        private void DragHandle_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            _dragStartPoint = e.GetPosition(null);
        }

        private void DragHandle_PreviewMouseMove(object sender, MouseEventArgs e)
        {
            if (e.LeftButton == MouseButtonState.Pressed)
            {
                Point mousePos = e.GetPosition(null);
                Vector diff = _dragStartPoint - mousePos;

                if (Math.Abs(diff.X) > SystemParameters.MinimumHorizontalDragDistance ||
                    Math.Abs(diff.Y) > SystemParameters.MinimumVerticalDragDistance)
                {
                    if (sender is FrameworkElement dragHandle && dragHandle.DataContext != null)
                    {
                        var item = dragHandle.DataContext;
                        DragDrop.DoDragDrop(dragHandle, new DataObject("ReorderItem", item), DragDropEffects.Move);
                    }
                }
            }
        }

        private void Row_DragOver(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent("ReorderItem"))
            {
                e.Effects = DragDropEffects.Move;
                e.Handled = true;
            }
        }

        private void Row_Drop(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent("ReorderItem"))
            {
                var draggedItem = e.Data.GetData("ReorderItem");
                if (sender is FrameworkElement row && row.DataContext != null)
                {
                    var targetItem = row.DataContext;

                    if (draggedItem is GitAliasEntry sourceAlias && targetItem is GitAliasEntry targetAlias)
                    {
                        int sourceIdx = _workingSettings.Aliases.IndexOf(sourceAlias);
                        int targetIdx = _workingSettings.Aliases.IndexOf(targetAlias);
                        if (sourceIdx != -1 && targetIdx != -1 && sourceIdx != targetIdx)
                        {
                            _workingSettings.Aliases.RemoveAt(sourceIdx);
                            _workingSettings.Aliases.Insert(targetIdx, sourceAlias);
                            RefreshAliasesList();
                            FocusAliasTextBoxAtIndex(targetIdx, false);
                        }
                    }
                    else if (draggedItem is RepositoryNameMapping sourceMap && targetItem is RepositoryNameMapping targetMap)
                    {
                        int sourceIdx = _workingSettings.RepositoryNameMappings.IndexOf(sourceMap);
                        int targetIdx = _workingSettings.RepositoryNameMappings.IndexOf(targetMap);
                        if (sourceIdx != -1 && targetIdx != -1 && sourceIdx != targetIdx)
                        {
                            _workingSettings.RepositoryNameMappings.RemoveAt(sourceIdx);
                            _workingSettings.RepositoryNameMappings.Insert(targetIdx, sourceMap);
                            RefreshMappingsList();
                            FocusMappingTextBoxAtIndex(targetIdx, false);
                        }
                    }
                }
                e.Handled = true;
            }
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

        private void AddMapping_Click(object sender, RoutedEventArgs e)
        {
            var newEntry = new RepositoryNameMapping { Path = "C:\\path\\to\\repo", OverwrittenName = "MyRepo" };
            _workingSettings.RepositoryNameMappings.Add(newEntry);
            RefreshMappingsList();
        }

        private void DeleteMappingInline_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.DataContext is RepositoryNameMapping entry)
            {
                int index = _workingSettings.RepositoryNameMappings.IndexOf(entry);
                if (index != -1)
                {
                    _workingSettings.RepositoryNameMappings.RemoveAt(index);
                    RefreshMappingsList();
                }
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
