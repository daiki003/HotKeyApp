using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.ComponentModel;
using System.Runtime.InteropServices;
using HotKeyCommandApp.Models;
using HotKeyCommandApp.Services;
using HotKeyCommandApp.ViewModels;

namespace HotKeyCommandApp.Views
{
    public partial class GitDiffWindow : Window
    {
        private readonly GitDiffViewModel _viewModel;

        public GitDiffWindow(string repositoryPath, double movementSpeed = 1200.0)
        {
            InitializeComponent();
            _viewModel = new GitDiffViewModel(repositoryPath, movementSpeed);
            DataContext = _viewModel;
            _viewModel.PropertyChanged += (_, args) =>
            {
                if (args.PropertyName == nameof(GitDiffViewModel.Lines)) DiffLines.SetLines(_viewModel.Lines);
            };
            Loaded += async (_, _) => { await _viewModel.RefreshAsync(); FocusFileList(); };
            Closed += (_, _) => _viewModel.Dispose();
        }

        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);
            WindowHelper.DisableSystemMenu(this);
            WindowHelper.EnableWindowMoveShortcut(this);
        }

        protected override void OnClosing(CancelEventArgs e)
        {
            if (_viewModel.IsOperating) { e.Cancel = true; return; }
            base.OnClosing(e);
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
            var dialog = new CommitMessageDialog(stagedOnly: true) { Owner = this };
            if (dialog.ShowDialog() == true) await _viewModel.CommitStagedAsync(dialog.Message);
        }

        private void FocusFileList()
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
            if (_viewModel.IsBusy) return;
            await _viewModel.RefreshAsync();
            FocusFileList();
        }

        private async void Window_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape) { Close(); e.Handled = true; return; }
            if (e.Key == Key.F5)
            {
                e.Handled = true;
                if (!_viewModel.IsBusy) { await _viewModel.RefreshAsync(); FocusFileList(); }
                return;
            }
            if ((DiffLines.IsKeyboardFocusWithin || SelectedFilePathBox.IsKeyboardFocusWithin) &&
                Keyboard.Modifiers.HasFlag(ModifierKeys.Control) && e.Key is Key.Left or Key.Right or Key.Up or Key.Down)
                return;
            if (Keyboard.Modifiers == (ModifierKeys.Control | ModifierKeys.Shift))
            {
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
                    double step = _viewModel.MovementSpeed / 60.0;
                    double offsetX = e.Key == Key.Left ? -step : e.Key == Key.Right ? step : 0;
                    double offsetY = e.Key == Key.Up ? -step : e.Key == Key.Down ? step : 0;
                    Left += offsetX;
                    Top += offsetY;
                    if (Owner != null) { Owner.Left += offsetX; Owner.Top += offsetY; }
                }
                e.Handled = true;
                return;
            }
            if (Keyboard.Modifiers != ModifierKeys.None) return;
            if (FileList.IsKeyboardFocusWithin && e.Key is Key.Left or Key.Right) e.Handled = true;
        }
    }
}
