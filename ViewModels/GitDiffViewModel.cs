using System.Collections.ObjectModel;
using System.Collections.Specialized;
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
        private readonly GitStashService _stashService;
        private readonly HashSet<string> _stashPreparedPaths = new(StringComparer.Ordinal);
        private readonly GitBranchService _branchService;
        private readonly GitSettingsManager _settingsManager;
        private readonly string _repositoryPath;
        private GitDiffContent _displayedDiff;
        private string _displayedFileKey;
        private CancellationTokenSource _refreshCts;
        private CancellationTokenSource _diffCts;
        private CancellationTokenSource _searchCts;
        private readonly Dictionary<string, int> _manualOrder = new(StringComparer.Ordinal);
        private bool _selectingSearchMatch;
        private int _searchGeneration;
        private bool _disposed;

        public GitDiffViewModel(string repositoryPath, double movementSpeed = 1200.0)
        {
            _service = new GitDiffService(repositoryPath);
            _stageService = new GitStageService(repositoryPath);
            _stashService = new GitStashService(repositoryPath);
            _branchService = new GitBranchService(repositoryPath);
            _settingsManager = new GitSettingsManager();
            _repositoryPath = Path.TrimEndingDirectorySeparator(Path.GetFullPath(repositoryPath));
            MovementSpeed = movementSpeed;
            RepositoryName = new DirectoryInfo(repositoryPath).Name;
            FileView = new ListCollectionView(Files);
            FileView.GroupDescriptions.Add(new PropertyGroupDescription(nameof(GitDiffFile.Section)));
        }

        public string RepositoryName { get; }
        private GitDiffMode _mode;
        public GitDiffMode Mode => _mode;
        public bool IsWorkingMode => Mode == GitDiffMode.Uncommitted;
        public int SelectedModeIndex
        {
            get => (int)Mode;
            set
            {
                if (_disposed || IsBusy || value == (int)Mode) return;
                _mode = (GitDiffMode)value;
                NotifyModeChanged();
                _ = RefreshAsync();
            }
        }
        private void NotifyModeChanged()
        {
            CancelSearch();
            SelectedFile = null;
            OnPropertyChanged(nameof(Mode));
            OnPropertyChanged(nameof(SelectedModeIndex));
            OnPropertyChanged(nameof(IsWorkingMode));
        }
        public ObservableCollection<GitDiffCommit> RecentCommits { get; } = new();
        public void ShowCommit(string revision)
        {
            if (IsBusy || string.IsNullOrWhiteSpace(revision)) return;
            try { SelectedCommit = _service.ReadCommit(revision); }
            catch (Exception ex) { OperationStatus = ex.Message; }
        }
        private GitDiffCommit _selectedCommit;
        public GitDiffCommit SelectedCommit
        {
            get => _selectedCommit;
            set
            {
                if (_disposed || IsOperating || value == null || value == _selectedCommit && Mode == GitDiffMode.Commit) return;
                _selectedCommit = value;
                OnPropertyChanged();
                _mode = GitDiffMode.Commit;
                NotifyModeChanged();
                _ = RefreshAsync();
            }
        }
        private GitDiffSortOrder _sortOrder;
        public int SelectedSortIndex
        {
            get => (int)_sortOrder;
            set
            {
                if (_sortOrder == (GitDiffSortOrder)value) return;
                if ((GitDiffSortOrder)value == GitDiffSortOrder.Manual) RememberOrder();
                _sortOrder = (GitDiffSortOrder)value;
                OnPropertyChanged();
                string selectedKey = SelectedFile?.Key;
                ApplyFileSnapshot(Files.ToArray());
                if (selectedKey != null) SelectedFile = Files.FirstOrDefault(file => file.Key == selectedKey);
            }
        }
        private void RememberOrder()
        {
            for (int i = 0; i < Files.Count; i++) _manualOrder[Files[i].Key] = i;
        }
        public void MoveFile(GitDiffFile source, GitDiffFile target)
        {
            if (IsBusy || source == null || target == null || source.Section != target.Section || !Files.Contains(source) || !Files.Contains(target)) return;
            var selected = SelectedFile;
            int destination = Files.IndexOf(target);
            Files.Move(Files.IndexOf(source), destination);
            if (selected != null) SelectedFile = selected;
            RememberOrder();
            _sortOrder = GitDiffSortOrder.Manual;
            OnPropertyChanged(nameof(SelectedSortIndex));
        }
        public void MoveSelectedFile(int direction)
        {
            int index = Files.IndexOf(SelectedFile), target = index + direction;
            if (index >= 0 && target >= 0 && target < Files.Count) MoveFile(SelectedFile, Files[target]);
        }
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
            private set { _baseBranch = value; OnPropertyChanged(); OnPropertyChanged(nameof(BaseBranchDisplay)); OnPropertyChanged(nameof(CanUpdateFromBase)); }
        }
        public string BaseBranchDisplay => string.IsNullOrWhiteSpace(BaseBranch) ? "未設定" : BaseBranch;
        public ObservableCollection<GitDiffFile> Files { get; } = new BulkFileCollection();
        private sealed class BulkFileCollection : ObservableCollection<GitDiffFile>
        {
            public void ReplaceAll(IEnumerable<GitDiffFile> files)
            {
                Items.Clear();
                foreach (var file in files) Items.Add(file);
                OnPropertyChanged(new PropertyChangedEventArgs(nameof(Count)));
                OnPropertyChanged(new PropertyChangedEventArgs("Item[]"));
                OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));
            }
        }
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
                if (!_selectingSearchMatch && !SearchAllFiles) ScheduleSearch();
                OnPropertyChanged();
                OnPropertyChanged(nameof(SelectedFilePath));
                OnPropertyChanged(nameof(Comparison));
                OnPropertyChanged(nameof(CanStageLines));
                OnPropertyChanged(nameof(CanDiscardFile));
                if (!_selectingSearchMatch) _ = LoadDiffAsync(value);
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
        public bool IsRefreshing { get => _isRefreshing; private set { _isRefreshing = value; OnPropertyChanged(); OnPropertyChanged(nameof(IsBusy)); OnPropertyChanged(nameof(CanStageLines)); OnPropertyChanged(nameof(CanDiscardFile)); OnPropertyChanged(nameof(CanOperateCurrentBranch)); OnPropertyChanged(nameof(CanStashPreparedFiles)); OnPropertyChanged(nameof(CanCommitStagedChanges)); } }

        private bool _isOperating;
        public bool IsOperating { get => _isOperating; private set { _isOperating = value; OnPropertyChanged(); OnPropertyChanged(nameof(IsBusy)); OnPropertyChanged(nameof(CanStageLines)); OnPropertyChanged(nameof(CanDiscardFile)); OnPropertyChanged(nameof(CanOperateCurrentBranch)); OnPropertyChanged(nameof(CanStashPreparedFiles)); OnPropertyChanged(nameof(CanCommitStagedChanges)); } }
        public bool IsBusy => IsRefreshing || IsOperating;
        public bool CanStageLines => IsWorkingMode && !IsBusy && SelectedFile is { IsReadOnly: false, IsStaged: false, IsConflicted: false, IsStashPrepared: false } && _displayedDiff != null &&
            _displayedDiff.Lines.Any(line => line.Kind is GitDiffLineKind.Added or GitDiffLineKind.Deleted);
        public bool CanDiscardFile => IsWorkingMode && !IsBusy && SelectedFile is { IsReadOnly: false, IsStaged: false, IsConflicted: false, IsStashPrepared: false } &&
            (string.IsNullOrEmpty(SelectedFile.OldPath) || SelectedFile.OldPath == SelectedFile.Path) && !string.IsNullOrEmpty(_displayedDiff?.RawPatch);
        public bool HasStagedChanges => IsWorkingMode && Files.Any(file => file.IsStaged && !file.IsStashPrepared);
        public bool HasUnstagedChanges => IsWorkingMode && Files.Any(file => !file.IsStaged && !file.IsStashPrepared);
        public int StashPreparedFileCount => Files.Where(file => file.IsStashPrepared).Select(file => file.Path).Distinct(StringComparer.Ordinal).Count();
        public bool CanStashPreparedFiles => !IsBusy && StashPreparedFileCount > 0;
        public bool CanCommitStagedChanges => !IsBusy && HasStagedChanges && !Files.Any(file => file.IsStaged && file.IsStashPrepared);
        public string CommitStagedToolTip => Files.Any(file => file.IsStaged && file.IsStashPrepared)
            ? "スタッシュ準備群にステージ済みの変更があります。スタッシュするか準備解除してからコミットしてください。"
            : "ステージ済みの変更をコミット";
        private string _operationStatus = "";
        public string OperationStatus { get => _operationStatus; private set { _operationStatus = value; OnPropertyChanged(); } }
        public void SetOperationStatus(string message) => OperationStatus = message;

        public bool CanUpdateFromBase => CanOperateCurrentBranch && !string.IsNullOrWhiteSpace(BaseBranch);
        public Task UpdateFromBaseAsync() => RunBranchOperationAsync(() => new GitBaseUpdateService(_repositoryPath).UpdateAsync(BaseBranch),
            "ベースブランチを取り込み、退避した変更を復元しました。復元したスタッシュは削除しました。");

        private string _searchText = string.Empty;
        public string SearchText { get => _searchText; set { if (_searchText == value) return; _searchText = value; OnPropertyChanged(); ScheduleSearch(); } }
        private bool _searchAllFiles;
        public bool SearchAllFiles { get => _searchAllFiles; set { if (_searchAllFiles == value) return; _searchAllFiles = value; OnPropertyChanged(); ScheduleSearch(); } }
        public ObservableCollection<GitDiffSearchResult> SearchResults { get; } = new BulkSearchResultCollection();
        private sealed class BulkSearchResultCollection : ObservableCollection<GitDiffSearchResult>
        {
            public void ReplaceAll(IEnumerable<GitDiffSearchResult> results)
            {
                Items.Clear();
                foreach (var result in results) Items.Add(result);
                OnPropertyChanged(new PropertyChangedEventArgs(nameof(Count)));
                OnPropertyChanged(new PropertyChangedEventArgs("Item[]"));
                OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));
            }
        }
        private string _searchStatus = string.Empty;
        public string SearchStatus { get => _searchStatus; private set { _searchStatus = value; OnPropertyChanged(); } }
        private bool _isSearching;
        public bool IsSearching { get => _isSearching; private set { _isSearching = value; OnPropertyChanged(); } }
        public event Action<GitDiffSearchResult> SearchMatchRequested;
        private GitDiffSearchResult _selectedSearchResult;
        public GitDiffSearchResult SelectedSearchResult
        {
            get => _selectedSearchResult;
            set { if (_selectedSearchResult == value) return; _selectedSearchResult = value; OnPropertyChanged(); if (value != null) _ = NavigateSearchResultAsync(value); }
        }

        private void CancelSearch()
        {
            _searchGeneration++;
            var previousSearch = _searchCts;
            _searchCts = null;
            if (previousSearch != null) _ = CancelAndDisposeSearchAsync(previousSearch);
            SelectedSearchResult = null;
            if (SearchResults.Count > 0) SearchResults.Clear();
            SearchStatus = string.Empty;
            IsSearching = false;
        }

        private async void ScheduleSearch()
        {
            CancelSearch();
            if (_disposed || string.IsNullOrWhiteSpace(SearchText)) return;
            int generation = _searchGeneration;
            await Task.Delay(200);
            while (!_disposed && generation == _searchGeneration && IsBusy) await Task.Delay(100);
            if (!_disposed && generation == _searchGeneration) await SearchAsync();
        }

        private static async Task CancelAndDisposeSearchAsync(CancellationTokenSource source)
        {
            try { await source.CancelAsync(); }
            catch (AggregateException) { }
            finally { source.Dispose(); }
        }

        public async Task SearchAsync()
        {
            CancelSearch();
            if (_disposed || IsBusy || string.IsNullOrWhiteSpace(SearchText)) return;
            _searchCts = new CancellationTokenSource();
            var token = _searchCts.Token;
            string query = SearchText;
            var files = SearchAllFiles ? Files.ToArray() : SelectedFile == null ? Array.Empty<GitDiffFile>() : new[] { SelectedFile };
            IsSearching = true;
            SearchStatus = "検索中...";
            try
            {
                var results = await Task.Run(() =>
                {
                    var matches = new List<GitDiffSearchResult>();
                    foreach (var file in files)
                    {
                        token.ThrowIfCancellationRequested();
                        var diff = _service.ReadDiff(file, token, fullContent: true);
                        for (int i = 0; i < diff.Lines.Count; i++)
                        {
                            token.ThrowIfCancellationRequested();
                            var line = diff.Lines[i];
                            if (line.Kind is GitDiffLineKind.Hunk or GitDiffLineKind.Notice) continue;
                            for (int start = 0; start <= line.Text.Length - query.Length;)
                            {
                                int found = line.Text.IndexOf(query, start, StringComparison.OrdinalIgnoreCase);
                                if (found < 0) break;
                                matches.Add(new GitDiffSearchResult(file, i, found, query.Length, line));
                                start = found + query.Length;
                                if (matches.Count >= 5000) return matches;
                            }
                        }
                    }
                    return matches;
                }, token);
                if (token.IsCancellationRequested || _disposed) return;
                ((BulkSearchResultCollection)SearchResults).ReplaceAll(results);
                SearchStatus = results.Count >= 5000 ? "5,000 件まで表示しています。検索語を絞り込んでください。" : $"{results.Count} 件（大文字・小文字を区別しない／差分と前後の行）";
                if (results.Count > 0) SelectedSearchResult = results[0];
            }
            catch (OperationCanceledException) { }
            catch (Exception ex) { if (!token.IsCancellationRequested && !_disposed) SearchStatus = $"検索に失敗しました。{ex.Message}"; }
            finally { if (!token.IsCancellationRequested && !_disposed) IsSearching = false; }
        }

        private async Task NavigateSearchResultAsync(GitDiffSearchResult result)
        {
            if (_disposed || IsBusy || !Files.Contains(result.File)) return;
            bool alreadyDisplayed = SelectedFile == result.File && _displayedFileKey == result.File.Key &&
                result.LineIndex < Lines.Count && Lines[result.LineIndex] == result.Line;
            _selectingSearchMatch = true;
            try { SelectedFile = Files.First(file => file == result.File); }
            finally { _selectingSearchMatch = false; }
            if (!alreadyDisplayed)
                await LoadDiffAsync(SelectedFile, preserveDisplay: _displayedFileKey == result.File.Key, fullContent: true);
            if (_disposed || SelectedSearchResult != result || SelectedFile != result.File) return;
            if (result.LineIndex < Lines.Count && Lines[result.LineIndex] == result.Line) SearchMatchRequested?.Invoke(result);
            else SearchStatus = "差分が変更されました。もう一度検索してください。";
        }

        public void NextSearchResult(int direction)
        {
            if (SearchResults.Count == 0) return;
            int index = SearchResults.IndexOf(SelectedSearchResult);
            SelectedSearchResult = SearchResults[(index + direction + SearchResults.Count) % SearchResults.Count];
        }

        public Task StageFileAsync(GitDiffFile file) => file == null || file.IsReadOnly ? Task.CompletedTask : RunOperationAsync(() => _stageService.StageFileAsync(file), $"{file.FileName} をステージしました。");
        public Task StageAllAsync() => !IsWorkingMode ? Task.CompletedTask : RunOperationAsync(() => _stageService.StageFilesAsync(Files.Where(file => !file.IsStaged && !file.IsStashPrepared).ToArray()), "すべての変更をステージしました。");
        public Task UnstageAllAsync() => !IsWorkingMode ? Task.CompletedTask : RunOperationAsync(() => _stageService.UnstageFilesAsync(Files.Where(file => file.IsStaged && !file.IsStashPrepared).ToArray()), "すべてのステージを解除しました。");

        public void ToggleStashPreparation(GitDiffFile file)
        {
            if (_disposed || IsBusy || !IsWorkingMode || file == null || file.IsReadOnly || file.IsConflicted || !Files.Contains(file)) return;
            var paths = new HashSet<string>(new[] { file.Path }, StringComparer.Ordinal);
            ExpandRelatedPaths(paths, Files);
            if (file.IsStashPrepared) _stashPreparedPaths.ExceptWith(paths);
            else _stashPreparedPaths.UnionWith(paths);
            UpdateStashPreparation($"{file.FileName} をスタッシュ準備群{(file.IsStashPrepared ? "から解除" : "に追加")}しました。");
        }

        public void ClearStashPreparation()
        {
            if (_disposed || IsBusy || !IsWorkingMode) return;
            _stashPreparedPaths.Clear();
            UpdateStashPreparation("スタッシュ準備群を解除しました。");
        }

        private void UpdateStashPreparation(string message)
        {
            string selectedKey = SelectedFile?.Key;
            ApplyFileSnapshot(Files.ToArray());
            SelectedFile = Files.FirstOrDefault(file => file.Key == selectedKey) ?? Files.FirstOrDefault();
            OperationStatus = message;
        }

        public Task StashPreparedFilesAsync()
        {
            if (!CanStashPreparedFiles) return Task.CompletedTask;
            var files = Files.Where(file => file.IsStashPrepared).ToArray();
            int count = StashPreparedFileCount;
            return RunOperationAsync(async () =>
            {
                await _stashService.StashFilesAsync(files);
                _stashPreparedPaths.Clear();
            }, $"{count} ファイルをスタッシュしました。");
        }
        public Task DiscardFileAsync()
        {
            var file = SelectedFile;
            var diff = _displayedDiff;
            return !CanDiscardFile ? Task.CompletedTask : RunOperationAsync(() => _stageService.DiscardFileAsync(file, diff), $"{file.FileName} の未ステージ変更を破棄しました。");
        }
        public Task DiscardFileAsync(GitDiffFile file) => !IsWorkingMode || file == null || file.IsReadOnly ? Task.CompletedTask : RunOperationAsync(async () =>
        {
            var diff = ReferenceEquals(file, SelectedFile) && _displayedDiff != null
                ? _displayedDiff : await Task.Run(() => _service.ReadDiff(file, CancellationToken.None));
            await _stageService.DiscardFileAsync(file, diff);
        }, $"{file.FileName} の未ステージ変更を破棄しました。");
        public Task DiscardLinesAsync(IReadOnlyCollection<int> indices)
        {
            var file = SelectedFile;
            var diff = _displayedDiff;
            return !CanStageLines ? Task.CompletedTask : RunOperationAsync(() => _stageService.DiscardLinesAsync(file, diff, indices), "選択した変更行を破棄しました。");
        }
        public Task UnstageFileAsync(GitDiffFile file) => file == null || file.IsReadOnly ? Task.CompletedTask : RunOperationAsync(() => _stageService.UnstageFileAsync(file), $"{file.FileName} のステージを解除しました。");
        public Task StageLinesAsync(IReadOnlyCollection<int> indices)
        {
            var file = SelectedFile;
            var diff = _displayedDiff;
            return !CanStageLines ? Task.CompletedTask : RunOperationAsync(() => _stageService.StageLinesAsync(file, diff, indices), "選択した変更行をステージしました。");
        }
        public Task CommitStagedAsync(string message)
        {
            if (!CanCommitStagedChanges) { OperationStatus = CommitStagedToolTip; return Task.CompletedTask; }
            return RunOperationAsync(() => _stageService.CommitStagedAsync(message), "ステージ済みの変更をコミットしました。");
        }
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
            CancelSearch();
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
                var mode = Mode;
                string revision = mode == GitDiffMode.Commit ? SelectedCommit?.Sha : BaseBranch;
                var filesTask = Task.Run(() =>
                {
                    try
                    {
                        var files = mode == GitDiffMode.Commit && revision == null ? Array.Empty<GitDiffFile>() : _service.ReadComparisonFiles(mode, revision, token);
                        return (Files: files, Error: (Exception)null);
                    }
                    catch (Exception ex) when (ex is not OperationCanceledException)
                    {
                        return (Files: (IReadOnlyList<GitDiffFile>)Array.Empty<GitDiffFile>(), Error: ex);
                    }
                }, token);
                var branchTask = Task.Run(() => _service.ReadBranchInfo(token), token);
                var commitsTask = Task.Run(() => _service.ReadRecentCommits(token), token);
                await Task.WhenAll(filesTask, branchTask, commitsTask);
                var snapshot = (Result: await filesTask, Branch: await branchTask);
                if (token.IsCancellationRequested || _disposed) return;
                var files = snapshot.Result.Files;
                CurrentBranch = snapshot.Branch.CurrentBranch;
                CurrentBranchName = snapshot.Branch.BranchName;
                SetTrackingInfo(snapshot.Branch);
                var commits = await commitsTask;
                if (!RecentCommits.SequenceEqual(commits))
                {
                    RecentCommits.Clear();
                    foreach (var commit in commits) RecentCommits.Add(commit);
                }
                OnPropertyChanged(nameof(SelectedCommit));
                if (snapshot.Result.Error != null)
                {
                    SelectedFile = null;
                    ApplyFileSnapshot(Array.Empty<GitDiffFile>());
                    Overview = "読み込み失敗";
                    Message = $"ファイル一覧を取得できませんでした。\n{snapshot.Result.Error.Message}";
                    return;
                }
                ApplyFileSnapshot(files);
                var previousSelection = SelectedFile;
                if (previousSelection != null && !ReferenceEquals(previousSelection, initialSelection))
                    selectedKey = previousSelection.Key;
                SelectedFile = Files.FirstOrDefault(file => file.Key == selectedKey) ?? Files.FirstOrDefault();
                if (ReferenceEquals(previousSelection, SelectedFile) && SelectedFile != null)
                    await LoadDiffAsync(SelectedFile, preserveDisplay: true);
                if (SelectedFile == null) Message = mode == GitDiffMode.Uncommitted ? "未コミットの変更はありません。"
                    : mode == GitDiffMode.Commit && SelectedCommit == null ? "直近コミットから表示するコミットを選択してください。" : "表示する差分はありません。";
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                if (token.IsCancellationRequested || _disposed) return;
                SelectedFile = null;
                Files.Clear();
                OnPropertyChanged(nameof(HasStagedChanges));
                OnPropertyChanged(nameof(HasUnstagedChanges));
                OnPropertyChanged(nameof(StashPreparedFileCount));
                OnPropertyChanged(nameof(CanStashPreparedFiles));
                OnPropertyChanged(nameof(CanCommitStagedChanges));
                OnPropertyChanged(nameof(CommitStagedToolTip));
                Overview = "読み込み失敗";
                CurrentBranch = "取得失敗";
                TrackingStatusText = "取得失敗";
                Message = $"ファイル一覧を取得できませんでした。\n{ex.Message}";
            }
            finally
            {
                if (!token.IsCancellationRequested && !_disposed)
                {
                    IsRefreshing = false;
                    if (!string.IsNullOrWhiteSpace(SearchText)) ScheduleSearch();
                }
            }
        }

        private void ApplyFileSnapshot(IReadOnlyList<GitDiffFile> snapshot)
        {
            if (IsWorkingMode)
            {
                _stashPreparedPaths.IntersectWith(snapshot.SelectMany(file => new[] { file.Path, file.OldPath }).Where(path => !string.IsNullOrEmpty(path)));
                ExpandRelatedPaths(_stashPreparedPaths, snapshot);
            }
            var files = snapshot.Select(file => file with { IsStashPrepared = !file.IsReadOnly && _stashPreparedPaths.Contains(file.Path) })
                .OrderBy(file => file.IsStashPrepared ? 2 : file.IsStaged ? 1 : 0)
                .ThenBy(file => _sortOrder == GitDiffSortOrder.Manual ? _manualOrder.GetValueOrDefault(file.Key, int.MaxValue) : 0)
                .ThenBy(file => _sortOrder == GitDiffSortOrder.FileName ? file.FileName : _sortOrder == GitDiffSortOrder.Change ? file.Change : string.Empty, StringComparer.OrdinalIgnoreCase)
                .ThenBy(file => file.Path, StringComparer.OrdinalIgnoreCase).ToArray();
            if (files.Length + Files.Count > 200 && !Files.SequenceEqual(files))
            {
                ((BulkFileCollection)Files).ReplaceAll(files);
            }
            else
            {
            var fileSet = files.ToHashSet();
            for (int i = Files.Count - 1; i >= 0; i--)
                if (!fileSet.Contains(Files[i])) Files.RemoveAt(i);
            for (int i = 0; i < files.Length; i++)
            {
                int existingIndex = Files.IndexOf(files[i]);
                if (existingIndex < 0) Files.Insert(i, files[i]);
                else if (existingIndex != i) Files.Move(existingIndex, i);
            }
            }
            OnPropertyChanged(nameof(HasStagedChanges));
            OnPropertyChanged(nameof(HasUnstagedChanges));
            OnPropertyChanged(nameof(StashPreparedFileCount));
            OnPropertyChanged(nameof(CanStashPreparedFiles));
            OnPropertyChanged(nameof(CanCommitStagedChanges));
            OnPropertyChanged(nameof(CommitStagedToolTip));
            Overview = IsWorkingMode ? $"未ステージ {files.Count(file => !file.IsStaged && !file.IsStashPrepared)} 件  /  ステージ済み {files.Count(file => file.IsStaged && !file.IsStashPrepared)} 件  /  スタッシュ準備 {StashPreparedFileCount} 件"
                : Mode == GitDiffMode.Commit ? $"{SelectedCommit?.Display}  /  {files.Length} ファイル" : $"{BaseBranchDisplay} → 作業ツリー（未コミット含む）  /  {files.Length} ファイル";
        }

        private static void ExpandRelatedPaths(HashSet<string> paths, IEnumerable<GitDiffFile> files)
        {
            int previousCount;
            do
            {
                previousCount = paths.Count;
                foreach (var file in files)
                {
                    if (!paths.Contains(file.Path) && (string.IsNullOrEmpty(file.OldPath) || !paths.Contains(file.OldPath))) continue;
                    paths.Add(file.Path);
                    if (!string.IsNullOrEmpty(file.OldPath)) paths.Add(file.OldPath);
                }
            } while (paths.Count != previousCount);
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

        private async Task LoadDiffAsync(GitDiffFile file, bool preserveDisplay = false, bool fullContent = false)
        {
            _diffCts?.Cancel();
            _diffCts?.Dispose();
            _diffCts = new CancellationTokenSource();
            var token = _diffCts.Token;
            if (!preserveDisplay)
            {
                _displayedDiff = null;
                _displayedFileKey = null;
                OnPropertyChanged(nameof(CanStageLines));
                OnPropertyChanged(nameof(CanDiscardFile));
                Lines = Array.Empty<GitDiffLine>();
                Summary = "";
            }
            if (file == null || _disposed) return;
            if (!preserveDisplay) Message = "差分を読み込んでいます...";
            try
            {
                var previousDiff = preserveDisplay && !fullContent ? _displayedDiff : null;
                var diff = await Task.Run(() => _service.ReadDiff(file, token, previousDiff, fullContent), token);
                if (token.IsCancellationRequested || _disposed) return;
                bool unchanged = preserveDisplay && _displayedDiff != null &&
                    _displayedDiff.RawPatch == diff.RawPatch && _displayedDiff.Message == diff.Message &&
                    _displayedDiff.Added == diff.Added && _displayedDiff.Deleted == diff.Deleted;
                _displayedDiff = diff;
                _displayedFileKey = file.Key;
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
            var search = _searchCts;
            _searchCts = null;
            if (search != null) _ = CancelAndDisposeSearchAsync(search);
        }

        private void OnPropertyChanged([CallerMemberName] string name = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
            if (name is nameof(IsOperating) or nameof(IsRefreshing) or nameof(CurrentBranchName))
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CanUpdateFromBase)));
        }
    }
}
