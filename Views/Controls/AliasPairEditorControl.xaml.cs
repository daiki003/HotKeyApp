using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using HotKeyCommandApp.Models;

namespace HotKeyCommandApp.Views.Controls
{
    public partial class AliasPairEditorControl : UserControl
    {
        private const string ReorderDataFormat = "AliasPairEditorItem";
        private Point _dragStartPoint;
        private string _currentFolderId = string.Empty;
        private string _folderIdToFocusAfterNavigateUp = string.Empty;

        public AliasPairEditorControl()
        {
            InitializeComponent();
            UpdateFolderHeader();
        }

        public ObservableCollection<IPairEntryEditable> VisibleItems { get; } = new();

        public static readonly DependencyProperty ItemsSourceProperty =
            DependencyProperty.Register(nameof(ItemsSource), typeof(IList), typeof(AliasPairEditorControl), new PropertyMetadata(null, OnSourceChanged));

        public static readonly DependencyProperty FolderItemsSourceProperty =
            DependencyProperty.Register(nameof(FolderItemsSource), typeof(IList), typeof(AliasPairEditorControl), new PropertyMetadata(null, OnSourceChanged));

        public static readonly DependencyProperty AddItemCommandProperty =
            DependencyProperty.Register(nameof(AddItemCommand), typeof(ICommand), typeof(AliasPairEditorControl), new PropertyMetadata(null));

        public static readonly DependencyProperty FirstHeaderProperty =
            DependencyProperty.Register(nameof(FirstHeader), typeof(string), typeof(AliasPairEditorControl), new PropertyMetadata(string.Empty));

        public static readonly DependencyProperty SecondHeaderProperty =
            DependencyProperty.Register(nameof(SecondHeader), typeof(string), typeof(AliasPairEditorControl), new PropertyMetadata(string.Empty));

        public static readonly DependencyProperty AddButtonTextProperty =
            DependencyProperty.Register(nameof(AddButtonText), typeof(string), typeof(AliasPairEditorControl), new PropertyMetadata("新規追加"));

        public static readonly DependencyProperty AddFolderButtonTextProperty =
            DependencyProperty.Register(nameof(AddFolderButtonText), typeof(string), typeof(AliasPairEditorControl), new PropertyMetadata("フォルダ作成"));

        public static readonly DependencyProperty FirstColumnWidthProperty =
            DependencyProperty.Register(nameof(FirstColumnWidth), typeof(GridLength), typeof(AliasPairEditorControl), new PropertyMetadata(new GridLength(150)));

        public static readonly DependencyProperty DisplayValueOffsetProperty =
            DependencyProperty.Register(nameof(DisplayValueOffset), typeof(GridLength), typeof(AliasPairEditorControl), new PropertyMetadata(new GridLength(120)));

        public static readonly DependencyProperty CurrentEditingItemProperty =
            DependencyProperty.Register(nameof(CurrentEditingItem), typeof(object), typeof(AliasPairEditorControl), new PropertyMetadata(null));

        public static readonly DependencyProperty HeaderBottomContentProperty =
            DependencyProperty.Register(nameof(HeaderBottomContent), typeof(object), typeof(AliasPairEditorControl), new PropertyMetadata(null));

        public static readonly DependencyProperty ShowColumnHeadersProperty =
            DependencyProperty.Register(nameof(ShowColumnHeaders), typeof(bool), typeof(AliasPairEditorControl), new PropertyMetadata(true));

        public IList? ItemsSource
        {
            get => (IList?)GetValue(ItemsSourceProperty);
            set => SetValue(ItemsSourceProperty, value);
        }

