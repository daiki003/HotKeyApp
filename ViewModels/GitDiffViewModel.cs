using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows.Data;
using HotKeyCommandApp.Models;
using HotKeyCommandApp.Services;

namespace HotKeyCommandApp.ViewModels
{
    public sealed class GitDiffViewModel : INotifyPropertyChanged, IDisposable
    {
        private readonly GitDiffService _service;
        private CancellationTokenSource _refreshCts;
        private CancellationTokenSource _diffCts;
        private bool _disposed;

        public GitDiffViewModel(string repositoryPath, double movementSpeed = 1200.0)
        {
            _service = new GitDiffService(repositoryPath);
            MovementSpeed = movementSpeed;
            RepositoryName = new DirectoryInfo(repositoryPath).Name;
            FileView = new ListCollectionView(Files);
            FileView.GroupDescriptions.Add(new PropertyGroupDescription(nameof(GitDiffFile.Section)));
        }

        public string RepositoryName { get; }
        public double MovementSpeed { get; }
        private string _currentBranch = "読み込み中...";
        public string CurrentBranch { get => _currentBranch; private set { _currentBranch = value; OnPropertyChanged(); } }
        public ObservableCollection<GitDiffFile> Files { get; } = new();
        public ListCollectionView FileView { get; }
        public event PropertyChangedEventHandler PropertyChanged;

        private GitDiffFile _selectedFile;
        public GitDiffFile SelectedFile
        {
            get => _selectedFile;
            set
            {
                if (ReferenceEquals(_selectedFile, value)) return;
                _selectedFile = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(SelectedFilePath));
                OnPropertyChanged(nameof(Comparison));
                _ = LoadDiffAsync(value);
            }
        }

        public string SelectedFilePath => SelectedFile?.DisplayPath ?? "ファイルを選択してください";
        public string Comparison => SelectedFile == null ? "" : $"{SelectedFile.Section}  ·  {SelectedFile.Comparison}";

        private IReadOnlyList<GitDiffLine> _lines = Array.Empty<GitDiffLine>();
        public IReadOnlyList<GitDiffLine> Lines { get => _lines; private set { _lines = value; OnPropertyChanged(); } }
        private string _message = "ファイル一覧を読み込んでいます...";
        public string Message { get => _message; private set { _message = value; OnPropertyChanged(); } }
        private string _summary = "";
        public string Summary { get => _summary; private set { _summary = value; OnPropertyChanged(); } }
        private string _overview = "読み込み中...";
        public string Overview { get => _overview; private set { _overview = value; OnPropertyChanged(); } }
        private bool _isRefreshing;
        public bool IsRefreshing { get => _isRefreshing; private set { _isRefreshing = value; OnPropertyChanged(); } }

        public async Task RefreshAsync()
        {
            if (_disposed) return;
            _refreshCts?.Cancel();
            _refreshCts?.Dispose();
            _refreshCts = new CancellationTokenSource();
            var token = _refreshCts.Token;
            string selectedKey = SelectedFile?.Key;
            IsRefreshing = true;
            SelectedFile = null;
            Message = "ファイル一覧を読み込んでいます...";
            try
            {
                var snapshot = await Task.Run(() => (Files: _service.ReadFiles(token), Branch: _service.ReadCurrentBranch(token)), token);
                if (token.IsCancellationRequested || _disposed) return;
                var files = snapshot.Files;
                CurrentBranch = snapshot.Branch;
                Files.Clear();
                foreach (var file in files) Files.Add(file);
                Overview = $"未ステージ {files.Count(file => !file.IsStaged)} 件  /  ステージ済み {files.Count(file => file.IsStaged)} 件";
                SelectedFile = files.FirstOrDefault(file => file.Key == selectedKey) ?? files.FirstOrDefault();
                if (SelectedFile == null) Message = "未コミットの変更はありません。";
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                if (token.IsCancellationRequested || _disposed) return;
                Files.Clear();
                Overview = "読み込み失敗";
                Message = $"ファイル一覧を取得できませんでした。\n{ex.Message}";
            }
            finally { if (!token.IsCancellationRequested && !_disposed) IsRefreshing = false; }
        }

        private async Task LoadDiffAsync(GitDiffFile file)
        {
            _diffCts?.Cancel();
            _diffCts?.Dispose();
            _diffCts = new CancellationTokenSource();
            var token = _diffCts.Token;
            Lines = Array.Empty<GitDiffLine>();
            Summary = "";
            if (file == null || _disposed) return;
            Message = "差分を読み込んでいます...";
            try
            {
                var diff = await Task.Run(() => _service.ReadDiff(file, token), token);
                if (token.IsCancellationRequested || _disposed) return;
                Lines = diff.Lines;
                Summary = $"+{diff.Added}  −{diff.Deleted}";
                Message = diff.Message;
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                if (!token.IsCancellationRequested && !_disposed) Message = $"差分を表示できませんでした。F5 で一覧を更新できます。\n{ex.Message}";
            }
        }

        public void Dispose()
        {
            _disposed = true;
            _refreshCts?.Cancel();
            _refreshCts?.Dispose();
            _diffCts?.Cancel();
            _diffCts?.Dispose();
        }

        private void OnPropertyChanged([CallerMemberName] string name = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
