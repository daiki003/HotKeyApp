using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using HotKeyCommandApp.Models;
using HotKeyCommandApp.Services;

namespace HotKeyCommandApp.Views
{
    public partial class ArgumentSelectDialog : Window
    {
        private readonly List<string> _allOptions;
        public string? SelectedOption { get; private set; }

        public ArgumentSelectDialog(CommandEntry command, string prompt, List<string> options)
        {
            InitializeComponent();
            
            PromptTextBlock.Text = prompt;
            _allOptions = options;
            
            UpdateOptionsList(string.Empty);
            
            this.Loaded += (s, e) => 
            {
                SearchTextBox.Focus();
                if (OptionsListBox.Items.Count > 0)
                {
                    OptionsListBox.SelectedIndex = 0;
                }
            };
        }

        private void UpdateOptionsList(string filter)
        {
            List<string> sortedOptions;
            if (string.IsNullOrWhiteSpace(filter))
            {
                sortedOptions = _allOptions.ToList();
            }
            else
            {
                var matched = _allOptions.Where(o => o.Contains(filter, StringComparison.OrdinalIgnoreCase)).ToList();
                var unmatched = _allOptions.Where(o => !o.Contains(filter, StringComparison.OrdinalIgnoreCase)).ToList();
                sortedOptions = matched.Concat(unmatched).ToList();
            }

            OptionsListBox.ItemsSource = sortedOptions;

            if (sortedOptions.Count > 0)
            {
                OptionsListBox.SelectedIndex = 0;
            }
        }

        private void SearchTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            UpdateOptionsList(SearchTextBox.Text);
        }

        private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape)
            {
                this.DialogResult = false;
                this.Close();
                e.Handled = true;
            }
            else if (e.Key == Key.Enter)
            {
                CommitSelection();
                e.Handled = true;
            }
            else if (e.Key == Key.Up)
            {
                MoveSelection(-1);
                e.Handled = true;
            }
            else if (e.Key == Key.Down)
            {
                MoveSelection(1);
                e.Handled = true;
            }
        }

        private void MoveSelection(int direction)
        {
            if (OptionsListBox.Items.Count == 0) return;

            int newIndex = OptionsListBox.SelectedIndex + direction;
            if (newIndex < 0) newIndex = OptionsListBox.Items.Count - 1;
            if (newIndex >= OptionsListBox.Items.Count) newIndex = 0;

            OptionsListBox.SelectedIndex = newIndex;
            OptionsListBox.ScrollIntoView(OptionsListBox.SelectedItem);
        }

        private void OptionsListBox_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            var item = ItemsControl.ContainerFromElement(OptionsListBox, e.OriginalSource as DependencyObject) as ListBoxItem;
            if (item != null)
            {
                OptionsListBox.SelectedItem = item.DataContext;
                CommitSelection();
                e.Handled = true;
            }
        }

        private void CommitSelection()
        {
            if (OptionsListBox.SelectedItem is string selected)
            {
                SelectedOption = selected;
                this.DialogResult = true;
                this.Close();
            }
        }

        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);
            WindowHelper.EnableWindowDragMove(this);
        }
    }
}
