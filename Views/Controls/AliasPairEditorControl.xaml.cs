using System;
using System.Collections;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace HotKeyCommandApp.Views.Controls
{
    public partial class AliasPairEditorControl : UserControl
    {
        private const string ReorderDataFormat = "AliasPairEditorItem";
        private Point _dragStartPoint;

        public AliasPairEditorControl()
        {
            InitializeComponent();
        }

        public static readonly DependencyProperty ItemsSourceProperty =
            DependencyProperty.Register(nameof(ItemsSource), typeof(IList), typeof(AliasPairEditorControl), new PropertyMetadata(null));

        public static readonly DependencyProperty AddItemCommandProperty =
            DependencyProperty.Register(nameof(AddItemCommand), typeof(ICommand), typeof(AliasPairEditorControl), new PropertyMetadata(null));

        public static readonly DependencyProperty FirstHeaderProperty =
            DependencyProperty.Register(nameof(FirstHeader), typeof(string), typeof(AliasPairEditorControl), new PropertyMetadata(string.Empty));

        public static readonly DependencyProperty SecondHeaderProperty =
            DependencyProperty.Register(nameof(SecondHeader), typeof(string), typeof(AliasPairEditorControl), new PropertyMetadata(string.Empty));

        public static readonly DependencyProperty AddButtonTextProperty =
            DependencyProperty.Register(nameof(AddButtonText), typeof(string), typeof(AliasPairEditorControl), new PropertyMetadata("新規追加"));

        public static readonly DependencyProperty FirstColumnWidthProperty =
            DependencyProperty.Register(nameof(FirstColumnWidth), typeof(GridLength), typeof(AliasPairEditorControl), new PropertyMetadata(new GridLength(150)));

        public IList? ItemsSource
        {
            get => (IList?)GetValue(ItemsSourceProperty);
            set => SetValue(ItemsSourceProperty, value);
        }

        public ICommand? AddItemCommand
        {
            get => (ICommand?)GetValue(AddItemCommandProperty);
            set => SetValue(AddItemCommandProperty, value);
        }

        public string FirstHeader
        {
            get => (string)GetValue(FirstHeaderProperty);
            set => SetValue(FirstHeaderProperty, value);
        }

        public string SecondHeader
        {
            get => (string)GetValue(SecondHeaderProperty);
            set => SetValue(SecondHeaderProperty, value);
        }

        public string AddButtonText
        {
            get => (string)GetValue(AddButtonTextProperty);
            set => SetValue(AddButtonTextProperty, value);
        }

        public GridLength FirstColumnWidth
        {
            get => (GridLength)GetValue(FirstColumnWidthProperty);
            set => SetValue(FirstColumnWidthProperty, value);
        }

        public bool IsAddButtonFocused => Keyboard.FocusedElement == AddItemButton;

        public void FocusAddButton()
        {
            AddItemButton.Focus();
        }

        public bool FocusFirstTextBox()
        {
            var textBox = FindFirstTextBox(PairItemsControl);
            if (textBox == null)
            {
                return false;
            }

            textBox.Focus();
            textBox.SelectAll();
            return true;
        }

        public void FocusLastRow(bool focusSecondColumn, bool selectAll = false)
        {
            FocusTextBoxAtIndex((ItemsSource?.Count ?? 0) - 1, focusSecondColumn, selectAll);
        }

        public void AddNewItem()
        {
            if (AddItemCommand?.CanExecute(null) != true)
            {
                return;
            }

            AddItemCommand.Execute(null);
            RefreshItems();
            FocusLastRow(focusSecondColumn: false, selectAll: true);
        }

        public void FocusTextBoxAtIndex(int index, bool focusSecondColumn, bool selectAll = false)
        {
            Dispatcher.BeginInvoke(new Action(() =>
            {
                PairItemsControl.UpdateLayout();
                if (index < 0 || index >= PairItemsControl.Items.Count)
                {
                    return;
                }

                if (PairItemsControl.ItemContainerGenerator.ContainerFromIndex(index) is not DependencyObject container)
                {
                    return;
                }

                var textBoxes = new List<TextBox>();
                FindAllTextBoxes(container, textBoxes);

                int textBoxIndex = focusSecondColumn ? 1 : 0;
                if (textBoxes.Count <= textBoxIndex)
                {
                    return;
                }

                textBoxes[textBoxIndex].Focus();
                if (selectAll)
                {
                    textBoxes[textBoxIndex].SelectAll();
                }
                else
                {
                    textBoxes[textBoxIndex].CaretIndex = textBoxes[textBoxIndex].Text.Length;
                }
            }), System.Windows.Threading.DispatcherPriority.Loaded);
        }

        private void AddItemButton_Click(object sender, RoutedEventArgs e)
        {
            AddNewItem();
        }

        private void DeleteInline_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button button || button.DataContext == null || ItemsSource == null)
            {
                return;
            }

            int index = ItemsSource.IndexOf(button.DataContext);
            if (index < 0)
            {
                return;
            }

            ItemsSource.RemoveAt(index);
            RefreshItems();

            int targetIndex = index < ItemsSource.Count ? index : index - 1;
            FocusTextBoxAtIndex(targetIndex, focusSecondColumn: true);
        }

        private void EntryTextBox_GotFocus(object sender, RoutedEventArgs e)
        {
            if (sender is not TextBox textBox)
            {
                return;
            }

            textBox.CaretIndex = textBox.Text.Length;
            Dispatcher.BeginInvoke(new Action(() =>
            {
                if (textBox.IsFocused)
                {
                    textBox.CaretIndex = textBox.Text.Length;
                }
            }), System.Windows.Threading.DispatcherPriority.Background);
        }

        private void EntryTextBox_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (sender is not TextBox textBox || textBox.DataContext == null || ItemsSource == null)
            {
                return;
            }

            if (Keyboard.Modifiers == ModifierKeys.Shift && (e.Key == Key.Up || e.Key == Key.Down))
            {
                bool moveUp = e.Key == Key.Up;
                bool focusSecondColumn = Grid.GetColumn(textBox) == 2;
                MoveItem(textBox.DataContext, moveUp, focusSecondColumn);
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

        private void MoveItem(object item, bool moveUp, bool focusSecondColumn)
        {
            if (ItemsSource == null)
            {
                return;
            }

            int currentIndex = ItemsSource.IndexOf(item);
            int targetIndex = moveUp ? currentIndex - 1 : currentIndex + 1;
            if (currentIndex < 0 || targetIndex < 0 || targetIndex >= ItemsSource.Count)
            {
                return;
            }

            ItemsSource.RemoveAt(currentIndex);
            ItemsSource.Insert(targetIndex, item);
            RefreshItems();
            FocusTextBoxAtIndex(targetIndex, focusSecondColumn);
        }

        private void DragHandle_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            _dragStartPoint = e.GetPosition(null);
        }

        private void DragHandle_PreviewMouseMove(object sender, MouseEventArgs e)
        {
            if (e.LeftButton != MouseButtonState.Pressed)
            {
                return;
            }

            Point currentPosition = e.GetPosition(null);
            Vector diff = _dragStartPoint - currentPosition;
            if (Math.Abs(diff.X) <= SystemParameters.MinimumHorizontalDragDistance &&
                Math.Abs(diff.Y) <= SystemParameters.MinimumVerticalDragDistance)
            {
                return;
            }

            if (sender is FrameworkElement element && element.DataContext != null)
            {
                DragDrop.DoDragDrop(element, new DataObject(ReorderDataFormat, element.DataContext), DragDropEffects.Move);
            }
        }

        private void Row_DragOver(object sender, DragEventArgs e)
        {
            if (!e.Data.GetDataPresent(ReorderDataFormat))
            {
                return;
            }

            e.Effects = DragDropEffects.Move;
            e.Handled = true;
        }

        private void Row_Drop(object sender, DragEventArgs e)
        {
            if (!e.Data.GetDataPresent(ReorderDataFormat) || sender is not FrameworkElement row || row.DataContext == null || ItemsSource == null)
            {
                return;
            }

            object? draggedItem = e.Data.GetData(ReorderDataFormat);
            object targetItem = row.DataContext;
            if (draggedItem == null || ReferenceEquals(draggedItem, targetItem))
            {
                return;
            }

            int sourceIndex = ItemsSource.IndexOf(draggedItem);
            int targetIndex = ItemsSource.IndexOf(targetItem);
            if (sourceIndex < 0 || targetIndex < 0 || sourceIndex == targetIndex)
            {
                return;
            }

            ItemsSource.RemoveAt(sourceIndex);
            ItemsSource.Insert(targetIndex, draggedItem);
            RefreshItems();
            FocusTextBoxAtIndex(targetIndex, focusSecondColumn: false);
            e.Handled = true;
        }

        private void RefreshItems()
        {
            PairItemsControl.Items.Refresh();
            PairItemsControl.UpdateLayout();
        }

        private TextBox? FindFirstTextBox(DependencyObject parent)
        {
            for (int i = 0; i < System.Windows.Media.VisualTreeHelper.GetChildrenCount(parent); i++)
            {
                var child = System.Windows.Media.VisualTreeHelper.GetChild(parent, i);
                if (child is TextBox textBox)
                {
                    return textBox;
                }

                var result = FindFirstTextBox(child);
                if (result != null)
                {
                    return result;
                }
            }

            return null;
        }

        private void FindAllTextBoxes(DependencyObject parent, List<TextBox> textBoxes)
        {
            for (int i = 0; i < System.Windows.Media.VisualTreeHelper.GetChildrenCount(parent); i++)
            {
                var child = System.Windows.Media.VisualTreeHelper.GetChild(parent, i);
                if (child is TextBox textBox)
                {
                    textBoxes.Add(textBox);
                }

                FindAllTextBoxes(child, textBoxes);
            }
        }
    }
}
