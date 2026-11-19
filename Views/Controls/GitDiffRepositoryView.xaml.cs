using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using HotKeyCommandApp.Models;
using HotKeyCommandApp.Services;
using HotKeyCommandApp.ViewModels;
using HotKeyCommandApp.Views;

namespace HotKeyCommandApp.Views.Controls
{
    public partial class GitDiffRepositoryView : UserControl, IDisposable
    {
        private readonly GitDiffViewModel _viewModel;
        private readonly Func<string, string, int?, Task> _openFile;
        private bool _initialized;
        private bool _isRenameDialogOpen;
        private bool _isModalDialogOpen;
        private bool _activationRefreshPending;
        private bool _activationRefreshRunning;
        private bool _disposed;
        private int _inputVersion;
        private int _diffFontSize = 12;
        private Point _fileDragStart;
        private GitDiffFile _draggedFile;
        private bool _copyDiffSelection;
        private IInputElement _lastNavigationFocus;
        private IInputElement _searchReturnFocus;
        private const string FileDragFormat = "HotKeyCommandApp.GitDiffFile";

        public GitDiffRepositoryView(string repositoryPath, double movementSpeed, Func<string, string, int?, Task> openFile = null)
        {
            RepositoryPath = Path.TrimEndingDirectorySeparator(Path.GetFullPath(repositoryPath));
            _openFile = openFile ?? VsCodeFileOpener.OpenAsync;
            InitializeComponent();
            _viewModel = new GitDiffViewModel(RepositoryPath, movementSpeed);
            DataContext = _viewModel;
            DiffLines.LineOpenRequested += line => _ = OpenFileAsync(_viewModel.SelectedFile, line);
            DiffLines.TextMouseInteraction += (_, _) => _copyDiffSelection = true;
            Keyboard.AddGotKeyboardFocusHandler(this, (_, e) =>
            {
                _copyDiffSelection = false;
                if (ReferenceEquals(e.NewFocus, DiffSearchBox)) _searchReturnFocus = _lastNavigationFocus;
                else if (e.NewFocus is ButtonBase or ListBoxItem) _lastNavigationFocus = e.NewFocus;
            });
            _viewModel.SearchMatchRequested += result => DiffLines.SelectSearchMatch(result);
            _viewModel.PropertyChanged += (_, args) =>
            {
                if (args.PropertyName == nameof(GitDiffViewModel.Lines)) DiffLines.SetLines(_viewModel.Lines, _viewModel.SelectedFile, RepositoryPath);
                if (args.PropertyName == nameof(GitDiffViewModel.SearchText)) DiffLines.SetSearchText(_viewModel.SearchText);
                if (args.PropertyName == nameof(GitDiffViewModel.SelectedSearchResult) && _viewModel.SelectedSearchResult == null) DiffLines.ClearSearchMatch();
                if (args.PropertyName == nameof(GitDiffViewModel.IsBusy) && !_viewModel.IsBusy) ProcessActivationRefresh();
            };
            Loaded += async (_, _) =>
            {
                if (_initialized) return;
                _initialized = true;
                await RefreshAsync();
            };
        }

        public string RepositoryPath { get; }
        public GitDiffViewModel ViewModel => _viewModel;
        public bool IsDiffTextFocused => DiffLines.IsKeyboardFocusWithin || SelectedFilePathBox.IsKeyboardFocusWithin;
        public bool IsModalDialogOpen => _isModalDialogOpen;
        public bool IsRefreshReady => _initialized && !_disposed;

        private void SidebarPanel_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            if (SidebarActions == null || SidebarHeader == null) return;
            // 小さいウィンドウでもファイル一覧の領域を残し、下部の操作はスクロールで利用できるようにする。
            SidebarActions.MaxHeight = Math.Max(40, (e.NewSize.Height - SidebarHeader.ActualHeight - SidebarHeader.Margin.Top - SidebarHeader.Margin.Bottom) / 2);
        }

        public void RequestRefreshOnActivation()
        {
            if (!_initialized || _disposed || _isModalDialogOpen) return;
            _activationRefreshPending = true;
            ProcessActivationRefresh();
        }

