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

        public GitDiffRepositoryView(string repositoryPath, double movementSpeed, Func<string, string, int?, Task> openFile = null)
        {
            RepositoryPath = Path.TrimEndingDirectorySeparator(Path.GetFullPath(repositoryPath));
            _openFile = openFile ?? VsCodeFileOpener.OpenAsync;
            InitializeComponent();
            _viewModel = new GitDiffViewModel(RepositoryPath, movementSpeed);
            DataContext = _viewModel;
            DiffLines.LineOpenRequested += line => _ = OpenFileAsync(_viewModel.SelectedFile, line);
            _viewModel.PropertyChanged += (_, args) =>
            {
                if (args.PropertyName == nameof(GitDiffViewModel.Lines)) DiffLines.SetLines(_viewModel.Lines, _viewModel.SelectedFile, RepositoryPath);
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

        private Button[] HeaderButtons => new[] { PushButton, PullButton, RefreshButton, RenameBranchButton, CopyCurrentBranchButton }
            .Where(button => button.IsVisible && button.IsEnabled).ToArray();

        private Button[] FileActionButtons => new[] { CommitStagedButton }
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
            if (sender is not Button { DataContext: GitDiffFile file } || _viewModel.IsBusy) return;
            bool restoreFocus = ((Button)sender).IsKeyboardFocused;
            int inputVersion = _inputVersion;
            if (file.IsStaged) await _viewModel.UnstageFileAsync(file);
            else await _viewModel.StageFileAsync(file);
            if (restoreFocus && CanRestoreFocus(inputVersion)) FocusFileList();
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
            if (sender is not Button { DataContext: GitDiffFile file } button || _viewModel.IsBusy || file.IsStaged || file.IsConflicted ||
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
            if (_viewModel.IsBusy || !_viewModel.HasStagedChanges) return;
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
            if (_viewModel.Files.Count != 0) return;

            for (var element = e.OriginalSource as DependencyObject; element != null && element != this;
                 element = element is FrameworkContentElement content ? content.Parent : VisualTreeHelper.GetParent(element))
            {
                if (element is ButtonBase or Thumb or ScrollBar) return;
            }

            if (!HeaderButtons.Any(button => button.IsKeyboardFocusWithin)) FocusHeaderButton(RefreshButton);
            e.Handled = true;
        }

        private void View_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            _inputVersion++;
            if (Keyboard.Modifiers == ModifierKeys.None && e.Key is Key.Up or Key.Down or Key.Left or Key.Right)
            {
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
                    else if (e.Key == Key.Down && _viewModel.Files.Count > 0) FocusFileList();
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
                        (fileIndex == 0 || FileList.Items[fileIndex - 1] is GitDiffFile previousFile && previousFile.IsStaged != currentFile.IsStaged) &&
                        TryFocusSectionAction(currentFile))
                    {
                        e.Handled = true;
                        return;
                    }
                    if (e.Key == Key.Down && fileIndex + 1 < FileList.Items.Count &&
                        item.DataContext is GitDiffFile sourceFile && FileList.Items[fileIndex + 1] is GitDiffFile nextFile &&
                        sourceFile.IsStaged != nextFile.IsStaged && TryFocusSectionAction(nextFile))
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
                if (parent is Button button && button.Name is "FileCopyButton" or "FileStageButton" or "FileDiscardButton" && button.IsVisible && button.IsEnabled)
                    buttons.Add(button);
                for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++) Visit(VisualTreeHelper.GetChild(parent, i));
            }
            Visit(element);
            return buttons.OrderBy(button => button.Name == "FileCopyButton" ? 0 : button.Name == "FileStageButton" ? 1 : 2).ToArray();
        }

        public void Dispose() { _disposed = true; DiffLines.CancelHighlighting(); _viewModel.Dispose(); }
    }
}
