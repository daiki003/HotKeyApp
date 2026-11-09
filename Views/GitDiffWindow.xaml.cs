using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using HotKeyCommandApp.Services;
using HotKeyCommandApp.Views.Controls;

namespace HotKeyCommandApp.Views
{
    public partial class GitDiffWindow : Window
    {
        private const string TabReorderDataFormat = "HotKeyCommandApp.GitDiffRepositoryTab";
        private Point _tabDragStartPoint;
        private TabItem _draggedTab;
        private GitDiffRepositoryView ActiveView => (RepositoryTabs.SelectedItem as TabItem)?.Content as GitDiffRepositoryView;
        private IEnumerable<GitDiffRepositoryView> RepositoryViews =>
            RepositoryTabs.Items.Cast<TabItem>().Select(tab => (GitDiffRepositoryView)tab.Content);

        public static void ShowForRepository(string repositoryPath, double movementSpeed = 1200.0)
        {
            var window = Application.Current.Windows.OfType<GitDiffWindow>().FirstOrDefault();
            if (window == null)
            {
                window = new GitDiffWindow(repositoryPath, movementSpeed);
                window.Show();
            }
            else
            {
                window.SelectRepository(repositoryPath, movementSpeed);
            }
            window.FocusWindow();
        }

        public static void FocusExisting()
        {
            Application.Current.Windows.OfType<GitDiffWindow>().FirstOrDefault()?.FocusWindow();
        }

        public GitDiffWindow(string repositoryPath, double movementSpeed = 1200.0)
        {
            InitializeComponent();
            SelectRepository(repositoryPath, movementSpeed);
            Closed += (_, _) =>
            {
                foreach (var view in RepositoryViews) view.Dispose();
            };
        }

        private void SelectRepository(string repositoryPath, double movementSpeed)
        {
            string fullPath = Path.TrimEndingDirectorySeparator(Path.GetFullPath(repositoryPath));
            var tab = RepositoryTabs.Items.Cast<TabItem>().FirstOrDefault(item =>
                string.Equals(((GitDiffRepositoryView)item.Content).RepositoryPath, fullPath, StringComparison.OrdinalIgnoreCase));
            if (tab == null)
            {
                var view = new GitDiffRepositoryView(fullPath, movementSpeed);
                tab = new TabItem { Header = view.ViewModel, Content = view, ToolTip = fullPath };
                RepositoryTabs.Items.Add(tab);
            }
            RepositoryTabs.SelectedItem = tab;
        }

        private void FocusWindow()
        {
            if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
            var target = OwnedWindows.Cast<Window>().LastOrDefault(window => window.IsVisible) ?? this;
            target.Activate();
            target.Focus();
            if (target == this)
            {
                Dispatcher.BeginInvoke(() =>
                {
                    if (IsActive) ActiveView?.FocusFileList();
                }, System.Windows.Threading.DispatcherPriority.Input);
            }
        }

        private void RepositoryTabs_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (e.Source != RepositoryTabs) return;
            DataContext = ActiveView?.ViewModel;
        }

