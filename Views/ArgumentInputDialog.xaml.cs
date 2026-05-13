using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using System.Windows.Controls;
using HotKeyCommandApp.Models;
using HotKeyCommandApp.Services;
using System.Windows.Interop;

namespace HotKeyCommandApp.Views
{
    public partial class ArgumentInputDialog : Window
    {
        public string InputText { get; private set; } = string.Empty;
        private List<TextBox> _textBoxes = new List<TextBox>();
        private int _fixedArgumentCount = 0;

        private int GetRequiredArgumentCount(string? value)
        {
            if (string.IsNullOrEmpty(value)) return 0;
            int max = -1;
            for (int i = 0; i < 10; i++)
            {
                if (value.Contains($"{{{i}}}"))
                {
                    max = i;
                }
            }
            return max + 1;
        }

        public ArgumentInputDialog(CommandEntry command, string prompt)
        {
            InitializeComponent();
            PromptTextBlock.Text = prompt;

            if (command.ArgumentsHistory != null && command.ArgumentsHistory.Count > 0)
            {
                HistoryListBox.ItemsSource = command.ArgumentsHistory;
                HistoryListBox.SelectedIndex = -1;
            }
            else
            {
                HistoryListBox.ItemsSource = new List<string>();
            }

            _fixedArgumentCount = GetRequiredArgumentCount(command.Value);

            if (_fixedArgumentCount > 0)
            {
                AddArgumentButton.Visibility = Visibility.Collapsed;
                for (int i = 0; i < _fixedArgumentCount; i++)
                {
                    AddNewTextBox(focus: i == 0);
                }
            }
            else
            {
                AddNewTextBox(focus: true);
            }

            this.Loaded += (s, e) =>
            {
                if (_textBoxes.Count > 0)
                {
                    _textBoxes[0].Focus();
                }
            };

            InputScrollViewer.SizeChanged += (s, e) => UpdateTextBoxWidths();
        }

        private void UpdateTextBoxWidths()
        {
            if (_textBoxes.Count == 0) return;
            
            double availableWidth = InputScrollViewer.ActualWidth;
            if (availableWidth <= 0) return;

            // Maintain equal widths, with a minimum of 40 to allow up to 4-5 empty boxes without scrolling.
            // -2 avoids premature scrollbar appearing due to exact boundaries/rounding
            double targetMinWidth = Math.Max(40, (availableWidth - 2) / _textBoxes.Count);
            
            foreach (var tb in _textBoxes)
            {
                tb.MinWidth = targetMinWidth;
                tb.Width = double.NaN; // Auto width so it can grow if text is long
            }
        }

