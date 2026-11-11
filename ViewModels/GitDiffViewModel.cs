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
        private readonly GitBranchService _branchService;
        private readonly GitSettingsManager _settingsManager;
        private readonly string _repositoryPath;
        private GitDiffContent _displayedDiff;
        private CancellationTokenSource _refreshCts;
        private CancellationTokenSource _diffCts;
        private bool _disposed;

        public GitDiffViewModel(string repositoryPath, double movementSpeed = 1200.0)
        {
            _service = new GitDiffService(repositoryPath);
            _stageService = new GitStageService(repositoryPath);
            _branchService = new GitBranchService(repositoryPath);
            _settingsManager = new GitSettingsManager();
            _repositoryPath = Path.TrimEndingDirectorySeparator(Path.GetFullPath(repositoryPath));
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
        public bool HasTrackingCounts => AheadBy.HasValue && BehindBy.HasValue;
        private string _trackingStatusText = "集計中...";
        public string TrackingStatusText { get => _trackingStatusText; private set { _trackingStatusText = value; OnPropertyChanged(); } }
        private string _trackingStatusToolTip = string.Empty;
        public string TrackingStatusToolTip { get => _trackingStatusToolTip; private set { _trackingStatusToolTip = value; OnPropertyChanged(); } }
        private string _currentBranchName = string.Empty;
        public string CurrentBranchName
        {
            get => _currentBranchName;
            private set { _currentBranchName = value; OnPropertyChanged(); OnPropertyChanged(nameof(CanCopyCurrentBranch)); OnPropertyChanged(nameof(CanOperateCurrentBranch)); }
        }
        public bool CanCopyCurrentBranch => !string.IsNullOrWhiteSpace(CurrentBranchName);
        public bool CanOperateCurrentBranch => CanCopyCurrentBranch && !IsBusy;
        private string _baseBranch = string.Empty;
        public string BaseBranch
        {
            get => _baseBranch;
            private set { _baseBranch = value; OnPropertyChanged(); OnPropertyChanged(nameof(BaseBranchDisplay)); }
        }
        public string BaseBranchDisplay => string.IsNullOrWhiteSpace(BaseBranch) ? "未設定" : BaseBranch;
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
                OnPropertyChanged(nameof(CanDiscardFile));
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
        public bool IsRefreshing { get => _isRefreshing; private set { _isRefreshing = value; OnPropertyChanged(); OnPropertyChanged(nameof(IsBusy)); OnPropertyChanged(nameof(CanStageLines)); OnPropertyChanged(nameof(CanDiscardFile)); OnPropertyChanged(nameof(CanOperateCurrentBranch)); } }

        private bool _isOperating;
        public bool IsOperating { get => _isOperating; private set { _isOperating = value; OnPropertyChanged(); OnPropertyChanged(nameof(IsBusy)); OnPropertyChanged(nameof(CanStageLines)); OnPropertyChanged(nameof(CanDiscardFile)); OnPropertyChanged(nameof(CanOperateCurrentBranch)); } }
        public bool IsBusy => IsRefreshing || IsOperating;
        public bool CanStageLines => !IsBusy && SelectedFile is { IsStaged: false, IsConflicted: false } && _displayedDiff != null &&
            _displayedDiff.Lines.Any(line => line.Kind is GitDiffLineKind.Added or GitDiffLineKind.Deleted);
        public bool CanDiscardFile => !IsBusy && SelectedFile is { IsStaged: false, IsConflicted: false } &&
            (string.IsNullOrEmpty(SelectedFile.OldPath) || SelectedFile.OldPath == SelectedFile.Path) && !string.IsNullOrEmpty(_displayedDiff?.RawPatch);
        public bool HasStagedChanges => Files.Any(file => file.IsStaged);
        public bool HasUnstagedChanges => Files.Any(file => !file.IsStaged);
        private string _operationStatus = "";
        public string OperationStatus { get => _operationStatus; private set { _operationStatus = value; OnPropertyChanged(); } }
        public void SetOperationStatus(string message) => OperationStatus = message;

        public Task StageFileAsync(GitDiffFile file) => RunOperationAsync(() => _stageService.StageFileAsync(file), $"{file.FileName} をステージしました。");
        public Task StageAllAsync() => RunOperationAsync(() => _stageService.StageAllAsync(), "すべての変更をステージしました。");
        public Task DiscardFileAsync()
        {
            var file = SelectedFile;
            var diff = _displayedDiff;
            return file == null ? Task.CompletedTask : RunOperationAsync(() => _stageService.DiscardFileAsync(file, diff), $"{file.FileName} の未ステージ変更を破棄しました。");
        }
        public Task DiscardFileAsync(GitDiffFile file) => file == null ? Task.CompletedTask : RunOperationAsync(async () =>
        {
            var diff = ReferenceEquals(file, SelectedFile) && _displayedDiff != null
                ? _displayedDiff : await Task.Run(() => _service.ReadDiff(file, CancellationToken.None));
            await _stageService.DiscardFileAsync(file, diff);
        }, $"{file.FileName} の未ステージ変更を破棄しました。");
        public Task DiscardLinesAsync(IReadOnlyCollection<int> indices)
        {
            var file = SelectedFile;
            var diff = _displayedDiff;
            return file == null ? Task.CompletedTask : RunOperationAsync(() => _stageService.DiscardLinesAsync(file, diff, indices), "選択した変更行を破棄しました。");
        }
        public Task UnstageFileAsync(GitDiffFile file) => RunOperationAsync(() => _stageService.UnstageFileAsync(file), $"{file.FileName} のステージを解除しました。");
        public Task StageLinesAsync(IReadOnlyCollection<int> indices)
        {
            var file = SelectedFile;
            var diff = _displayedDiff;
            return file == null ? Task.CompletedTask : RunOperationAsync(() => _stageService.StageLinesAsync(file, diff, indices), "選択した変更行をステージしました。");
        }
        public Task CommitStagedAsync(string message) => RunOperationAsync(() => _stageService.CommitStagedAsync(message), "ステージ済みの変更をコミットしました。");
        public Task PushCurrentBranchAsync() => RunBranchOperationAsync(_branchService.PushCurrentBranchAsync, "現在のブランチをプッシュしました。");
        public Task PullCurrentBranchAsync() => RunBranchOperationAsync(_branchService.PullCurrentBranchAsync, "現在のブランチをプルしました。");
        public Task RenameCurrentBranchAsync(string expectedName, string newName) =>
            RunBranchOperationAsync(() => _branchService.RenameCurrentBranchAsync(expectedName, newName), "ブランチ名を変更しました。");

        private Task RunBranchOperationAsync(Func<Task> operation, string successMessage) => RunOperationAsync(async () =>
        {
            try { await operation(); }
            catch
            {
                // プルが競合で失敗した場合も、変更された作業ツリーを表示へ反映する。
                await RefreshAsync();
                throw;
            }
        }, successMessage);

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
            var initialSelection = SelectedFile;
            string selectedKey = initialSelection?.Key;
            IsRefreshing = true;
            _settingsManager.LoadSettings();
            BaseBranch = _settingsManager.CurrentSettings.RepositoryNameMappings.FirstOrDefault(mapping =>
                string.Equals(Path.TrimEndingDirectorySeparator(mapping.Path ?? string.Empty), _repositoryPath, StringComparison.OrdinalIgnoreCase))
                ?.BaseBranch?.Trim() ?? string.Empty;
            try
            {
                var filesTask = Task.Run(() => _service.ReadFiles(token), token);
                var branchTask = Task.Run(() => _service.ReadBranchInfo(token), token);
                await Task.WhenAll(filesTask, branchTask);
                var snapshot = (Files: await filesTask, Branch: await branchTask);
                if (token.IsCancellationRequested || _disposed) return;
                var files = snapshot.Files;
                CurrentBranch = snapshot.Branch.CurrentBranch;
                CurrentBranchName = snapshot.Branch.BranchName;
                SetTrackingInfo(snapshot.Branch);
                var fileSet = files.ToHashSet();
                for (int i = Files.Count - 1; i >= 0; i--)
                    if (!fileSet.Contains(Files[i])) Files.RemoveAt(i);
                for (int i = 0; i < files.Count; i++)
                {
                    int existingIndex = Files.IndexOf(files[i]);
                    if (existingIndex < 0) Files.Insert(i, files[i]);
                    else if (existingIndex != i) Files.Move(existingIndex, i);
                }
                OnPropertyChanged(nameof(HasStagedChanges));
                OnPropertyChanged(nameof(HasUnstagedChanges));
                Overview = $"未ステージ {files.Count(file => !file.IsStaged)} 件  /  ステージ済み {files.Count(file => file.IsStaged)} 件";
                var previousSelection = SelectedFile;
                if (previousSelection != null && !ReferenceEquals(previousSelection, initialSelection))
                    selectedKey = previousSelection.Key;
                SelectedFile = Files.FirstOrDefault(file => file.Key == selectedKey) ?? Files.FirstOrDefault();
                if (ReferenceEquals(previousSelection, SelectedFile) && SelectedFile != null)
                    await LoadDiffAsync(SelectedFile, preserveDisplay: true);
                if (SelectedFile == null) Message = "未コミットの変更はありません。";
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                if (token.IsCancellationRequested || _disposed) return;
                Files.Clear();
                OnPropertyChanged(nameof(HasStagedChanges));
                OnPropertyChanged(nameof(HasUnstagedChanges));
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
            OnPropertyChanged(nameof(HasTrackingCounts));
            TrackingStatusText = info == null ? "集計中..." : AheadBy.HasValue && BehindBy.HasValue
                ? $"behind {BehindBy.Value}  ·  ahead {AheadBy.Value}" : string.IsNullOrEmpty(TrackedBranch) ? "追跡先なし" : "差分不明";
            TrackingStatusToolTip = string.IsNullOrEmpty(TrackedBranch)
                ? "追跡先が未設定か、取得済みのリモート情報に追跡先がありません。"
                : $"追跡先: {TrackedBranch}\nbehind: リモート側だけにあるコミット数\nahead: ローカル側だけにあるコミット数\n最終 fetch / pull で取得したリモート情報に基づきます。";
        }

        private async Task LoadDiffAsync(GitDiffFile file, bool preserveDisplay = false)
        {
            _diffCts?.Cancel();
            _diffCts?.Dispose();
            _diffCts = new CancellationTokenSource();
            var token = _diffCts.Token;
            if (!preserveDisplay)
            {
                _displayedDiff = null;
                OnPropertyChanged(nameof(CanStageLines));
                OnPropertyChanged(nameof(CanDiscardFile));
                Lines = Array.Empty<GitDiffLine>();
                Summary = "";
            }
            if (file == null || _disposed) return;
            if (!preserveDisplay) Message = "差分を読み込んでいます...";
            try
            {
                var previousDiff = preserveDisplay ? _displayedDiff : null;
                var diff = await Task.Run(() => _service.ReadDiff(file, token, previousDiff), token);
                if (token.IsCancellationRequested || _disposed) return;
                bool unchanged = preserveDisplay && _displayedDiff != null &&
                    _displayedDiff.RawPatch == diff.RawPatch && _displayedDiff.Message == diff.Message &&
                    _displayedDiff.Added == diff.Added && _displayedDiff.Deleted == diff.Deleted;
                _displayedDiff = diff;
                if (!unchanged) Lines = diff.Lines;
                OnPropertyChanged(nameof(CanStageLines));
                OnPropertyChanged(nameof(CanDiscardFile));
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
