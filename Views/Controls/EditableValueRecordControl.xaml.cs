using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using HotKeyCommandApp.ViewModels.Settings;

namespace HotKeyCommandApp.Views.Controls
{
    public partial class EditableValueRecordControl : UserControl
    {
        public static readonly DependencyProperty ItemProperty =
            DependencyProperty.Register(nameof(Item), typeof(EditableValueRecordViewModel), typeof(EditableValueRecordControl), new PropertyMetadata(null));

        public EditableValueRecordControl()
        {
            InitializeComponent();
        }

        public EditableValueRecordViewModel? Item
        {
            get => (EditableValueRecordViewModel?)GetValue(ItemProperty);
            set => SetValue(ItemProperty, value);
        }

        public void FocusRowButton()
        {
            RecordButton.FocusRowButton();
        }

        private void RowButton_Click(object sender, RoutedEventArgs e)
        {
            BeginEdit();
        }

        private void RowButton_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter || e.Key == Key.F2)
            {
                BeginEdit();
                e.Handled = true;
                return;
            }

            if (SettingsRecordButtonKeyHandler.TryHandleStandardNavigation(RecordButton, e))
            {
                return;
            }
        }

        private void ValueTextBox_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (Item == null)
            {
                return;
            }

            if (e.Key == Key.Enter)
            {
                if (Item.CommitEdit())
                {
                    Dispatcher.BeginInvoke(new Action(FocusRowButton), System.Windows.Threading.DispatcherPriority.Loaded);
                }

                e.Handled = true;
                return;
            }

            if (e.Key == Key.Escape)
            {
                Item.CancelEdit();
                Dispatcher.BeginInvoke(new Action(FocusRowButton), System.Windows.Threading.DispatcherPriority.Loaded);
                e.Handled = true;
                return;
            }

            if (e.Key == Key.Up)
            {
                e.Handled = CommitAndMove(() => SettingsRecordNavigationHelper.FocusPrevious(RecordButton.RowButtonElement));
                return;
            }

            if (e.Key == Key.Down)
            {
                e.Handled = CommitAndMove(() => SettingsRecordNavigationHelper.FocusNext(RecordButton.RowButtonElement));
                return;
            }

        }

        private void ValueTextBox_GotFocus(object sender, RoutedEventArgs e)
        {
            if (sender is TextBox textBox)
            {
                textBox.SelectAll();
            }
        }

        private void ValueTextBox_LostKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
        {
            if (Item == null || !Item.IsEditing)
            {
                return;
            }

            if (e.NewFocus is DependencyObject nextFocus && IsDescendantOf(nextFocus, this))
            {
                return;
            }

            Item.CommitEdit();
        }

        private void BeginEdit()
        {
            if (Item == null)
            {
                return;
            }

            Item.BeginEdit();
            Dispatcher.BeginInvoke(new Action(() =>
            {
                ValueTextBox.Focus();
                ValueTextBox.SelectAll();
            }), System.Windows.Threading.DispatcherPriority.Loaded);
        }

        private bool CommitAndMove(Func<bool> moveFocus)
        {
            if (Item == null || !Item.CommitEdit())
            {
                return false;
            }

            Dispatcher.BeginInvoke(new Action(() => moveFocus()), System.Windows.Threading.DispatcherPriority.Loaded);
            return true;
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

                current = System.Windows.Media.VisualTreeHelper.GetParent(current);
            }

            return false;
        }
    }
}
