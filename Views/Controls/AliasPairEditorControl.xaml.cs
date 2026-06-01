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
        private bool _keepFocusOnTabs;
        private bool _focusTabWhenTargetTabIsEmpty;
        private IPairEditorTab? _editingTab;
        private string _editingTabOriginalName = string.Empty;
        private IPairEntryEditable? _itemToFocusAfterTabSwitch;

        public AliasPairEditorControl()
        {
            InitializeComponent();
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
            DependencyProperty.Register(nameof(FirstColumnWidth), typeof(GridLength), typeof(AliasPairEditorControl), new PropertyMetadata(new GridLength(100)));

        public static readonly DependencyProperty SecondColumnWidthProperty =
            DependencyProperty.Register(nameof(SecondColumnWidth), typeof(GridLength), typeof(AliasPairEditorControl), new PropertyMetadata(new GridLength(1, GridUnitType.Star)));

        public static readonly DependencyProperty DisplayValueOffsetProperty =
            DependencyProperty.Register(nameof(DisplayValueOffset), typeof(GridLength), typeof(AliasPairEditorControl), new PropertyMetadata(new GridLength(120)));

        public static readonly DependencyProperty CurrentEditingItemProperty =
            DependencyProperty.Register(nameof(CurrentEditingItem), typeof(object), typeof(AliasPairEditorControl), new PropertyMetadata(null));

        public static readonly DependencyProperty HeaderBottomContentProperty =
            DependencyProperty.Register(nameof(HeaderBottomContent), typeof(object), typeof(AliasPairEditorControl), new PropertyMetadata(null));

        public static readonly DependencyProperty ShowColumnHeadersProperty =
            DependencyProperty.Register(nameof(ShowColumnHeaders), typeof(bool), typeof(AliasPairEditorControl), new PropertyMetadata(true));

        public static readonly DependencyProperty TabHostProperty =
            DependencyProperty.Register(nameof(TabHost), typeof(IPairEditorTabHost), typeof(AliasPairEditorControl), new PropertyMetadata(null, OnTabHostChanged));

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

        public GridLength SecondColumnWidth
        {
            get => (GridLength)GetValue(SecondColumnWidthProperty);
            set => SetValue(SecondColumnWidthProperty, value);
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

        public IPairEditorTabHost? TabHost
        {
            get => (IPairEditorTabHost?)GetValue(TabHostProperty);
            set => SetValue(TabHostProperty, value);
        }

        public bool HasTabs => TabHost != null;
        public bool IsAddButtonFocused => Keyboard.FocusedElement == AddItemButton;

        private IPairEditorTab? SelectedTab
        {
            get => TabHost?.SelectedTab;
            set
            {
                if (TabHost != null)
                {
                    TabHost.SelectedTab = value;
                }
            }
        }

        private IList? ActiveItemsSource => SelectedTab?.Items ?? ItemsSource;
        private IList? ActiveFolderItemsSource => SelectedTab?.Folders ?? FolderItemsSource;

        public void FocusAddButton()
        {
            AddItemButton.Focus();
        }

        public bool FocusFirstRowButton()
        {
            return VisibleItems.Count > 0 && FocusRowButtonAtIndex(0);
        }

        public bool FocusFirstNavigableButton()
        {
            if (VisibleItems.Count > 0)
            {
                return FocusRowButtonAtIndex(0);
            }

            FocusAddButton();
            return true;
        }

        public bool FocusLastRowButton()
        {
            return FocusRowButtonAtIndex(VisibleItems.Count - 1);
        }

        public bool FocusRowButton(IPairEntryEditable item)
        {
            int index = VisibleItems.IndexOf(item);
            return index >= 0 && FocusRowButtonAtIndex(index);
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

            FocusAddButton();
            return true;
        }

        public bool FocusTopButton()
        {
            if (VisibleItems.Count > 0)
            {
                return FocusRowButtonAtIndex(0);
            }

            FocusAddButton();
            return true;
        }

        public bool IsFocusInItemEditor()
        {
            return Keyboard.FocusedElement is DependencyObject focusedElement && IsDescendantOf(focusedElement, this);
        }

        public bool ShouldFocusTabsOnUp()
        {
            return HasTabs &&
                   VisibleItems.Count == 0 &&
                   Keyboard.FocusedElement == AddItemButton;
        }

        public bool TryFocusTabsFromTopRow()
        {
            if (!HasTabs || Keyboard.FocusedElement is not DependencyObject focusedElement)
            {
                return false;
            }

            if (VisibleItems.Count == 0)
            {
                return FocusSelectedTab();
            }

            Button? rowButton = FindAncestor<Button>(focusedElement);
            if (rowButton?.DataContext is not IPairEntryEditable item)
            {
                return false;
            }

            return VisibleItems.FirstOrDefault() == item && FocusSelectedTab();
        }

        public bool FocusSelectedTab()
        {
            if (!HasTabs || TabsListBox == null)
            {
                return false;
            }

            if (TabsListBox.SelectedIndex < 0 && TabsListBox.Items.Count > 0)
            {
                TabsListBox.SelectedIndex = 0;
            }

            if (TabsListBox.SelectedIndex < 0)
            {
                return false;
            }

            if (TabsListBox.ItemContainerGenerator.ContainerFromIndex(TabsListBox.SelectedIndex) is ListBoxItem item)
            {
                return item.Focus();
            }

            Dispatcher.BeginInvoke(new Action(() =>
            {
                if (TabsListBox.ItemContainerGenerator.ContainerFromIndex(TabsListBox.SelectedIndex) is ListBoxItem generatedItem)
                {
                    generatedItem.Focus();
                }
            }), System.Windows.Threading.DispatcherPriority.Loaded);

            return true;
        }

        public bool TryHandleTabKeyboardShortcut(KeyEventArgs e)
        {
            if (!HasTabs || Keyboard.FocusedElement is not DependencyObject focusedElement)
            {
                return false;
            }

            if (IsTabFocus(focusedElement) || ReferenceEquals(focusedElement, AddTabButton))
            {
                return false;
            }

            if (!IsDescendantOf(focusedElement, this))
            {
                return false;
            }

            if (Keyboard.Modifiers == ModifierKeys.Shift &&
                (e.Key == Key.Left || e.Key == Key.Right) &&
                TryMoveFocusedItemToAdjacentTab(moveRight: e.Key == Key.Right))
            {
                e.Handled = true;
                return true;
            }

            if (Keyboard.Modifiers == ModifierKeys.None && (e.Key == Key.Left || e.Key == Key.Right))
            {
                if (MoveTabSelection(moveRight: e.Key == Key.Right, keepTabFocus: false))
                {
                    e.Handled = true;
                    return true;
                }
            }

            return false;
        }

        public void AddNewItem()
        {
            if (SelectedTab != null)
            {
                IPairEntryEditable newItem = SelectedTab.CreateNewItem();
                SelectedTab.Items.Add(newItem);
                newItem.ParentFolderId = string.Empty;
                newItem.SortOrder = VisibleItems.Count;
                RefreshVisibleItems();
                NormalizeSortOrder();
                RefreshVisibleItems();
                ScrollToBottom();
                EnterEditMode(newItem, focusSecondColumn: false, selectAll: true);
                return;
            }

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

            if (ItemsSource[ItemsSource.Count - 1] is IPairEntryEditable addedItem)
            {
                addedItem.ParentFolderId = string.Empty;
                addedItem.SortOrder = VisibleItems.Count;
                RefreshVisibleItems();
                NormalizeSortOrder();
                RefreshVisibleItems();
                ScrollToBottom();
                EnterEditMode(addedItem, focusSecondColumn: false, selectAll: true);
            }
        }

        public void ResetToRoot()
        {
            ExitCurrentEditWithoutFocusRestore();
            RefreshVisibleItems();
        }

        private static void OnSourceChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is AliasPairEditorControl control)
            {
                control.RefreshVisibleItems();
            }
        }

        private static void OnTabHostChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is AliasPairEditorControl control)
            {
                control.InitializeTabHost();
            }
        }

        private void InitializeTabHost()
        {
            EndTabRename(commit: true);
            if (TabHost != null && TabHost.SelectedTab == null)
            {
                TabHost.SelectedTab = TabHost.Tabs.Cast<object>().OfType<IPairEditorTab>().FirstOrDefault();
            }

            SyncSelectedTabToList();
            RefreshVisibleItems();
        }

        private void AddItemButton_Click(object sender, RoutedEventArgs e)
        {
            AddNewItem();
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

            if (e.Key == Key.Up)
            {
                if (ShouldFocusTabsOnUp())
                {
                    FocusSelectedTab();
                }
                else
                {
                    FocusLastNavigableButton();
                }

                e.Handled = true;
                return;
            }

            if (e.Key == Key.Down && FocusFirstNavigableButton())
            {
                e.Handled = true;
                return;
            }
        }

        private void RowButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button { DataContext: IPairEntryEditable item })
            {
                EnterEditMode(item, focusSecondColumn: false, selectAll: false);
            }
        }

        private void RowButton_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (sender is not Button { DataContext: IPairEntryEditable item })
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

            if (e.Key == Key.F2 || e.Key == Key.Enter)
            {
                EnterEditMode(item, focusSecondColumn: false, selectAll: true);
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
                if (TryFocusTabsFromTopRow())
                {
                    e.Handled = true;
                    return;
                }

                FocusAdjacentRowButton(item, moveUp: true, fallbackToAddButton: true);
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
            if (sender is Button { DataContext: IPairEntryEditable item })
            {
                DeleteItem(item, preferSecondColumnAfterDelete: true);
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

            if (Keyboard.Modifiers == ModifierKeys.Shift && (e.Key == Key.Up || e.Key == Key.Down))
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

            if (e.Key == Key.Delete && string.IsNullOrEmpty(textBox.SelectedText) && string.IsNullOrEmpty(textBox.Text))
            {
                DeleteItem(item, preferSecondColumnAfterDelete: true);
                e.Handled = true;
                return;
            }

            if (e.Key == Key.Up)
            {
                if (TryFocusTabsFromTopRow())
                {
                    e.Handled = true;
                    return;
                }

                FocusAdjacentRowButton(item, moveUp: true, fallbackToAddButton: false);
                e.Handled = true;
            }
            else if (e.Key == Key.Down)
            {
                FocusAdjacentRowButton(item, moveUp: false, fallbackToAddButton: false);
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
            else if (e.Key == Key.Escape)
            {
                ExitEditMode(item, focusSameRowButton: true);
                e.Handled = true;
            }
        }

        private void TabsListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (e.Source != TabsListBox)
            {
                return;
            }

            SelectedTab = TabsListBox.SelectedItem as IPairEditorTab;
            RefreshVisibleItems();

            if (_keepFocusOnTabs)
            {
                _keepFocusOnTabs = false;
                FocusSelectedTab();
                return;
            }

            if (_itemToFocusAfterTabSwitch != null)
            {
                IPairEntryEditable targetItem = _itemToFocusAfterTabSwitch;
                _itemToFocusAfterTabSwitch = null;

                Dispatcher.BeginInvoke(new Action(() =>
                {
                    if (!FocusRowButton(targetItem))
                    {
                        FocusFirstNavigableButton();
                    }
                }), System.Windows.Threading.DispatcherPriority.Loaded);
                return;
            }

            if (_focusTabWhenTargetTabIsEmpty)
            {
                _focusTabWhenTargetTabIsEmpty = false;
                if (VisibleItems.Count == 0)
                {
                    FocusSelectedTab();
                    return;
                }
            }

            FocusFirstNavigableButton();
        }

        private void TabsListBox_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (TabsListBox == null || Keyboard.FocusedElement is not ListBoxItem focusedItem)
            {
                return;
            }

            int currentIndex = TabsListBox.ItemContainerGenerator.IndexFromContainer(focusedItem);
            if (currentIndex < 0)
            {
                return;
            }

            if (e.Key == Key.Right)
            {
                MoveTabSelection(moveRight: true, keepTabFocus: true);
                e.Handled = true;
                return;
            }

            if (e.Key == Key.Left)
            {
                MoveTabSelection(moveRight: false, keepTabFocus: true);
                e.Handled = true;
                return;
            }

            if (e.Key == Key.Home && FocusTopButton())
            {
                e.Handled = true;
                return;
            }

            if (e.Key == Key.End && FocusBottomButton())
            {
                e.Handled = true;
                return;
            }

            if (e.Key == Key.Delete)
            {
                DeleteSelectedTab();
                e.Handled = true;
                return;
            }

            if (e.Key == Key.F2 && TabsListBox.SelectedItem is IPairEditorTab selectedTab)
            {
                StartTabRename(selectedTab);
                e.Handled = true;
                return;
            }

            if (e.Key == Key.Down)
            {
                FocusFirstNavigableButton();
                e.Handled = true;
            }
        }

        private void AddTabButton_Click(object sender, RoutedEventArgs e)
        {
            if (TabHost == null)
            {
                return;
            }

            TabHost.CreateNewTab();
            SyncSelectedTabToList();
            _keepFocusOnTabs = true;
            RefreshVisibleItems();
            FocusSelectedTab();
        }

        private void AddTabButton_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (TabsListBox != null && e.Key == Key.Left && TabsListBox.Items.Count > 0)
            {
                TabsListBox.SelectedIndex = TabsListBox.Items.Count - 1;
                FocusSelectedTab();
                e.Handled = true;
                return;
            }

            if (e.Key == Key.Down)
            {
                FocusFirstNavigableButton();
                e.Handled = true;
            }
        }

        private void TabNameTextBox_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (sender is not TextBox textBox || textBox.DataContext is not IPairEditorTab tab)
            {
                return;
            }

            if (e.Key == Key.Enter)
            {
                EndTabRename(commit: true);
                FocusTab(tab);
                e.Handled = true;
                return;
            }

            if (e.Key == Key.Escape)
            {
                EndTabRename(commit: false);
                FocusTab(tab);
                e.Handled = true;
                return;
            }

            if (e.Key == Key.Up)
            {
                EndTabRename(commit: true);
                FocusTab(tab);
                e.Handled = true;
                return;
            }

            if (e.Key == Key.Down)
            {
                EndTabRename(commit: true);
                FocusFirstNavigableButton();
                e.Handled = true;
            }
        }

        private void TabNameTextBox_LostKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
        {
            if (sender is TextBox { DataContext: IPairEditorTab tab } && ReferenceEquals(_editingTab, tab))
            {
                EndTabRename(commit: true);
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
            NormalizeSortOrder(orderedItems);
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
            ActiveItemsSource?.Remove(item);

            if (wasEditing)
            {
                CurrentEditingItem = null;
            }

            RefreshVisibleItems();
            NormalizeSortOrder();
            RefreshVisibleItems();

            if (VisibleItems.Count == 0)
            {
                FocusAddButton();
                return;
            }

            int targetIndex = index < VisibleItems.Count ? index : index - 1;
            if (wasEditing && preferSecondColumnAfterDelete)
            {
                EnterEditMode(VisibleItems[targetIndex], focusSecondColumn: true, selectAll: false);
                return;
            }

            FocusRowButtonAtIndex(targetIndex);
        }

        private void EnterEditMode(IPairEntryEditable item, bool focusSecondColumn, bool selectAll)
        {
            CurrentEditingItem = item;
            RefreshVisibleItems();
            FocusEditorTextBox(item, focusSecondColumn ? 1 : 0, selectAll);
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
            NormalizeSortOrder(orderedItems);
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
            FlattenFolderStructureIfNeeded();

            List<IPairEntryEditable> currentItems = GetRecordItems()
                .OrderBy(i => i.SortOrder)
                .ToList();

            VisibleItems.Clear();
            foreach (IPairEntryEditable item in currentItems)
            {
                VisibleItems.Add(item);
            }

            PairItemsControl.ItemsSource = VisibleItems;
            PairItemsControl.Items.Refresh();
            PairItemsControl.UpdateLayout();
        }

        private void FlattenFolderStructureIfNeeded()
        {
            List<IPairEntryEditable> records = GetRecordItems().ToList();
            List<PairEntryFolder> folders = GetFolders().ToList();
            if (records.Count == 0)
            {
                ClearFolders();
                return;
            }

            if (folders.Count == 0 && records.All(item => string.IsNullOrEmpty(item.ParentFolderId)))
            {
                return;
            }

            List<IPairEntryEditable> flattened = FlattenItems(records, folders);
            NormalizeSortOrder(flattened);
            ClearFolders();
        }

        private List<IPairEntryEditable> FlattenItems(IReadOnlyCollection<IPairEntryEditable> records, IReadOnlyCollection<PairEntryFolder> folders)
        {
            var flattened = new List<IPairEntryEditable>();
            AppendFlattenedItems(string.Empty, records, folders, flattened);

            foreach (IPairEntryEditable record in records)
            {
                if (!flattened.Contains(record))
                {
                    flattened.Add(record);
                }
            }

            return flattened;
        }

        private void AppendFlattenedItems(
            string parentFolderId,
            IReadOnlyCollection<IPairEntryEditable> records,
            IReadOnlyCollection<PairEntryFolder> folders,
            ICollection<IPairEntryEditable> flattened)
        {
            var childFolders = folders
                .Where(folder => folder.ParentFolderId == parentFolderId)
                .Cast<IPairEntryEditable>();
            var childRecords = records
                .Where(record => record.ParentFolderId == parentFolderId);

            foreach (IPairEntryEditable item in childFolders.Concat(childRecords)
                         .OrderBy(item => item.SortOrder)
                         .ThenBy(item => item.IsFolder ? 0 : 1))
            {
                if (item is PairEntryFolder folder)
                {
                    AppendFlattenedItems(folder.Id, records, folders, flattened);
                    continue;
                }

                flattened.Add(item);
            }
        }

        private void NormalizeSortOrder()
        {
            NormalizeSortOrder(VisibleItems.ToList());
        }

        private void NormalizeSortOrder(IList<IPairEntryEditable> orderedItems)
        {
            for (int i = 0; i < orderedItems.Count; i++)
            {
                orderedItems[i].SortOrder = i;
                orderedItems[i].ParentFolderId = string.Empty;
            }
        }

        private IEnumerable<PairEntryFolder> GetFolders()
        {
            return ActiveFolderItemsSource?.Cast<PairEntryFolder>() ?? Enumerable.Empty<PairEntryFolder>();
        }

        private IEnumerable<IPairEntryEditable> GetRecordItems()
        {
            return ActiveItemsSource?.Cast<object>().OfType<IPairEntryEditable>() ?? Enumerable.Empty<IPairEntryEditable>();
        }

        private void ClearFolders()
        {
            ActiveFolderItemsSource?.Clear();
        }

        private void ScrollToBottom()
        {
            Dispatcher.BeginInvoke(new Action(() =>
            {
                ItemsScrollViewer.UpdateLayout();
                ItemsScrollViewer.ScrollToEnd();
            }), System.Windows.Threading.DispatcherPriority.Loaded);
        }

        private void SyncSelectedTabToList()
        {
            if (TabsListBox == null)
            {
                return;
            }

            TabsListBox.Items.Refresh();
            TabsListBox.SelectedItem = SelectedTab;
        }

        private bool MoveTabSelection(bool moveRight, bool keepTabFocus)
        {
            if (TabsListBox == null || TabsListBox.Items.Count == 0)
            {
                return false;
            }

            int currentIndex = TabsListBox.SelectedIndex >= 0 ? TabsListBox.SelectedIndex : 0;
            int targetIndex = moveRight ? currentIndex + 1 : currentIndex - 1;

            if (targetIndex < 0)
            {
                targetIndex = 0;
            }

            if (targetIndex >= TabsListBox.Items.Count)
            {
                if (moveRight && AddTabButton.Visibility == Visibility.Visible)
                {
                    AddTabButton.Focus();
                    return true;
                }

                targetIndex = TabsListBox.Items.Count - 1;
            }

            _focusTabWhenTargetTabIsEmpty = !keepTabFocus;
            _keepFocusOnTabs = keepTabFocus;
            TabsListBox.SelectedIndex = targetIndex;
            if (keepTabFocus)
            {
                FocusSelectedTab();
            }

            return true;
        }

        private bool TryMoveFocusedItemToAdjacentTab(bool moveRight)
        {
            if (SelectedTab == null || TabHost == null || TabsListBox == null)
            {
                return false;
            }

            if (Keyboard.FocusedElement is not DependencyObject focusedElement)
            {
                return false;
            }

            Button? rowButton = FindAncestor<Button>(focusedElement);
            if (rowButton?.DataContext is not IPairEntryEditable entry)
            {
                return false;
            }

            List<IPairEditorTab> tabs = TabHost.Tabs.Cast<object>().OfType<IPairEditorTab>().ToList();
            int currentTabIndex = tabs.IndexOf(SelectedTab);
            if (currentTabIndex < 0)
            {
                return false;
            }

            int targetTabIndex = moveRight ? currentTabIndex + 1 : currentTabIndex - 1;
            if (targetTabIndex < 0 || targetTabIndex >= tabs.Count)
            {
                return false;
            }

            IPairEditorTab sourceTab = tabs[currentTabIndex];
            IPairEditorTab targetTab = tabs[targetTabIndex];
            int entryIndex = sourceTab.Items.IndexOf(entry);
            if (entryIndex < 0)
            {
                return false;
            }

            sourceTab.Items.RemoveAt(entryIndex);
            NormalizeSortOrder(sourceTab.Items.Cast<object>().OfType<IPairEntryEditable>().ToList());

            entry.SortOrder = targetTab.Items.Count;
            entry.ParentFolderId = string.Empty;
            targetTab.Items.Add(entry);
            NormalizeSortOrder(targetTab.Items.Cast<object>().OfType<IPairEntryEditable>().ToList());

            _itemToFocusAfterTabSwitch = entry;
            SelectedTab = targetTab;
            SyncSelectedTabToList();
            RefreshVisibleItems();
            return true;
        }

        private void DeleteSelectedTab()
        {
            if (SelectedTab == null || TabHost == null)
            {
                return;
            }

            var dialog = new DeleteConfirmationWindow(SelectedTab.Name)
            {
                Owner = Window.GetWindow(this)
            };
            if (dialog.ShowDialog() != true)
            {
                FocusSelectedTab();
                return;
            }

            TabHost.DeleteTab(SelectedTab);
            EndTabRename(commit: true);
            SyncSelectedTabToList();
            _keepFocusOnTabs = true;
            RefreshVisibleItems();
            FocusSelectedTab();
        }

        private void StartTabRename(IPairEditorTab tab)
        {
            EndTabRename(commit: true);
            _editingTab = tab;
            _editingTabOriginalName = tab.Name;
            tab.IsEditing = true;

            Dispatcher.BeginInvoke(new Action(() =>
            {
                if (FindTabTextBox(tab) is TextBox textBox)
                {
                    textBox.Focus();
                    textBox.SelectAll();
                }
            }), System.Windows.Threading.DispatcherPriority.Loaded);
        }

        private void EndTabRename(bool commit)
        {
            if (_editingTab == null)
            {
                return;
            }

            if (!commit)
            {
                _editingTab.Name = _editingTabOriginalName;
            }
            else
            {
                _editingTab.Name = string.IsNullOrWhiteSpace(_editingTab.Name)
                    ? _editingTabOriginalName
                    : _editingTab.Name.Trim();
            }

            _editingTab.IsEditing = false;
            _editingTab = null;
            _editingTabOriginalName = string.Empty;
            TabsListBox?.Items.Refresh();
        }

        private TextBox? FindTabTextBox(IPairEditorTab tab)
        {
            if (TabsListBox == null)
            {
                return null;
            }

            int index = TabsListBox.Items.IndexOf(tab);
            if (index < 0 || TabsListBox.ItemContainerGenerator.ContainerFromIndex(index) is not DependencyObject container)
            {
                return null;
            }

            return FindNamedChild<TextBox>(container, null);
        }

        private void FocusTab(IPairEditorTab tab)
        {
            if (TabsListBox == null)
            {
                return;
            }

            int index = TabsListBox.Items.IndexOf(tab);
            if (index < 0)
            {
                return;
            }

            TabsListBox.SelectedIndex = index;
            _keepFocusOnTabs = true;
            FocusSelectedTab();
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

        private bool IsTabFocus(DependencyObject focusedElement)
        {
            return TabsListBox != null && IsDescendantOf(focusedElement, TabsListBox);
        }

        private static bool IsDescendantOf(DependencyObject target, DependencyObject ancestor)
        {
            DependencyObject? current = target;
            while (current != null)
            {
                if (ReferenceEquals(current, ancestor))
                {
                    return true;
                }

                current = VisualTreeHelper.GetParent(current);
            }

            return false;
        }

        private T? FindNamedChild<T>(DependencyObject parent, string? name) where T : FrameworkElement
        {
            int childCount = VisualTreeHelper.GetChildrenCount(parent);
            for (int i = 0; i < childCount; i++)
            {
                DependencyObject child = VisualTreeHelper.GetChild(parent, i);
                if (child is T typedChild && (name == null || typedChild.Name == name))
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

        private static T? FindAncestor<T>(DependencyObject? child) where T : DependencyObject
        {
            DependencyObject? current = child;
            while (current != null)
            {
                if (current is T typed)
                {
                    return typed;
                }

                current = VisualTreeHelper.GetParent(current);
            }

            return null;
        }
    }
}