        private TextBox AddNewTextBox(bool focus = false, string text = "")
        {
            var textBox = new TextBox
            {
                Background = System.Windows.Media.Brushes.Transparent,
                Foreground = System.Windows.Media.Brushes.White,
                BorderThickness = new Thickness(0, 0, 1, 0),
                BorderBrush = new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#555555")),
                Padding = new Thickness(10, 8, 10, 8),
                FontSize = 15,
                CaretBrush = System.Windows.Media.Brushes.White,
                VerticalContentAlignment = VerticalAlignment.Center,
                AcceptsReturn = false,
                MinWidth = 40,
                Text = text
            };

            // Focus management
            textBox.PreviewKeyDown += TextBox_PreviewKeyDown;

            _textBoxes.Add(textBox);
            InputsStackPanel.Children.Add(textBox);
            UpdateTextBoxWidths();

            if (focus)
            {
                // Let it render then focus
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    textBox.Focus();
                    if (!string.IsNullOrEmpty(textBox.Text))
                    {
                        textBox.CaretIndex = textBox.Text.Length;
                    }
                    textBox.BringIntoView();
                }), System.Windows.Threading.DispatcherPriority.Loaded);
            }

            return textBox;
        }

        private void TextBox_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            var tb = sender as TextBox;
            if (tb == null) return;

            int index = _textBoxes.IndexOf(tb);

            if (e.Key == Key.Right || e.Key == Key.End)
            {
                if (tb.CaretIndex == tb.Text.Length && tb.SelectionLength == 0)
                {
                    if (index < _textBoxes.Count - 1)
                    {
                        var nextTb = _textBoxes[index + 1];
                        nextTb.Focus();
                        nextTb.CaretIndex = 0;
                        nextTb.BringIntoView();
                        e.Handled = true;
                    }
                    else if (_fixedArgumentCount == 0)
                    {
                        AddArgumentButton.Focus();
                        AddArgumentButton.BringIntoView();
                        e.Handled = true;
                    }
                }
            }
            else if (e.Key == Key.Left || e.Key == Key.Home)
            {
                if (tb.CaretIndex == 0 && tb.SelectionLength == 0)
                {
                    if (index > 0)
                    {
                        var prevTb = _textBoxes[index - 1];
                        prevTb.Focus();
                        prevTb.CaretIndex = prevTb.Text.Length;
                        prevTb.BringIntoView();
                        e.Handled = true;
                    }
                }
            }
            else if (e.Key == Key.PageUp)
            {
                if (index > 0)
                {
                    var prevTb = _textBoxes[index - 1];
                    prevTb.Focus();
                    prevTb.BringIntoView();
                    e.Handled = true;
                }
            }
            else if (e.Key == Key.PageDown)
            {
                if (index < _textBoxes.Count - 1)
                {
                    var nextTb = _textBoxes[index + 1];
                    nextTb.Focus();
                    nextTb.BringIntoView();
                    e.Handled = true;
                }
                else if (_fixedArgumentCount == 0)
                {
                    AddArgumentButton.Focus();
                    AddArgumentButton.BringIntoView();
                    e.Handled = true;
                }
            }
            else if ((e.Key == Key.Back || e.Key == Key.Delete) && tb.Text.Length == 0)
            {
                if (_fixedArgumentCount == 0 && _textBoxes.Count > 1)
                {
                    _textBoxes.Remove(tb);
                    InputsStackPanel.Children.Remove(tb);
                    
                    UpdateTextBoxWidths();

                    if (index > 0)
                    {
                        var prevTb = _textBoxes[index - 1];
                        prevTb.Focus();
                        prevTb.CaretIndex = prevTb.Text.Length;
                        prevTb.BringIntoView();
                    }
                    else
                    {
                        var nextTb = _textBoxes[0];
                        nextTb.Focus();
                        nextTb.CaretIndex = 0;
                        nextTb.BringIntoView();
                    }
                    
                    e.Handled = true;
                }
            }
        }

        private void AddArgumentButton_Click(object sender, RoutedEventArgs e)
        {
            AddNewTextBox(focus: true);
        }

        private string BuildArgumentsString()
        {
            var args = _textBoxes.Select(tb => tb.Text).Where(t => !string.IsNullOrEmpty(t)).ToList();
            var escapedArgs = args.Select(a => a.Contains(" ") ? $"\"{a}\"" : a);
            return string.Join(" ", escapedArgs);
        }

        private List<string> ParseArguments(string commandLine)
        {
            var args = new List<string>();
            bool inQuotes = false;
            string currentArg = "";
            for (int i = 0; i < commandLine.Length; i++)
            {
                char c = commandLine[i];
                if (c == '"')
                {
                    inQuotes = !inQuotes;
                }
                else if (c == ' ' && !inQuotes)
                {
                    if (!string.IsNullOrEmpty(currentArg))
                    {
                        args.Add(currentArg);
                        currentArg = "";
                    }
                }
                else
                {
                    currentArg += c;
                }
            }
            if (!string.IsNullOrEmpty(currentArg))
            {
                args.Add(currentArg);
            }
            return args;
        }

        private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                // If Add button is focused, let it handle Enter (it generates Click)
                if (AddArgumentButton.IsFocused)
                {
                    return;
                }

                if (HistoryListBox.SelectedIndex >= 0 && HistoryListBox.SelectedItem != null)
                {
                    string selectedHistory = HistoryListBox.SelectedItem.ToString() ?? "";
                    
                    var parsedArgs = ParseArguments(selectedHistory);
                    
                    // Clear existing text boxes
                    foreach (var tb in _textBoxes)
                    {
                        InputsStackPanel.Children.Remove(tb);
                    }
                    _textBoxes.Clear();

                    if (_fixedArgumentCount > 0)
                    {
                        for (int i = 0; i < _fixedArgumentCount; i++)
                        {
                            string text = i < parsedArgs.Count ? parsedArgs[i] : "";
                            AddNewTextBox(focus: i == 0, text: text);
                        }
                    }
                    else
                    {
                        if (parsedArgs.Count == 0)
                        {
                            AddNewTextBox(focus: true);
                        }
                        else
                        {
                            for (int i = 0; i < parsedArgs.Count; i++)
                            {
                                AddNewTextBox(focus: i == parsedArgs.Count - 1, text: parsedArgs[i]);
                            }
                        }
                    }

                    HistoryListBox.SelectedIndex = -1;
                    e.Handled = true;
                    return;
                }

                InputText = BuildArgumentsString();
                SetDialogResult(true, e);
            }
            else if (e.Key == Key.Escape)
            {
                SetDialogResult(false, e);
            }
            else if (e.Key == Key.Down)
            {
                if (HistoryListBox.Items.Count > 0)
                {
                    int next = HistoryListBox.SelectedIndex + 1;
                    if (next < HistoryListBox.Items.Count)
                    {
                        HistoryListBox.SelectedIndex = next;
                        HistoryListBox.ScrollIntoView(HistoryListBox.SelectedItem);
                    }
                    e.Handled = true;
                }
            }
            else if (e.Key == Key.Up)
            {
                if (HistoryListBox.Items.Count > 0)
                {
                    int prev = HistoryListBox.SelectedIndex - 1;
                    if (prev >= -1)
                    {
                        HistoryListBox.SelectedIndex = prev;
                        if (prev >= 0) HistoryListBox.ScrollIntoView(HistoryListBox.SelectedItem);
                    }
                    e.Handled = true;
                }
            }
            else if (e.Key == Key.Right && AddArgumentButton.IsFocused)
            {
                e.Handled = true; // Stay on button
            }
            else if ((e.Key == Key.Left || e.Key == Key.PageUp || e.Key == Key.Home) && AddArgumentButton.IsFocused)
            {
                if (_textBoxes.Count > 0)
                {
                    var lastTb = _textBoxes.Last();
                    lastTb.Focus();
                    lastTb.CaretIndex = lastTb.Text.Length;
                    lastTb.BringIntoView();
                    e.Handled = true;
                }
            }
        }

        private void HistoryListBox_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            var item = ItemsControl.ContainerFromElement(HistoryListBox, e.OriginalSource as DependencyObject) as ListBoxItem;
            if (item != null)
            {
                InputText = item.Content.ToString() ?? string.Empty;
                this.DialogResult = true;
                this.Close();
            }
        }

        private void SetDialogResult(bool result, KeyEventArgs e)
        {
            this.DialogResult = result;
            e.Handled = true;
            this.Close();
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