        public IList? FolderItemsSource
        {
            get => (IList?)GetValue(FolderItemsSourceProperty);
            set => SetValue(FolderItemsSourceProperty, value);
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

        public string AddFolderButtonText
        {
            get => (string)GetValue(AddFolderButtonTextProperty);
            set => SetValue(AddFolderButtonTextProperty, value);
        }

        public GridLength FirstColumnWidth
        {
            get => (GridLength)GetValue(FirstColumnWidthProperty);
            set => SetValue(FirstColumnWidthProperty, value);
        }

        public GridLength DisplayValueOffset
        {
            get => (GridLength)GetValue(DisplayValueOffsetProperty);
            set => SetValue(DisplayValueOffsetProperty, value);
        }

        public object? CurrentEditingItem
        {
            get => GetValue(CurrentEditingItemProperty);
            set => SetValue(CurrentEditingItemProperty, value);
        }

        public object? HeaderBottomContent
        {
            get => GetValue(HeaderBottomContentProperty);
            set => SetValue(HeaderBottomContentProperty, value);
        }

        public bool ShowColumnHeaders
        {
            get => (bool)GetValue(ShowColumnHeadersProperty);
            set => SetValue(ShowColumnHeadersProperty, value);
        }

        public bool IsAddButtonFocused => Keyboard.FocusedElement == AddItemButton;

        public void FocusAddButton()
        {
            AddItemButton.Focus();
        }

        public bool FocusFirstRowButton()
        {
            if (VisibleItems.Count > 0)
            {
                return FocusRowButtonAtIndex(0);
            }

            if (BackRowButton.Visibility == Visibility.Visible)
            {
                return FocusBackRowButton();
            }

            return false;
        }

        public bool FocusFirstNavigableButton()
        {
            return FocusFirstRowButton();
        }

        public bool FocusLastRowButton()
        {
            return FocusRowButtonAtIndex(VisibleItems.Count - 1);
        }

        public bool FocusBottomButton()
        {
            if (VisibleItems.Count > 0)
            {
                return FocusLastRowButton();
            }

            FocusAddButton();
            return true;
        }

        public bool FocusLastNavigableButton()
        {
            if (VisibleItems.Count > 0)
            {
                return FocusLastRowButton();
            }

            if (BackRowButton.Visibility == Visibility.Visible)
            {
                return FocusBackRowButton();
            }

            return false;
        }

        public bool FocusTopButton()
        {
            if (BackRowButton.Visibility == Visibility.Visible)
            {
                return FocusBackRowButton();
            }

            if (VisibleItems.Count > 0)
            {
                return FocusRowButtonAtIndex(0);
            }

            FocusAddButton();
            return true;
        }

        private bool FocusBackRowButton()
        {
            if (BackRowButton.Focus())
            {
                return true;
            }

            Dispatcher.BeginInvoke(new Action(() =>
            {
                BackRowButton.Focus();
            }), System.Windows.Threading.DispatcherPriority.Loaded);

            Dispatcher.BeginInvoke(new Action(() =>
            {
                BackRowButton.Focus();
            }), System.Windows.Threading.DispatcherPriority.Input);

            return true;
        }

        public void AddNewItem()
        {
            if (AddItemCommand?.CanExecute(null) != true)
            {
                return;
            }

            int previousCount = ItemsSource?.Count ?? 0;
            AddItemCommand.Execute(null);
            if (ItemsSource == null || ItemsSource.Count <= previousCount)
            {
                return;
            }

            if (ItemsSource[ItemsSource.Count - 1] is IPairEntryEditable newItem)
            {
                newItem.ParentFolderId = _currentFolderId;
                newItem.SortOrder = VisibleItems.Count;
                RefreshVisibleItems();
                NormalizeCurrentFolderSortOrder();
                RefreshVisibleItems();
                ScrollToBottom();
                EnterEditMode(newItem, focusSecondColumn: false, selectAll: true);
            }
        }

        private static void OnSourceChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is AliasPairEditorControl control)
            {
                control.RefreshVisibleItems();
            }
        }

        private void AddItemButton_Click(object sender, RoutedEventArgs e)
        {
            AddNewItem();
        }

        private void AddFolderButton_Click(object sender, RoutedEventArgs e)
        {
            if (FolderItemsSource == null)
            {
                return;
            }

            var newFolder = new PairEntryFolder
            {
                Name = "新しいフォルダ",
                ParentFolderId = _currentFolderId,
                SortOrder = VisibleItems.Count
            };
            FolderItemsSource.Add(newFolder);
            RefreshVisibleItems();
            NormalizeCurrentFolderSortOrder();
            RefreshVisibleItems();
            ScrollToBottom();
            EnterEditMode(newFolder, focusSecondColumn: false, selectAll: true);
        }