        public async void ProcessActivationRefresh()
        {
            if (!_activationRefreshPending || _activationRefreshRunning || _disposed || _viewModel.IsBusy ||
                _isModalDialogOpen || !IsVisible || Window.GetWindow(this)?.IsActive != true) return;
            _activationRefreshPending = false;
            _activationRefreshRunning = true;
            try { await _viewModel.RefreshAsync(); }
            finally
            {
                _activationRefreshRunning = false;
                if (_activationRefreshPending) ProcessActivationRefresh();
            }
        }

        private T ShowModal<T>(Func<T> show)
        {
            _isModalDialogOpen = true;
            try { return show(); }
            finally
            {
                _isModalDialogOpen = false;
                Dispatcher.BeginInvoke(ProcessActivationRefresh, System.Windows.Threading.DispatcherPriority.Background);
            }
        }

        public async Task RefreshAsync()
        {
            if (_viewModel.IsOperating) return;
            await _viewModel.RefreshAsync();
            if (IsVisible && Window.GetWindow(this)?.IsActive == true) FocusFileList();
        }

        private void CopyFileName(GitDiffFile file)
        {
            if (file == null) return;
            try { Clipboard.SetText(file.FileName); _viewModel.SetOperationStatus($"{file.FileName} をコピーしました。"); }
            catch (ExternalException) { _viewModel.SetOperationStatus("クリップボードにコピーできませんでした。もう一度お試しください。"); }
        }

