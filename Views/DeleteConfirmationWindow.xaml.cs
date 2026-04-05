using System;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using HotKeyCommandApp.Services;
using System.Windows.Interop;

namespace HotKeyCommandApp.Views
{
    public partial class DeleteConfirmationWindow : Window
    {
        private bool _isDeleteSelected = true;

        public DeleteConfirmationWindow(string itemName)
        {
            InitializeComponent();
            MessageTextBlock.Text = $"「{itemName}」を削除しますか？";
            UpdateSelection();
        }

        private void UpdateSelection()
        {
            if (_isDeleteSelected)
            {
                DeleteButtonBorder.BorderBrush = Brushes.White;
                DeleteButtonBorder.BorderThickness = new Thickness(2);
                CancelButtonBorder.BorderThickness = new Thickness(0);
                DeleteButton.Focus();
            }
            else
            {
                CancelButtonBorder.BorderBrush = Brushes.White;
                CancelButtonBorder.BorderThickness = new Thickness(2);
                DeleteButtonBorder.BorderThickness = new Thickness(0);
                CancelButton.Focus();
            }
        }

        private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Left || e.Key == Key.Right)
            {
                _isDeleteSelected = !_isDeleteSelected;
                UpdateSelection();
                e.Handled = true;
            }
            else if (e.Key == Key.Enter)
            {
                if (_isDeleteSelected)
                {
                    this.DialogResult = true;
                }
                else
                {
                    this.DialogResult = false;
                }
                this.Close();
                e.Handled = true;
            }
            else if (e.Key == Key.Escape)
            {
                this.DialogResult = false;
                this.Close();
                e.Handled = true;
            }
        }

        private void DeleteButton_Click(object sender, RoutedEventArgs e)
        {
            this.DialogResult = true;
            this.Close();
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            this.DialogResult = false;
            this.Close();
        }

        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);
            WindowHelper.DisableSystemMenu(this);
        }
    }
}