        private void CloseTabButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button { Tag: TabItem tab }) CloseTab(tab);
            e.Handled = true;
        }

        private void CloseTab(TabItem tab)
        {
            if (tab.Content is not GitDiffRepositoryView view || view.ViewModel.IsOperating) return;
            if (RepositoryTabs.Items.Count == 1) { Close(); return; }
            RepositoryTabs.Items.Remove(tab);
            view.Dispose();
        }

        private void TabHeader_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            _draggedTab = null;
            for (var element = e.OriginalSource as DependencyObject; element != null && element != sender;
                 element = VisualTreeHelper.GetParent(element))
            {
                if (element is ButtonBase) return;
            }
            if (sender is FrameworkElement { Tag: TabItem tab })
            {
                _tabDragStartPoint = e.GetPosition(this);
                _draggedTab = tab;
            }
        }

        private void TabHeader_PreviewMouseMove(object sender, MouseEventArgs e)
        {
            if (e.LeftButton != MouseButtonState.Pressed) { _draggedTab = null; return; }
            if (_draggedTab == null || sender is not FrameworkElement header) return;
            Vector offset = e.GetPosition(this) - _tabDragStartPoint;
            if (Math.Abs(offset.X) < SystemParameters.MinimumHorizontalDragDistance &&
                Math.Abs(offset.Y) < SystemParameters.MinimumVerticalDragDistance) return;

            var tab = _draggedTab;
            _draggedTab = null;
            DragDrop.DoDragDrop(header, new DataObject(TabReorderDataFormat, tab), DragDropEffects.Move);
            e.Handled = true;
        }

        private void TabHeader_DragOver(object sender, DragEventArgs e)
        {
            if (!e.Data.GetDataPresent(TabReorderDataFormat)) return;
            e.Effects = e.Data.GetData(TabReorderDataFormat) is TabItem tab && RepositoryTabs.Items.Contains(tab)
                ? DragDropEffects.Move : DragDropEffects.None;
            e.Handled = true;
        }

        private void TabHeader_Drop(object sender, DragEventArgs e)
        {
            if (!e.Data.GetDataPresent(TabReorderDataFormat) ||
                e.Data.GetData(TabReorderDataFormat) is not TabItem source ||
                sender is not FrameworkElement { Tag: TabItem target } header) return;

            int sourceIndex = RepositoryTabs.Items.IndexOf(source);
            int targetIndex = RepositoryTabs.Items.IndexOf(target);
            if (sourceIndex < 0 || targetIndex < 0) return;
            int insertionIndex = targetIndex + (e.GetPosition(header).X >= header.ActualWidth / 2 ? 1 : 0);
            if (sourceIndex < insertionIndex) insertionIndex--;
            RepositoryTabs.MoveTab(source, insertionIndex);
            e.Effects = DragDropEffects.Move;
            e.Handled = true;
        }

        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);
            WindowHelper.DisableSystemMenu(this);
            WindowHelper.EnableWindowMoveShortcut(this);
        }

        protected override void OnClosing(CancelEventArgs e)
        {
            if (RepositoryViews.Any(view => view.ViewModel.IsOperating)) { e.Cancel = true; return; }
            base.OnClosing(e);
        }

        private async void Window_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (Keyboard.Modifiers == ModifierKeys.Control)
            {
                if (e.Key is Key.PageUp or Key.PageDown)
                {
                    RepositoryTabs.MoveSelection(e.Key == Key.PageDown ? 1 : -1);
                    ActiveView?.FocusFileList();
                    e.Handled = true;
                    return;
                }
                if (e.Key == Key.W && RepositoryTabs.SelectedItem is TabItem tab)
                {
                    CloseTab(tab);
                    e.Handled = true;
                    return;
                }
            }
            if (e.Key == Key.F5)
            {
                e.Handled = true;
                if (ActiveView != null) await ActiveView.RefreshAsync();
                return;
            }
            if (ActiveView?.IsDiffTextFocused == true &&
                Keyboard.Modifiers.HasFlag(ModifierKeys.Control) && e.Key is Key.Left or Key.Right or Key.Up or Key.Down)
                return;
            if (Keyboard.Modifiers == (ModifierKeys.Control | ModifierKeys.Shift))
            {
                if (e.Key is Key.PageUp or Key.PageDown)
                {
                    RepositoryTabs.MoveSelectedTab(e.Key == Key.PageDown ? 1 : -1);
                    ActiveView?.FocusFileList();
                    e.Handled = true;
                    return;
                }
                const double step = 10.0;
                if (e.Key == Key.Left) { Width = Math.Clamp(Width - step, MinWidth, MaxWidth); e.Handled = true; }
                else if (e.Key == Key.Right) { Width = Math.Clamp(Width + step, MinWidth, MaxWidth); e.Handled = true; }
                else if (e.Key == Key.Up) { Height = Math.Clamp(Height - step, MinHeight, MaxHeight); e.Handled = true; }
                else if (e.Key == Key.Down) { Height = Math.Clamp(Height + step, MinHeight, MaxHeight); e.Handled = true; }
                return;
            }
            if (Keyboard.Modifiers == ModifierKeys.Control && e.Key is Key.Left or Key.Right or Key.Up or Key.Down)
            {
                if (!e.IsRepeat)
                {
                    // 描画前にキーを離す単押しでも、ウィンドウを確実に移動する。
                    double step = (ActiveView?.ViewModel.MovementSpeed ?? 1200.0) / 60.0;
                    Left += e.Key == Key.Left ? -step : e.Key == Key.Right ? step : 0;
                    Top += e.Key == Key.Up ? -step : e.Key == Key.Down ? step : 0;
                }
                e.Handled = true;
            }
        }
    }
}
