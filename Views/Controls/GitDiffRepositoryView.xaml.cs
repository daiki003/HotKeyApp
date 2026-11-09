using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using HotKeyCommandApp.Models;
using HotKeyCommandApp.ViewModels;
using HotKeyCommandApp.Views;

namespace HotKeyCommandApp.Views.Controls
{
    public partial class GitDiffRepositoryView : UserControl, IDisposable
    {
        private readonly GitDiffViewModel _viewModel;
        private bool _initialized;
        private bool _isRenameDialogOpen;

        public GitDiffRepositoryView(string repositoryPath, double movementSpeed)
        {
            RepositoryPath = Path.TrimEndingDirectorySeparator(Path.GetFullPath(repositoryPath));
            InitializeComponent();
            _viewModel = new GitDiffViewModel(RepositoryPath, movementSpeed);
            DataContext = _viewModel;
            _viewModel.PropertyChanged += (_, args) =>
            {
                if (args.PropertyName == nameof(GitDiffViewModel.Lines)) DiffLines.SetLines(_viewModel.Lines);
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

        public async Task RefreshAsync()
        {
            if (_viewModel.IsBusy) return;
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
                if (dialog.ShowDialog() == true) await _viewModel.RenameCurrentBranchAsync(branchName, dialog.BranchName);
            }
            finally
            {
                _isRenameDialogOpen = false;
                FocusHeaderButton(RenameBranchButton);
            }
        }

        private Button[] HeaderButtons => new[] { PushButton, PullButton, RefreshButton, RenameBranchButton, CopyCurrentBranchButton }
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
            if (file.IsStaged) await _viewModel.UnstageFileAsync(file);
            else await _viewModel.StageFileAsync(file);
        }

        private async void StageLinesButton_Click(object sender, RoutedEventArgs e)
        {
            if (!_viewModel.CanStageLines) return;
            await _viewModel.StageLinesAsync(DiffLines.GetSelectedLineIndices());
        }

        private async void CommitStagedButton_Click(object sender, RoutedEventArgs e)
        {
            if (_viewModel.IsBusy || !_viewModel.HasStagedChanges) return;
            var dialog = new CommitMessageDialog(stagedOnly: true) { Owner = Window.GetWindow(this) };
            if (dialog.ShowDialog() == true) await _viewModel.CommitStagedAsync(dialog.Message);
        }

        public void FocusFileList()
        {
            if (_viewModel.SelectedFile == null) { FileList.Focus(); return; }
            FileList.ScrollIntoView(_viewModel.SelectedFile);
            FileList.UpdateLayout();
            if (FileList.ItemContainerGenerator.ContainerFromItem(_viewModel.SelectedFile) is ListBoxItem item) item.Focus();
            else FileList.Focus();
        }

        private void FileList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (FileList.SelectedItem != null) FileList.ScrollIntoView(FileList.SelectedItem);
        }

        private async void RefreshButton_Click(object sender, RoutedEventArgs e)
        {
            await RefreshAsync();
        }

        private void View_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (Keyboard.Modifiers == ModifierKeys.None && e.Key is Key.Up or Key.Down)
            {
                var buttons = HeaderButtons;
                int index = Array.FindIndex(buttons, button => button.IsKeyboardFocusWithin);
                if (index >= 0)
                {
                    if (e.Key == Key.Down && index == buttons.Length - 1) FocusFileList();
                    else buttons[Math.Clamp(index + (e.Key == Key.Down ? 1 : -1), 0, buttons.Length - 1)].Focus();
                    e.Handled = true;
                    return;
                }
                if (e.Key == Key.Up && FileList.IsKeyboardFocusWithin &&
                    (FileList.SelectedItem == null || _viewModel.FileView.IndexOf(FileList.SelectedItem) == 0) && buttons.Length > 0)
                {
                    buttons[^1].Focus();
                    e.Handled = true;
                    return;
                }
            }
            if (Keyboard.Modifiers == ModifierKeys.None && FileList.IsKeyboardFocusWithin &&
                e.Key is Key.Left or Key.Right) e.Handled = true;
        }

        public void Dispose() => _viewModel.Dispose();
    }
}