        private void CopyFileNameButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button { DataContext: GitDiffFile file }) CopyFileName(file);
            e.Handled = true;
        }

        private void CopySelectedFileNameButton_Click(object sender, RoutedEventArgs e) => CopyFileName(_viewModel.SelectedFile);

        private void FileNameButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button { Tag: GitDiffFile file }) return;
            e.Handled = true;
            _viewModel.SelectedFile = file;
            FocusFileList();
        }

        private async void FileNameButton_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton != MouseButton.Left || sender is not Button { Tag: GitDiffFile file }) return;
            e.Handled = true;
            _viewModel.SelectedFile = file;
            await OpenFileAsync(file, null);
        }

        private async Task OpenFileAsync(GitDiffFile file, int? line)
        {
            if (file == null || _disposed) return;
            try
            {
                await _openFile(RepositoryPath, file.Path, line);
                if (!_disposed) _viewModel.SetOperationStatus($"{file.FileName} を VS Code で開きました。");
            }
            catch (Exception ex) { if (!_disposed) _viewModel.SetOperationStatus($"VS Code でファイルを開けませんでした。\n{ex.Message}"); }
        }

        private void CopyCurrentBranchButton_Click(object sender, RoutedEventArgs e)
        {
            if (!_viewModel.CanCopyCurrentBranch) return;
            try
            {
                Clipboard.SetText(_viewModel.CurrentBranchName);
                _viewModel.SetOperationStatus($"{_viewModel.CurrentBranchName} をコピーしました。");
            }
            catch (ExternalException)
            {
                _viewModel.SetOperationStatus("クリップボードにコピーできませんでした。もう一度お試しください。");
            }
        }

        private async void PushButton_Click(object sender, RoutedEventArgs e)
        {
            if (!_viewModel.CanOperateCurrentBranch) return;
            await _viewModel.PushCurrentBranchAsync();
            FocusHeaderButton(PushButton);
        }

        private async void PullButton_Click(object sender, RoutedEventArgs e)
        {
            if (!_viewModel.CanOperateCurrentBranch) return;
            await _viewModel.PullCurrentBranchAsync();
            FocusHeaderButton(PullButton);
        }

        private async void RenameBranchButton_Click(object sender, RoutedEventArgs e)
        {
            if (!_viewModel.CanOperateCurrentBranch || _isRenameDialogOpen) return;
            _isRenameDialogOpen = true;
            try
            {
                string branchName = _viewModel.CurrentBranchName;
                var dialog = new BranchNameDialog(currentBranchName: branchName) { Owner = Window.GetWindow(this) };
                if (ShowModal(dialog.ShowDialog) == true) await _viewModel.RenameCurrentBranchAsync(branchName, dialog.BranchName);
            }
            finally
            {
                _isRenameDialogOpen = false;
                FocusHeaderButton(RenameBranchButton);
            }
        }

        private Button[] HeaderButtons => new[] { PushButton, PullButton, UpdateFromBaseButton, RefreshButton, RenameBranchButton, CopyCurrentBranchButton }
            .Where(button => button.IsVisible && button.IsEnabled).ToArray();

        private Button[] FileActionButtons => new[] { CommitStagedButton, StashPreparedButton }
            .Where(button => button.IsVisible && button.IsEnabled).ToArray();

        private void FocusHeaderButton(Button preferred)
        {
            if (!IsVisible || Window.GetWindow(this)?.IsActive != true) return;
            if (preferred.IsEnabled) preferred.Focus();
            else HeaderButtons.FirstOrDefault()?.Focus();
        }

        private async void StageFileButton_Click(object sender, RoutedEventArgs e)
        {
            e.Handled = true;
            if (sender is not Button { DataContext: GitDiffFile file } || _viewModel.IsBusy || file.IsStashPrepared) return;
            bool restoreFocus = ((Button)sender).IsKeyboardFocused;
            int inputVersion = _inputVersion;
            if (file.IsStaged) await _viewModel.UnstageFileAsync(file);
            else await _viewModel.StageFileAsync(file);
            if (restoreFocus && CanRestoreFocus(inputVersion)) FocusFileList();
        }

        private void FileStashButton_Click(object sender, RoutedEventArgs e)
        {
            e.Handled = true;
            if (sender is not Button { DataContext: GitDiffFile file } button || _viewModel.IsBusy) return;
            bool restoreFocus = button.IsKeyboardFocused;
            _viewModel.ToggleStashPreparation(file);
            if (restoreFocus && IsVisible && Window.GetWindow(this)?.IsActive == true) FocusFileList();
        }

        private async void StashPreparedButton_Click(object sender, RoutedEventArgs e)
        {
            if (!_viewModel.CanStashPreparedFiles) return;
            int inputVersion = _inputVersion;
            bool restoreFocus = StashPreparedButton.IsKeyboardFocused;
            await _viewModel.StashPreparedFilesAsync();
            if (restoreFocus && CanRestoreFocus(inputVersion)) FocusFileList();
        }

        private async void UpdateFromBaseButton_Click(object sender, RoutedEventArgs e)
        {
            if (_viewModel.CanUpdateFromBase) await _viewModel.UpdateFromBaseAsync();
            FocusHeaderButton(UpdateFromBaseButton);
        }

        public void SwitchDiffMode(int direction)
        {
            _viewModel.SelectedModeIndex = Math.Clamp(_viewModel.SelectedModeIndex + direction, 0, DiffModeTabs.Items.Count - 1);
        }

        private void ShowCommitButton_Click(object sender, RoutedEventArgs e) => _viewModel.ShowCommit(CommitRevisionBox.Text);
        private void CommitRevisionBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key != Key.Enter) return;
            _viewModel.ShowCommit(CommitRevisionBox.Text);
            e.Handled = true;
        }
        private async void SearchButton_Click(object sender, RoutedEventArgs e) => await _viewModel.SearchAsync();
        private void RecentCommitsExpander_Expanded(object sender, RoutedEventArgs e)
        {
            if (!ReferenceEquals(e.OriginalSource, RecentCommitsExpander)) return;
            Dispatcher.BeginInvoke(() =>
            {
                if (!RecentCommitsExpander.IsExpanded || !RecentCommitList.IsVisible || !RecentCommitList.IsEnabled || RecentCommitList.Items.Count == 0) return;
                if (RecentCommitList.SelectedItem == null) RecentCommitList.SelectedIndex = 0;
                RecentCommitList.ScrollIntoView(RecentCommitList.SelectedItem);
                RecentCommitList.UpdateLayout();
                if (RecentCommitList.ItemContainerGenerator.ContainerFromItem(RecentCommitList.SelectedItem) is ListBoxItem item) item.Focus();
            }, System.Windows.Threading.DispatcherPriority.Loaded);
        }
        private ToggleButton RecentCommitsHeader()
        {
            RecentCommitsExpander.ApplyTemplate();
            return RecentCommitsExpander.Template.FindName("HeaderSite", RecentCommitsExpander) as ToggleButton;
        }
        private void DiffSearchBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key != Key.Enter) return;
            e.Handled = true;
            _viewModel.NextSearchResult(Keyboard.Modifiers.HasFlag(ModifierKeys.Shift) ? -1 : 1);
        }
        private void NextSearchButton_Click(object sender, RoutedEventArgs e) => _viewModel.NextSearchResult(1);
        private void PreviousSearchButton_Click(object sender, RoutedEventArgs e) => _viewModel.NextSearchResult(-1);

        private void FileList_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            _draggedFile = null;
            if (_viewModel.IsBusy || e.OriginalSource is not DependencyObject source) return;
            if (ItemsControl.ContainerFromElement(FileList, source) is not ListBoxItem { DataContext: GitDiffFile file }) return;
            for (var current = source; current != null && current is not ListBoxItem; current = VisualTreeHelper.GetParent(current))
                if (current is Button { Name: not "FileOpenButton" }) return;
            _draggedFile = file;
            _fileDragStart = e.GetPosition(FileList);
        }

        private void FileList_PreviewMouseMove(object sender, MouseEventArgs e)
        {
            if (_draggedFile == null || e.LeftButton != MouseButtonState.Pressed || _viewModel.IsBusy) return;
            var point = e.GetPosition(FileList);
            if (Math.Abs(point.X - _fileDragStart.X) < SystemParameters.MinimumHorizontalDragDistance &&
                Math.Abs(point.Y - _fileDragStart.Y) < SystemParameters.MinimumVerticalDragDistance) return;
            try { DragDrop.DoDragDrop(FileList, new DataObject(FileDragFormat, _draggedFile), DragDropEffects.Move); }
            finally { _draggedFile = null; }
            e.Handled = true;
        }

        private GitDiffFile DropTarget(DragEventArgs e) => e.OriginalSource is DependencyObject source &&
            ItemsControl.ContainerFromElement(FileList, source) is ListBoxItem { DataContext: GitDiffFile file } ? file : null;

        private void FileList_DragOver(object sender, DragEventArgs e)
        {
            var source = e.Data.GetData(FileDragFormat) as GitDiffFile;
            var target = DropTarget(e);
            e.Effects = !_viewModel.IsBusy && source != null && target != null && source.Section == target.Section && _viewModel.Files.Contains(source)
                ? DragDropEffects.Move : DragDropEffects.None;
            e.Handled = true;
        }

        private void FileList_Drop(object sender, DragEventArgs e)
        {
            if (e.Data.GetData(FileDragFormat) is GitDiffFile source) _viewModel.MoveFile(source, DropTarget(e));
            e.Handled = true;
        }

        private async void StageLinesButton_Click(object sender, RoutedEventArgs e)
        {
            if (!_viewModel.CanStageLines) return;
            await _viewModel.StageLinesAsync(DiffLines.GetSelectedLineIndices());
        }

        private void DecreaseDiffFontButton_Click(object sender, RoutedEventArgs e) => ChangeDiffFontSize(-1);

        private void IncreaseDiffFontButton_Click(object sender, RoutedEventArgs e) => ChangeDiffFontSize(1);

        private void ChangeDiffFontSize(int change)
        {
            int size = Math.Clamp(_diffFontSize + change, 8, 32);
            if (size == _diffFontSize) return;
            _diffFontSize = size;
            DiffFontSizeText.Text = size.ToString();
            DiffLines.SetFontSize(size);
        }

        private async void SectionActionButton_Click(object sender, RoutedEventArgs e)
        {
            e.Handled = true;
            if (sender is not Button { DataContext: CollectionViewGroup group } button || _viewModel.IsBusy) return;
            if (group.Name as string == "スタッシュ準備群")
            {
                bool restorePreparationFocus = button.IsKeyboardFocused;
                _viewModel.ClearStashPreparation();
                if (restorePreparationFocus) FocusFileList();
                return;
            }
            bool isStaged = group.Items.OfType<GitDiffFile>().FirstOrDefault()?.IsStaged == true;
            if (isStaged ? !_viewModel.HasStagedChanges : !_viewModel.HasUnstagedChanges) return;
            bool restoreFocus = button.IsKeyboardFocused;
            int inputVersion = _inputVersion;
            if (isStaged) await _viewModel.UnstageAllAsync();
            else await _viewModel.StageAllAsync();
            if (restoreFocus && CanRestoreFocus(inputVersion))
            {
                if (!isStaged && CommitStagedButton.IsEnabled) CommitStagedButton.Focus();
                else FocusFileList();
            }
        }

        private bool CanRestoreFocus(int inputVersion) => _inputVersion == inputVersion && IsVisible && Window.GetWindow(this)?.IsActive == true;

        private async void DiscardFileButton_Click(object sender, RoutedEventArgs e)
        {
            if (!_viewModel.CanDiscardFile) return;
            if (ShowModal(() => MessageBox.Show(Window.GetWindow(this), $"「{_viewModel.SelectedFile.DisplayPath}」の未ステージ変更を破棄します。元に戻せません。\n続行しますか？",
                "変更の破棄", MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No)) == MessageBoxResult.Yes)
                await _viewModel.DiscardFileAsync();
        }

        private async void FileRowDiscardButton_Click(object sender, RoutedEventArgs e)
        {
            e.Handled = true;
            if (sender is not Button { DataContext: GitDiffFile file } button || _viewModel.IsBusy || file.IsStaged || file.IsConflicted || file.IsStashPrepared ||
                (!string.IsNullOrEmpty(file.OldPath) && file.OldPath != file.Path)) return;
            bool restoreFocus = button.IsKeyboardFocused;
            int inputVersion = _inputVersion;
            if (ShowModal(() => MessageBox.Show(Window.GetWindow(this), $"「{file.DisplayPath}」の未ステージ変更を破棄します。元に戻せません。\n続行しますか？",
                "変更の破棄", MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No)) == MessageBoxResult.Yes)
                await _viewModel.DiscardFileAsync(file);
            if (restoreFocus && CanRestoreFocus(inputVersion)) FocusFileList();
        }

        private async void DiscardLinesButton_Click(object sender, RoutedEventArgs e)
        {
            if (!_viewModel.CanStageLines) return;
            var indices = DiffLines.GetSelectedLineIndices();
            if (!indices.Any(index => index >= 0 && index < _viewModel.Lines.Count &&
                _viewModel.Lines[index].Kind is GitDiffLineKind.Added or GitDiffLineKind.Deleted))
            {
                _viewModel.SetOperationStatus("破棄する追加行または削除行を選択してください。");
                return;
            }
            if (ShowModal(() => MessageBox.Show(Window.GetWindow(this), "選択した未ステージの変更行を破棄します。元に戻せません。\n続行しますか？",
                "選択行の破棄", MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No)) == MessageBoxResult.Yes)
                await _viewModel.DiscardLinesAsync(indices);
        }

        private async void CommitStagedButton_Click(object sender, RoutedEventArgs e)
        {
            if (!_viewModel.CanCommitStagedChanges) return;
            var dialog = new CommitMessageDialog(stagedOnly: true) { Owner = Window.GetWindow(this) };
            if (ShowModal(dialog.ShowDialog) == true) await _viewModel.CommitStagedAsync(dialog.Message);
        }

        public void FocusFileList()
        {
            if (_viewModel.Files.Count == 0) { FocusHeaderButton(RefreshButton); return; }
            if (_viewModel.SelectedFile == null) { FocusHeaderButton(RefreshButton); return; }
            FileList.ScrollIntoView(_viewModel.SelectedFile);
            FileList.UpdateLayout();
            if (FileList.ItemContainerGenerator.ContainerFromItem(_viewModel.SelectedFile) is ListBoxItem item) item.Focus();
            else FocusHeaderButton(RefreshButton);
        }

        public void FocusFromEmptyArrow()
        {
            if (FileList.Items.Count > 0) FocusFileAt(0);
            else FocusHeaderButton(CopyCurrentBranchButton);
        }
        public bool IsEmptyFileListFocused => FileList.Items.Count == 0 && FileList.IsKeyboardFocused;

        private void FileList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (FileList.SelectedItem != null) FileList.ScrollIntoView(FileList.SelectedItem);
        }

        private async void RefreshButton_Click(object sender, RoutedEventArgs e)
        {
            await RefreshAsync();
        }

        private void View_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            _inputVersion++;
            if (!DiffLines.IsMouseOver) _copyDiffSelection = false;
            if (_viewModel.Files.Count != 0) return;

            for (var element = e.OriginalSource as DependencyObject; element != null && element != this;
                 element = element is FrameworkContentElement content ? content.Parent : VisualTreeHelper.GetParent(element))
            {
                if (element is ButtonBase or Thumb or ScrollBar or TextBoxBase or Selector or Expander) return;
            }

            if (!HeaderButtons.Any(button => button.IsKeyboardFocusWithin)) FocusHeaderButton(RefreshButton);
            e.Handled = true;
        }

        private void View_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            _inputVersion++;
            if (TryCopyDiffSelection(e)) return;
            if (e.Key == Key.Escape && DiffSearchBox.IsKeyboardFocusWithin)
            {
                if (_searchReturnFocus is not UIElement { IsVisible: true, IsEnabled: true, Focusable: true } target || !target.Focus())
                    FocusFileList();
                e.Handled = true;
                return;
            }
            if (Keyboard.Modifiers == ModifierKeys.None && e.Key is Key.PageUp or Key.PageDown)
            {
                SwitchDiffMode(e.Key == Key.PageDown ? 1 : -1);
                e.Handled = true;
                return;
            }
            if (e.Key == Key.F && (Keyboard.Modifiers == ModifierKeys.Control || Keyboard.Modifiers == (ModifierKeys.Control | ModifierKeys.Shift)))
            {
                _viewModel.SearchAllFiles = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift);
                DiffSearchBox.Focus();
                DiffSearchBox.SelectAll();
                e.Handled = true;
                return;
            }
            if (e.Key == Key.F3)
            {
                _viewModel.NextSearchResult(Keyboard.Modifiers.HasFlag(ModifierKeys.Shift) ? -1 : 1);
                e.Handled = true;
                return;
            }
            if (Keyboard.Modifiers == ModifierKeys.None && CommitRevisionBox.IsKeyboardFocusWithin && e.Key is Key.Up or Key.Down)
            {
                if (e.Key == Key.Up)
                {
                    RecentCommitsHeader()?.Focus();
                }
                else if (RecentCommitList.IsVisible && RecentCommitList.IsEnabled && RecentCommitList.Items.Count > 0)
                {
                    RecentCommitList.SelectedIndex = 0;
                    RecentCommitList.ScrollIntoView(RecentCommitList.SelectedItem);
                    RecentCommitList.UpdateLayout();
                    if (RecentCommitList.ItemContainerGenerator.ContainerFromIndex(0) is ListBoxItem item) item.Focus();
                }
                e.Handled = true;
                return;
            }
            if (Keyboard.Modifiers == ModifierKeys.None && e.Key is Key.Up or Key.Down && RecentCommitList.IsKeyboardFocusWithin)
            {
                if (RecentCommitList.Items.Count > 0)
                {
                    int index = RecentCommitList.SelectedIndex;
                    if (e.Key == Key.Up && index == 0)
                    {
                        CommitRevisionBox.Focus();
                        e.Handled = true;
                        return;
                    }
                    int next = Math.Clamp(index + (e.Key == Key.Down ? 1 : -1), 0, RecentCommitList.Items.Count - 1);
                    RecentCommitList.SelectedIndex = next;
                    RecentCommitList.ScrollIntoView(RecentCommitList.SelectedItem);
                    RecentCommitList.UpdateLayout();
                    if (RecentCommitList.ItemContainerGenerator.ContainerFromItem(RecentCommitList.SelectedItem) is ListBoxItem commitItem) commitItem.Focus();
                }
                e.Handled = true;
                return;
            }
            if (FileList.IsKeyboardFocusWithin && Keyboard.Modifiers == ModifierKeys.Shift && e.Key is Key.Up or Key.Down)
            {
                _viewModel.MoveSelectedFile(e.Key == Key.Up ? -1 : 1);
                FocusFileList();
                e.Handled = true;
                return;
            }
            if (Keyboard.Modifiers == ModifierKeys.None && e.Key is Key.Up or Key.Down or Key.Left or Key.Right)
            {
                if (e.Key == Key.Up && RecentCommitsHeader()?.IsKeyboardFocused == true && FileList.Items.Count > 0)
                {
                    FocusFileAt(FileList.Items.Count - 1);
                    e.Handled = true;
                    return;
                }
                if (e.Key is Key.Left or Key.Right && (DecreaseDiffFontButton.IsKeyboardFocused || IncreaseDiffFontButton.IsKeyboardFocused))
                {
                    if (e.Key == Key.Left) DecreaseDiffFontButton.Focus();
                    else IncreaseDiffFontButton.Focus();
                    e.Handled = true;
                    return;
                }
                var buttons = HeaderButtons;
                int index = Array.FindIndex(buttons, button => button.IsKeyboardFocusWithin);
                if (index >= 0)
                {
                    var source = buttons[index];
                    var origin = source.TranslatePoint(new Point(source.ActualWidth / 2, source.ActualHeight / 2), RepositoryHeader);
                    bool vertical = e.Key is Key.Up or Key.Down;
                    int direction = e.Key is Key.Up or Key.Left ? -1 : 1;
                    var next = buttons.Where(button => button != source).Select(button =>
                    {
                        var point = button.TranslatePoint(new Point(button.ActualWidth / 2, button.ActualHeight / 2), RepositoryHeader);
                        double primary = vertical ? (point.Y - origin.Y) * direction : (point.X - origin.X) * direction;
                        double cross = vertical ? Math.Abs(point.X - origin.X) : Math.Abs(point.Y - origin.Y);
                        return (button, primary, cross);
                    }).Where(candidate => candidate.primary > 1)
                      .OrderBy(candidate => candidate.cross).ThenBy(candidate => candidate.primary).FirstOrDefault();
                    if (next.button != null) next.button.Focus();
                    else if (e.Key == Key.Down && FileList.Items.Count > 0)
                    {
                        if (FileList.Items[0] is not GitDiffFile firstFile || !TryFocusSectionAction(firstFile)) FocusFileAt(0);
                    }
                    e.Handled = true;
                    return;
                }
                var actionButtons = FileActionButtons;
                int actionIndex = Array.FindIndex(actionButtons, button => button.IsKeyboardFocusWithin);
                if (actionIndex >= 0 && e.Key is Key.Up or Key.Down)
                {
                    int targetIndex = actionIndex + (e.Key == Key.Down ? 1 : -1);
                    if (targetIndex >= 0 && targetIndex < actionButtons.Length) actionButtons[targetIndex].Focus();
                    else if (e.Key == Key.Up && FileList.Items.Count > 0) FocusFileAt(FileList.Items.Count - 1);
                    e.Handled = true;
                    return;
                }
                if (Keyboard.FocusedElement is Button { Name: "SectionActionButton", DataContext: CollectionViewGroup group } &&
                    e.Key is Key.Up or Key.Down && group.Items.Count > 0)
                {
                    int firstIndex = FileList.Items.IndexOf(group.Items[0]);
                    if (e.Key == Key.Down) FocusFileAt(firstIndex);
                    else if (firstIndex > 0) FocusFileAt(firstIndex - 1);
                    else FocusHeaderButton(HeaderButtons.LastOrDefault() ?? RefreshButton);
                    e.Handled = true;
                    return;
                }
                if (FileList.IsKeyboardFocusWithin && Keyboard.FocusedElement is DependencyObject focusedElement &&
                    ItemsControl.ContainerFromElement(FileList, focusedElement) is ListBoxItem item)
                {
                    var rowButtons = FindFileButtons(item);
                    int buttonIndex = Array.FindIndex(rowButtons, button => button.IsKeyboardFocusWithin);
                    int fileIndex = FileList.Items.IndexOf(item.DataContext);
                    if (e.Key is Key.Left or Key.Right)
                    {
                        if (e.Key == Key.Right && buttonIndex + 1 < rowButtons.Length)
                        {
                            FileList.SelectedItem = item.DataContext;
                            rowButtons[buttonIndex + 1].Focus();
                        }
                        else if (e.Key == Key.Left)
                        {
                            if (buttonIndex > 0) rowButtons[buttonIndex - 1].Focus();
                            else if (buttonIndex == 0) item.Focus();
                        }
                        e.Handled = true;
                        return;
                    }
                    if (e.Key == Key.Up && item.DataContext is GitDiffFile currentFile &&
                        (fileIndex == 0 || FileList.Items[fileIndex - 1] is GitDiffFile previousFile && previousFile.Section != currentFile.Section) &&
                        TryFocusSectionAction(currentFile))
                    {
                        e.Handled = true;
                        return;
                    }
                    if (e.Key == Key.Down && fileIndex + 1 < FileList.Items.Count &&
                        item.DataContext is GitDiffFile sourceFile && FileList.Items[fileIndex + 1] is GitDiffFile nextFile &&
                        sourceFile.Section != nextFile.Section && TryFocusSectionAction(nextFile))
                    {
                        e.Handled = true;
                        return;
                    }
                    if (e.Key == Key.Up && fileIndex == 0 && buttons.Length > 0)
                    {
                        buttons[^1].Focus();
                        e.Handled = true;
                        return;
                    }
                    if (e.Key == Key.Down && fileIndex == FileList.Items.Count - 1 && RecentCommitsExpander.IsVisible && RecentCommitsExpander.IsEnabled)
                    {
                        RecentCommitsHeader()?.Focus();
                        e.Handled = true;
                        return;
                    }
                    if (e.Key == Key.Down && fileIndex == FileList.Items.Count - 1 && actionButtons.Length > 0)
                    {
                        actionButtons[0].Focus();
                        e.Handled = true;
                        return;
                    }
                    if (buttonIndex >= 0 && fileIndex >= 0)
                    {
                        int targetIndex = fileIndex + (e.Key == Key.Down ? 1 : -1);
                        if (targetIndex >= 0 && targetIndex < FileList.Items.Count) FocusFileAt(targetIndex, buttonIndex);
                        e.Handled = true;
                        return;
                    }
                }
            }
        }

        public bool TryCopyDiffSelection(KeyEventArgs e)
        {
            if (!_copyDiffSelection || !DiffLines.HasTextSelection || Keyboard.Modifiers != ModifierKeys.Control || e.Key != Key.C) return false;
            try { Clipboard.SetText(DiffLines.SelectedText); }
            catch (ExternalException) { _viewModel.SetOperationStatus("クリップボードにコピーできませんでした。"); }
            e.Handled = true;
            return true;
        }

        private void FocusFileAt(int index, int buttonIndex = -1)
        {
            FileList.SelectedIndex = index;
            FileList.ScrollIntoView(FileList.SelectedItem);
            FileList.UpdateLayout();
            if (FileList.ItemContainerGenerator.ContainerFromItem(FileList.SelectedItem) is not ListBoxItem item) return;
            var buttons = FindFileButtons(item);
            if (buttonIndex >= 0 && buttonIndex < buttons.Length) buttons[buttonIndex].Focus();
            else item.Focus();
        }

        private bool TryFocusSectionAction(GitDiffFile file)
        {
            FileList.ScrollIntoView(file);
            FileList.UpdateLayout();
            Button FindButton(DependencyObject parent)
            {
                if (parent is Button { Name: "SectionActionButton", DataContext: CollectionViewGroup group } button &&
                    group.Items.Contains(file) && button.IsVisible && button.IsEnabled) return button;
                for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
                {
                    var found = FindButton(VisualTreeHelper.GetChild(parent, i));
                    if (found != null) return found;
                }
                return null;
            }
            return FindButton(FileList)?.Focus() == true;
        }

        private static Button[] FindFileButtons(DependencyObject element)
        {
            var buttons = new List<Button>();
            void Visit(DependencyObject parent)
            {
                if (parent is Button button && button.Name is "FileCopyButton" or "FileStageButton" or "FileStashButton" or "FileDiscardButton" && button.IsVisible && button.IsEnabled)
                    buttons.Add(button);
                for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++) Visit(VisualTreeHelper.GetChild(parent, i));
            }
            Visit(element);
            return buttons.OrderBy(button => button.Name == "FileCopyButton" ? 0 : button.Name == "FileStageButton" ? 1 : button.Name == "FileStashButton" ? 2 : 3).ToArray();
        }

        public void Dispose() { _disposed = true; DiffLines.CancelHighlighting(); _viewModel.Dispose(); }
    }
}