        private void AddItemButton_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Home && FocusTopButton())
            {
                e.Handled = true;
                return;
            }

            if (e.Key == Key.End && FocusLastNavigableButton())
            {
                e.Handled = true;
                return;
            }

            if (e.Key == Key.Up && FocusLastNavigableButton())
            {
                e.Handled = true;
                return;
            }

            if (e.Key == Key.Down && FocusFirstNavigableButton())
            {
                e.Handled = true;
                return;
            }
        }

        private void AddFolderButton_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Home && FocusTopButton())
            {
                e.Handled = true;
                return;
            }

            if (e.Key == Key.End && FocusLastNavigableButton())
            {
                e.Handled = true;
                return;
            }

            if (e.Key == Key.Up && FocusLastNavigableButton())
            {
                e.Handled = true;
                return;
            }
        }

        private void BackRowButton_Click(object sender, RoutedEventArgs e)
        {
            NavigateUp();
        }

        private void BackRowButton_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Home && FocusTopButton())
            {
                e.Handled = true;
                return;
            }

            if (e.Key == Key.End && FocusLastNavigableButton())
            {
                e.Handled = true;
                return;
            }

            if (e.Key == Key.Enter || e.Key == Key.Left)
            {
                NavigateUp();
                e.Handled = true;
                return;
            }

            if (e.Key == Key.Down)
            {
                if (VisibleItems.Count > 0)
                {
                    FocusRowButtonAtIndex(0);
                }
                else
                {
                    FocusAddButton();
                }

                e.Handled = true;
                return;
            }

            if (e.Key == Key.Up)
            {
                FocusAddButton();
                e.Handled = true;
            }
        }

        private void RowButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button button || button.DataContext is not IPairEntryEditable item)
            {
                return;
            }

            if (item.IsFolder)
            {
                NavigateIntoFolder((PairEntryFolder)item);
                return;
            }

            EnterEditMode(item, focusSecondColumn: false, selectAll: false);
        }

        private void RowButton_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (sender is not Button button || button.DataContext is not IPairEntryEditable item)
            {
                return;
            }

            if (e.Key == Key.Home && FocusTopButton())
            {
                e.Handled = true;
                return;
            }

            if (e.Key == Key.End && FocusLastNavigableButton())
            {
                e.Handled = true;
                return;
            }

            if (Keyboard.Modifiers == ModifierKeys.Shift && (e.Key == Key.Up || e.Key == Key.Down))
            {
                MoveItem(item, moveUp: e.Key == Key.Up, focusSecondColumn: false, enterEditMode: false);
                e.Handled = true;
                return;
            }

            if (item.IsFolder && e.Key == Key.F2)
            {
                EnterEditMode(item, focusSecondColumn: false, selectAll: true);
                e.Handled = true;
                return;
            }

            if (item.IsFolder && e.Key == Key.Right)
            {
                NavigateIntoFolder((PairEntryFolder)item);
                e.Handled = true;
                return;
            }

            if (e.Key == Key.Left)
            {
                NavigateUp();
                e.Handled = true;
                return;
            }

            if (e.Key == Key.Enter)
            {
                if (item.IsFolder)
                {
                    NavigateIntoFolder((PairEntryFolder)item);
                }
                else
                {
                    EnterEditMode(item, focusSecondColumn: false, selectAll: true);
                }
                e.Handled = true;
                return;
            }

            if (e.Key == Key.Delete)
            {
                DeleteItem(item, preferSecondColumnAfterDelete: false);
                e.Handled = true;
                return;
            }

            if (e.Key == Key.Up)
            {
                int currentIndex = VisibleItems.IndexOf(item);
                if (currentIndex == 0 && BackRowButton.Visibility == Visibility.Visible)
                {
                    BackRowButton.Focus();
                }
                else
                {
                    FocusAdjacentRowButton(item, moveUp: true, fallbackToAddButton: true);
                }
                e.Handled = true;
                return;
            }

            if (e.Key == Key.Down)
            {
                FocusAdjacentRowButton(item, moveUp: false, fallbackToAddButton: true);
                e.Handled = true;
            }
        }

        private void DeleteInline_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button button && button.DataContext is IPairEntryEditable item)
            {
                DeleteItem(item, preferSecondColumnAfterDelete: !item.IsFolder);
            }
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

        private void EntryTextBox_LostKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
        {
            if (sender is not TextBox textBox || textBox.DataContext is not IPairEntryEditable item)
            {
                return;
            }

            DependencyObject? nextFocus = e.NewFocus as DependencyObject;
            if (IsFocusWithinSameItem(nextFocus, item))
            {
                return;
            }

            ExitEditMode(item, focusSameRowButton: false);
        }

        private void EntryTextBox_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (sender is not TextBox textBox || textBox.DataContext is not IPairEntryEditable item)
            {
                return;
            }

            if (!item.IsFolder && Keyboard.Modifiers == ModifierKeys.Shift && (e.Key == Key.Up || e.Key == Key.Down))
            {
                bool moveUp = e.Key == Key.Up;
                bool focusSecondColumn = Grid.GetColumn(textBox) == 2;
                MoveItem(item, moveUp, focusSecondColumn, enterEditMode: true);
                e.Handled = true;
                return;
            }

            if (e.Key == Key.Enter)
            {
                ExitEditMode(item, focusSameRowButton: true);
                e.Handled = true;
                return;
            }

            if (!item.IsFolder && e.Key == Key.Delete && string.IsNullOrEmpty(textBox.SelectedText) && string.IsNullOrEmpty(textBox.Text))
            {
                DeleteItem(item, preferSecondColumnAfterDelete: true);
                e.Handled = true;
                return;
            }

            if (e.Key == Key.Up)
            {
                FocusAdjacentRowButton(item, moveUp: true, fallbackToAddButton: false);
                e.Handled = true;
            }
            else if (e.Key == Key.Down)
            {
                FocusAdjacentRowButton(item, moveUp: false, fallbackToAddButton: false);
                e.Handled = true;
            }
            else if (!item.IsFolder && e.Key == Key.Left && textBox.CaretIndex == 0 && textBox.SelectionLength == 0)
            {
                textBox.MoveFocus(new TraversalRequest(FocusNavigationDirection.Left));
                e.Handled = true;
            }
            else if (!item.IsFolder && e.Key == Key.Right && textBox.CaretIndex == textBox.Text.Length && textBox.SelectionLength == 0)
            {
                textBox.MoveFocus(new TraversalRequest(FocusNavigationDirection.Right));
                e.Handled = true;
            }
            else if (e.Key == Key.Escape)
            {
                ExitEditMode(item, focusSameRowButton: true);
                e.Handled = true;
            }
        }

        private void MoveItem(IPairEntryEditable item, bool moveUp, bool focusSecondColumn, bool enterEditMode)
        {
            List<IPairEntryEditable> orderedItems = VisibleItems.ToList();
            int currentIndex = orderedItems.IndexOf(item);
            int targetIndex = moveUp ? currentIndex - 1 : currentIndex + 1;
            if (currentIndex < 0 || targetIndex < 0 || targetIndex >= orderedItems.Count)
            {
                return;
            }

            orderedItems.RemoveAt(currentIndex);
            orderedItems.Insert(targetIndex, item);
            NormalizeCurrentFolderSortOrder(orderedItems);
            RefreshVisibleItems();

            if (enterEditMode)
            {
                EnterEditMode(item, focusSecondColumn, selectAll: false);
                return;
            }

            ExitEditMode(item, focusSameRowButton: false);
            FocusRowButtonAtIndex(targetIndex);
        }

        private void DeleteItem(IPairEntryEditable item, bool preferSecondColumnAfterDelete)
        {
            int index = VisibleItems.IndexOf(item);
            if (index < 0)
            {
                return;
            }

            bool wasEditing = ReferenceEquals(CurrentEditingItem, item);

            if (item.IsFolder)
            {
                DeleteFolderRecursive((PairEntryFolder)item);
            }
            else
            {
                ItemsSource?.Remove(item);
            }

            if (wasEditing)
            {
                CurrentEditingItem = null;
            }

            RefreshVisibleItems();
            NormalizeCurrentFolderSortOrder();
            RefreshVisibleItems();

            if (VisibleItems.Count == 0)
            {
                FocusAddButton();
                return;
            }

            int targetIndex = index < VisibleItems.Count ? index : index - 1;
            if (wasEditing && preferSecondColumnAfterDelete && !VisibleItems[targetIndex].IsFolder)
            {
                EnterEditMode(VisibleItems[targetIndex], focusSecondColumn: true, selectAll: false);
                return;
            }

            FocusRowButtonAtIndex(targetIndex);
        }

        private void DeleteFolderRecursive(PairEntryFolder folder)
        {
            foreach (PairEntryFolder childFolder in GetFolders().Where(f => f.ParentFolderId == folder.Id).ToList())
            {
                DeleteFolderRecursive(childFolder);
            }

            foreach (IPairEntryEditable record in GetRecordItems().Where(i => i.ParentFolderId == folder.Id).ToList())
            {
                ItemsSource?.Remove(record);
            }

            FolderItemsSource?.Remove(folder);
        }

        private void EnterEditMode(IPairEntryEditable item, bool focusSecondColumn, bool selectAll)
        {
            CurrentEditingItem = item;
            RefreshVisibleItems();
            FocusEditorTextBox(item, item.IsFolder ? 0 : (focusSecondColumn ? 1 : 0), selectAll);
        }

        private void ExitEditMode(IPairEntryEditable item, bool focusSameRowButton)
        {
            if (!ReferenceEquals(CurrentEditingItem, item))
            {
                return;
            }

            int itemIndex = VisibleItems.IndexOf(item);
            CurrentEditingItem = null;
            RefreshVisibleItems();

            if (focusSameRowButton && itemIndex >= 0)
            {
                RestoreRowButtonFocus(itemIndex);
            }
        }

        private void RestoreRowButtonFocus(int index)
        {
            Dispatcher.BeginInvoke(new Action(() =>
            {
                PairItemsControl.UpdateLayout();
                if (TryFocusRowButtonAtIndex(index))
                {
                    return;
                }

                Dispatcher.BeginInvoke(new Action(() =>
                {
                    PairItemsControl.UpdateLayout();
                    TryFocusRowButtonAtIndex(index);
                }), System.Windows.Threading.DispatcherPriority.Input);
            }), System.Windows.Threading.DispatcherPriority.Loaded);
        }

        private void FocusAdjacentRowButton(IPairEntryEditable item, bool moveUp, bool fallbackToAddButton)
        {
            int currentIndex = VisibleItems.IndexOf(item);
            int targetIndex = moveUp ? currentIndex - 1 : currentIndex + 1;
            if (currentIndex < 0)
            {
                return;
            }

            ExitEditMode(item, focusSameRowButton: false);

            if (targetIndex < 0 || targetIndex >= VisibleItems.Count)
            {
                if (fallbackToAddButton)
                {
                    FocusAddButton();
                }
                return;
            }

            FocusRowButtonAtIndex(targetIndex);
        }

        private bool FocusRowButtonAtIndex(int index)
        {
            PairItemsControl.UpdateLayout();
            if (TryFocusRowButtonAtIndex(index))
            {
                return true;
            }

            Dispatcher.BeginInvoke(new Action(() =>
            {
                TryFocusRowButtonAtIndex(index);
            }), System.Windows.Threading.DispatcherPriority.Loaded);
            return false;
        }

        private bool TryFocusRowButtonAtIndex(int index)
        {
            if (index < 0 || index >= PairItemsControl.Items.Count)
            {
                return false;
            }

            if (PairItemsControl.ItemContainerGenerator.ContainerFromIndex(index) is not DependencyObject container)
            {
                return false;
            }

            Button? rowButton = FindNamedChild<Button>(container, "RowButton");
            return rowButton?.Focus() == true;
        }

        private bool FocusFolderButtonById(string folderId)
        {
            if (string.IsNullOrEmpty(folderId))
            {
                return false;
            }

            for (int i = 0; i < VisibleItems.Count; i++)
            {
                if (VisibleItems[i] is not PairEntryFolder folder || folder.Id != folderId)
                {
                    continue;
                }

                return FocusRowButtonAtIndex(i);
            }

            return false;
        }

        private void FocusEditorTextBox(IPairEntryEditable item, int targetIndex, bool selectAll)
        {
            Dispatcher.BeginInvoke(new Action(() =>
            {
                PairItemsControl.UpdateLayout();

                int index = VisibleItems.IndexOf(item);
                if (index < 0 || index >= PairItemsControl.Items.Count)
                {
                    return;
                }

                if (PairItemsControl.ItemContainerGenerator.ContainerFromIndex(index) is not DependencyObject container)
                {
                    return;
                }

                TextBox? targetTextBox = FindEditorTextBox(container, targetIndex);
                if (targetTextBox == null)
                {
                    return;
                }

                targetTextBox.Focus();
                if (selectAll)
                {
                    targetTextBox.SelectAll();
                }
                else
                {
                    targetTextBox.CaretIndex = targetTextBox.Text.Length;
                }
            }), System.Windows.Threading.DispatcherPriority.Loaded);
        }

        private TextBox? FindEditorTextBox(DependencyObject parent, int targetIndex)
        {
            int currentIndex = 0;
            return FindEditorTextBoxInternal(parent, targetIndex, ref currentIndex);
        }

        private TextBox? FindEditorTextBoxInternal(DependencyObject parent, int targetIndex, ref int currentIndex)
        {
            int childCount = VisualTreeHelper.GetChildrenCount(parent);
            for (int i = 0; i < childCount; i++)
            {
                DependencyObject child = VisualTreeHelper.GetChild(parent, i);
                if (child is FrameworkElement frameworkElement && frameworkElement.Visibility != Visibility.Visible)
                {
                    continue;
                }

                if (child is TextBox textBox)
                {
                    if (currentIndex == targetIndex)
                    {
                        return textBox;
                    }

                    currentIndex++;
                }

                TextBox? result = FindEditorTextBoxInternal(child, targetIndex, ref currentIndex);
                if (result != null)
                {
                    return result;
                }
            }

            return null;
        }

        private void NavigateIntoFolder(PairEntryFolder folder)
        {
            ExitCurrentEditWithoutFocusRestore();
            _folderIdToFocusAfterNavigateUp = string.Empty;
            _currentFolderId = folder.Id;
            RefreshVisibleItems();
            FocusFirstRowButton();
        }

        private void NavigateUp()
        {
            ExitCurrentEditWithoutFocusRestore();
            PairEntryFolder? currentFolder = GetCurrentFolder();
            _folderIdToFocusAfterNavigateUp = currentFolder?.Id ?? string.Empty;
            _currentFolderId = currentFolder?.ParentFolderId ?? string.Empty;
            RefreshVisibleItems();
            if (!FocusFolderButtonById(_folderIdToFocusAfterNavigateUp))
            {
                FocusFirstRowButton();
            }
            _folderIdToFocusAfterNavigateUp = string.Empty;
        }

        public void ResetToRoot()
        {
            ExitCurrentEditWithoutFocusRestore();
            _currentFolderId = string.Empty;
            RefreshVisibleItems();
        }

        private void ExitCurrentEditWithoutFocusRestore()
        {
            if (CurrentEditingItem is IPairEntryEditable item)
            {
                ExitEditMode(item, focusSameRowButton: false);
            }
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

            if (sender is FrameworkElement element && element.DataContext is IPairEntryEditable item)
            {
                DragDrop.DoDragDrop(element, new DataObject(ReorderDataFormat, item), DragDropEffects.Move);
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
            if (!e.Data.GetDataPresent(ReorderDataFormat) || sender is not FrameworkElement row || row.DataContext is not IPairEntryEditable targetItem)
            {
                return;
            }

            object? draggedItem = e.Data.GetData(ReorderDataFormat);
            if (draggedItem is not IPairEntryEditable dragged || ReferenceEquals(dragged, targetItem))
            {
                return;
            }

            List<IPairEntryEditable> orderedItems = VisibleItems.ToList();
            int sourceIndex = orderedItems.IndexOf(dragged);
            int targetIndex = orderedItems.IndexOf(targetItem);
            if (sourceIndex < 0 || targetIndex < 0 || sourceIndex == targetIndex)
            {
                return;
            }

            bool draggedWasEditing = ReferenceEquals(CurrentEditingItem, dragged);
            orderedItems.RemoveAt(sourceIndex);
            orderedItems.Insert(targetIndex, dragged);
            NormalizeCurrentFolderSortOrder(orderedItems);
            RefreshVisibleItems();

            if (draggedWasEditing)
            {
                EnterEditMode(dragged, focusSecondColumn: false, selectAll: false);
            }
            else
            {
                FocusRowButtonAtIndex(targetIndex);
            }

            e.Handled = true;
        }

        private void RefreshVisibleItems()
        {
            if (!FolderExists(_currentFolderId))
            {
                _currentFolderId = string.Empty;
            }

            List<IPairEntryEditable> currentItems = GetCurrentFolderItems().OrderBy(i => i.SortOrder).ToList();
            VisibleItems.Clear();
            foreach (IPairEntryEditable item in currentItems)
            {
                VisibleItems.Add(item);
            }

            PairItemsControl.ItemsSource = VisibleItems;
            PairItemsControl.Items.Refresh();
            PairItemsControl.UpdateLayout();
            UpdateFolderHeader();
        }

        private void NormalizeCurrentFolderSortOrder()
        {
            NormalizeCurrentFolderSortOrder(VisibleItems.ToList());
        }

        private void NormalizeCurrentFolderSortOrder(IList<IPairEntryEditable> orderedItems)
        {
            for (int i = 0; i < orderedItems.Count; i++)
            {
                orderedItems[i].SortOrder = i;
                orderedItems[i].ParentFolderId = _currentFolderId;
            }
        }

        private IEnumerable<PairEntryFolder> GetFolders()
        {
            return FolderItemsSource?.Cast<PairEntryFolder>() ?? Enumerable.Empty<PairEntryFolder>();
        }

        private IEnumerable<IPairEntryEditable> GetRecordItems()
        {
            return ItemsSource?.Cast<object>().OfType<IPairEntryEditable>() ?? Enumerable.Empty<IPairEntryEditable>();
        }

        private IEnumerable<IPairEntryEditable> GetCurrentFolderItems()
        {
            return GetFolders().Where(f => f.ParentFolderId == _currentFolderId).Cast<IPairEntryEditable>()
                .Concat(GetRecordItems().Where(i => i.ParentFolderId == _currentFolderId));
        }

        private PairEntryFolder? GetCurrentFolder()
        {
            return GetFolders().FirstOrDefault(f => f.Id == _currentFolderId);
        }

        private bool FolderExists(string folderId)
        {
            return string.IsNullOrEmpty(folderId) || GetFolders().Any(f => f.Id == folderId);
        }

        private void UpdateFolderHeader()
        {
            CurrentFolderTextBlock.Text = BuildCurrentPathText();
            BackRowButton.Visibility = string.IsNullOrEmpty(_currentFolderId) ? Visibility.Collapsed : Visibility.Visible;
        }

        private string BuildCurrentPathText()
        {
            var names = new List<string> { "ルート" };
            PairEntryFolder? folder = GetCurrentFolder();
            while (folder != null)
            {
                names.Insert(1, folder.Name);
                folder = string.IsNullOrEmpty(folder.ParentFolderId)
                    ? null
                    : GetFolders().FirstOrDefault(f => f.Id == folder.ParentFolderId);
            }

            return string.Join(" / ", names);
        }

        private void ScrollToBottom()
        {
            Dispatcher.BeginInvoke(new Action(() =>
            {
                ItemsScrollViewer.UpdateLayout();
                ItemsScrollViewer.ScrollToEnd();
            }), System.Windows.Threading.DispatcherPriority.Loaded);
        }

        private bool IsFocusWithinSameItem(DependencyObject? focusedElement, IPairEntryEditable item)
        {
            while (focusedElement != null)
            {
                if (focusedElement is FrameworkElement element && ReferenceEquals(element.DataContext, item))
                {
                    return true;
                }

                focusedElement = VisualTreeHelper.GetParent(focusedElement);
            }

            return false;
        }

        private T? FindNamedChild<T>(DependencyObject parent, string name) where T : FrameworkElement
        {
            int childCount = VisualTreeHelper.GetChildrenCount(parent);
            for (int i = 0; i < childCount; i++)
            {
                DependencyObject child = VisualTreeHelper.GetChild(parent, i);
                if (child is T typedChild && typedChild.Name == name)
                {
                    return typedChild;
                }

                T? result = FindNamedChild<T>(child, name);
                if (result != null)
                {
                    return result;
                }
            }

            return null;
        }
    }
}
