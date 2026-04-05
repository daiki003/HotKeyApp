using System;
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

        public ArgumentInputDialog(CommandEntry command, string prompt)
        {
            InitializeComponent();
            PromptTextBlock.Text = prompt;

            if (command.ArgumentsHistory != null && command.ArgumentsHistory.Count > 0)
            {
                HistoryContainer.Visibility = Visibility.Visible;
                HistoryListBox.ItemsSource = command.ArgumentsHistory;
                HistoryListBox.SelectedIndex = -1;
            }

            this.Loaded += (s, e) =>
            {
                InputTextBox.Focus();
                if (!string.IsNullOrEmpty(InputTextBox.Text))
                {
                    InputTextBox.SelectAll();
                }
            };
        }

        private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                // 履歴が選択されている場合は、その内容をテキストボックスに入れて選択を解除する（実行はしない）
                if (HistoryListBox.SelectedIndex >= 0 && HistoryListBox.SelectedItem != null)
                {
                    InputTextBox.Text = HistoryListBox.SelectedItem.ToString() ?? InputTextBox.Text;
                    InputTextBox.CaretIndex = InputTextBox.Text.Length;
                    HistoryListBox.SelectedIndex = -1;
                    e.Handled = true;
                    return;
                }

                // 選択がない、または二度目のEnterで実行
                InputText = InputTextBox.Text;
                SetDialogResult(true, e);
            }
            else if (e.Key == Key.Escape)
            {
                SetDialogResult(false, e);
            }
            else if (e.Key == Key.Down)
            {
                // 下キーで履歴を選択（フォーカスはテキストボックスのまま）
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
                // 上キーで選択を戻す
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
        }
    }
}
