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

        public bool ForceDelete => ForceDeleteCheckBox.IsChecked == true;

        public DeleteConfirmationWindow(string itemName, bool allowForceDelete = false, bool forceDelete = false, Action<bool>? onForceDeleteChanged = null)
        {
            InitializeComponent();
            MessageTextBlock.Text = $"「{itemName}」を削除しますか？";
            ForceDeleteCheckBox.Visibility = allowForceDelete ? Visibility.Visible : Visibility.Collapsed;
            ForceDeleteCheckBox.IsChecked = forceDelete;
            if (allowForceDelete) KeyboardHint.Text = "上下: 選択 / Space: チェック / Enter: 決定 / Esc: 戻る";
            if (allowForceDelete && onForceDeleteChanged != null)
            {
                ForceDeleteCheckBox.Checked += (s, e) => onForceDeleteChanged(true);
                ForceDeleteCheckBox.Unchecked += (s, e) => onForceDeleteChanged(false);
            }
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
            if (ForceDeleteCheckBox.IsKeyboardFocused && e.Key == Key.Enter)
            {
                ForceDeleteCheckBox.IsChecked = ForceDeleteCheckBox.IsChecked != true;
                e.Handled = true;
            }
            else if (ForceDeleteCheckBox.IsVisible && e.Key == Key.Up && !ForceDeleteCheckBox.IsKeyboardFocused)
            {
                ForceDeleteCheckBox.Focus();
                e.Handled = true;
            }
            else if (ForceDeleteCheckBox.IsKeyboardFocused && e.Key == Key.Down)
            {
                UpdateSelection();
                e.Handled = true;
            }
            else if (e.Key == Key.Left || e.Key == Key.Right)
            {
                _isDeleteSelected = !_isDeleteSelected;
                UpdateSelection();
                e.Handled = true;
            }
            else if (e.Key == Key.Enter)
            {
                if (DeleteButton.IsKeyboardFocused || (!CancelButton.IsKeyboardFocused && _isDeleteSelected))
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
            WindowHelper.EnableWindowMoveShortcut(this);
            WindowHelper.EnableWindowDragMove(this);
        }
    }
}
