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
        private readonly GitStageService _stageService;
        private GitDiffContent _displayedDiff;
        private CancellationTokenSource _refreshCts;
        private CancellationTokenSource _diffCts;
        private bool _disposed;

        public GitDiffViewModel(string repositoryPath, double movementSpeed = 1200.0)
        {
            _service = new GitDiffService(repositoryPath);
            _stageService = new GitStageService(repositoryPath);
            MovementSpeed = movementSpeed;
            RepositoryName = new DirectoryInfo(repositoryPath).Name;
            FileView = new ListCollectionView(Files);
            FileView.GroupDescriptions.Add(new PropertyGroupDescription(nameof(GitDiffFile.Section)));
        }

        public string RepositoryName { get; }
        public double MovementSpeed { get; }
        private string _currentBranch = "読み込み中...";
        public string CurrentBranch { get => _currentBranch; private set { _currentBranch = value; OnPropertyChanged(); } }
        public int? AheadBy { get; private set; }
        public int? BehindBy { get; private set; }
        public string TrackedBranch { get; private set; } = string.Empty;
        private string _trackingStatusText = "集計中...";
        public string TrackingStatusText { get => _trackingStatusText; private set { _trackingStatusText = value; OnPropertyChanged(); } }
        private string _trackingStatusToolTip = string.Empty;
        public string TrackingStatusToolTip { get => _trackingStatusToolTip; private set { _trackingStatusToolTip = value; OnPropertyChanged(); } }
        private string _currentBranchName = string.Empty;
        public string CurrentBranchName
        {
            get => _currentBranchName;
            private set { _currentBranchName = value; OnPropertyChanged(); OnPropertyChanged(nameof(CanCopyCurrentBranch)); }
        }
        public bool CanCopyCurrentBranch => !string.IsNullOrWhiteSpace(CurrentBranchName);
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
                OnPropertyChanged(nameof(CanStageLines));
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
        public bool IsRefreshing { get => _isRefreshing; private set { _isRefreshing = value; OnPropertyChanged(); OnPropertyChanged(nameof(IsBusy)); OnPropertyChanged(nameof(CanStageLines)); } }

        private bool _isOperating;
        public bool IsOperating { get => _isOperating; private set { _isOperating = value; OnPropertyChanged(); OnPropertyChanged(nameof(IsBusy)); OnPropertyChanged(nameof(CanStageLines)); } }
        public bool IsBusy => IsRefreshing || IsOperating;
        public bool CanStageLines => !IsBusy && SelectedFile is { IsStaged: false, IsConflicted: false } && _displayedDiff != null &&
            _displayedDiff.Lines.Any(line => line.Kind is GitDiffLineKind.Added or GitDiffLineKind.Deleted);
        public bool HasStagedChanges => Files.Any(file => file.IsStaged);
        private string _operationStatus = "";
        public string OperationStatus { get => _operationStatus; private set { _operationStatus = value; OnPropertyChanged(); } }
        public void SetOperationStatus(string message) => OperationStatus = message;

        public Task StageFileAsync(GitDiffFile file) => RunOperationAsync(() => _stageService.StageFileAsync(file), $"{file.FileName} をステージしました。");
        public Task UnstageFileAsync(GitDiffFile file) => RunOperationAsync(() => _stageService.UnstageFileAsync(file), $"{file.FileName} のステージを解除しました。");
        public Task StageLinesAsync(IReadOnlyCollection<int> indices)
        {
            var file = SelectedFile;
            var diff = _displayedDiff;
            return file == null ? Task.CompletedTask : RunOperationAsync(() => _stageService.StageLinesAsync(file, diff, indices), "選択した変更行をステージしました。");
        }
        public Task CommitStagedAsync(string message) => RunOperationAsync(() => _stageService.CommitStagedAsync(message), "ステージ済みの変更をコミットしました。");

        private async Task RunOperationAsync(Func<Task> operation, string successMessage)
        {
            if (_disposed || IsBusy) return;
            IsOperating = true;
            OperationStatus = "実行中...";
            try
            {
                await operation();
                if (_disposed) return;
                await RefreshAsync();
                OperationStatus = successMessage;
            }
            catch (Exception ex)
            {
                if (!_disposed) OperationStatus = $"操作に失敗しました。\n{ex.Message}";
            }
            finally { IsOperating = false; }
        }

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
            CurrentBranch = "読み込み中...";
            CurrentBranchName = string.Empty;
            SetTrackingInfo(null);
            try
            {
                var snapshot = await Task.Run(() => (Files: _service.ReadFiles(token), Branch: _service.ReadBranchInfo(token)), token);
                if (token.IsCancellationRequested || _disposed) return;
                var files = snapshot.Files;
                CurrentBranch = snapshot.Branch.CurrentBranch;
                CurrentBranchName = snapshot.Branch.BranchName;
                SetTrackingInfo(snapshot.Branch);
                Files.Clear();
                foreach (var file in files) Files.Add(file);
                OnPropertyChanged(nameof(HasStagedChanges));
                Overview = $"未ステージ {files.Count(file => !file.IsStaged)} 件  /  ステージ済み {files.Count(file => file.IsStaged)} 件";
                SelectedFile = files.FirstOrDefault(file => file.Key == selectedKey) ?? files.FirstOrDefault();
                if (SelectedFile == null) Message = "未コミットの変更はありません。";
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                if (token.IsCancellationRequested || _disposed) return;
                Files.Clear();
                OnPropertyChanged(nameof(HasStagedChanges));
                Overview = "読み込み失敗";
                CurrentBranch = "取得失敗";
                TrackingStatusText = "取得失敗";
                Message = $"ファイル一覧を取得できませんでした。\n{ex.Message}";
            }
            finally { if (!token.IsCancellationRequested && !_disposed) IsRefreshing = false; }
        }

        private void SetTrackingInfo(GitDiffBranchInfo info)
        {
            AheadBy = info?.AheadBy;
            BehindBy = info?.BehindBy;
            TrackedBranch = info?.TrackedBranch ?? string.Empty;
            OnPropertyChanged(nameof(AheadBy));
            OnPropertyChanged(nameof(BehindBy));
            OnPropertyChanged(nameof(TrackedBranch));
            TrackingStatusText = info == null ? "集計中..." : AheadBy.HasValue && BehindBy.HasValue
                ? $"behind {BehindBy.Value}  ·  ahead {AheadBy.Value}" : string.IsNullOrEmpty(TrackedBranch) ? "追跡先なし" : "差分不明";
            TrackingStatusToolTip = string.IsNullOrEmpty(TrackedBranch)
                ? "追跡先が未設定か、取得済みのリモート情報に追跡先がありません。"
                : $"追跡先: {TrackedBranch}\nbehind: リモート側だけにあるコミット数\nahead: ローカル側だけにあるコミット数\n最終 fetch / pull で取得したリモート情報に基づきます。";
        }

        private async Task LoadDiffAsync(GitDiffFile file)
        {
            _diffCts?.Cancel();
            _diffCts?.Dispose();
            _diffCts = new CancellationTokenSource();
            var token = _diffCts.Token;
            _displayedDiff = null;
            OnPropertyChanged(nameof(CanStageLines));
            Lines = Array.Empty<GitDiffLine>();
            Summary = "";
            if (file == null || _disposed) return;
            Message = "差分を読み込んでいます...";
            try
            {
                var diff = await Task.Run(() => _service.ReadDiff(file, token), token);
                if (token.IsCancellationRequested || _disposed) return;
                _displayedDiff = diff;
                Lines = diff.Lines;
                OnPropertyChanged(nameof(CanStageLines));
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
