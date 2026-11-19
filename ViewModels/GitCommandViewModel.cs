using System;
using System.Collections.Generic;
using System.Collections;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Input;
using System.Windows.Data;
using System.Text.RegularExpressions;
using LibGit2Sharp;
using HotKeyCommandApp.Models;
using HotKeyCommandApp.Services;

namespace HotKeyCommandApp.ViewModels
{
    public class GitCommandViewModel : INotifyPropertyChanged
    {
        private string _repositoryPath;
        public string RepositoryPath => _repositoryPath;
        private readonly MainViewModel? _mainVm;
        private readonly GitSettingsManager _gitSettingsManager;
        private readonly List<string> _commandHistory = new();
        private int _historyIndex = 0;
        private CancellationTokenSource? _currentExecutionCts;
        private readonly GitProcessRunner _processRunner = new();

        public GitCommandViewModel(string repositoryPath, MainViewModel? mainVm = null)
        {
            _repositoryPath = repositoryPath;
            _mainVm = mainVm;
            _gitSettingsManager = new GitSettingsManager();
            BranchView = new ListCollectionView(Branches);
            BranchView.Filter = item => item is BranchListItem branch && !branch.IsCurrent &&
                branch.Name.IndexOf(BranchSearchText.Trim(), StringComparison.OrdinalIgnoreCase) >= 0;
            BranchView.CustomSort = new BranchSearchComparer(() => BranchSearchText.Trim());
            LoadGitInfo();
        }

        public void ReloadSettings()
        {
            _gitSettingsManager.LoadSettings();
        }

        public bool ForceDeleteBranches => _gitSettingsManager.CurrentSettings.ForceDeleteBranches;

        public void SetForceDeleteBranches(bool value)
        {
            _gitSettingsManager.LoadSettings();
            _gitSettingsManager.CurrentSettings.ForceDeleteBranches = value;
            _gitSettingsManager.SaveSettings();
        }

        public double MovementSpeed => _mainVm?.MovementSpeed ?? 1200.0;

        private readonly GitConsoleHistory _consoleHistory = new();
        private static readonly TimeSpan ConsoleFlushInterval = TimeSpan.FromMilliseconds(50);
        private readonly object _consoleBufferLock = new();
        private readonly List<string> _pendingConsoleLines = new();
        private bool _isConsoleFlushScheduled = false;
        private readonly object _commandQueueLock = new();
        private readonly Queue<string> _pendingCommands = new();
        private readonly List<string> _waitingCommandInputs = new();
        private bool _isProcessingCommandQueue = false;
        private string _waitingCommandsText = string.Empty;

        public string WaitingCommandsText
        {
            get => _waitingCommandsText;
            private set
            {
                _waitingCommandsText = value;
                OnPropertyChanged();
            }
        }

        private static void RunOnUiThread(Action action)
        {
            var dispatcher = System.Windows.Application.Current?.Dispatcher;
            if (dispatcher == null || dispatcher.CheckAccess())
            {
                action();
            }
            else
            {
                dispatcher.Invoke(action);
            }
        }

        private static Encoding ProcessOutputEncoding => Encoding.Default;

        private void ScheduleConsoleFlush()
        {
            lock (_consoleBufferLock)
            {
                if (_isConsoleFlushScheduled)
                {
                    return;
                }

                _isConsoleFlushScheduled = true;
            }

            _ = Task.Run(async () =>
            {
                await Task.Delay(ConsoleFlushInterval).ConfigureAwait(false);
                FlushPendingConsoleLines();
            });
        }

        private void FlushPendingConsoleLines()
        {
            List<string> linesToAppend;
            lock (_consoleBufferLock)
            {
                linesToAppend = _pendingConsoleLines.ToList();
                _pendingConsoleLines.Clear();
                _isConsoleFlushScheduled = false;
            }

            if (linesToAppend.Count == 0)
            {
                return;
            }

            RunOnUiThread(() =>
            {
                _consoleHistory.AppendLines(linesToAppend);

                UpdateConsoleOutputDisplay();
            });
        }

        private void ResetPendingConsoleLines()
        {
            lock (_consoleBufferLock)
            {
                _pendingConsoleLines.Clear();
                _isConsoleFlushScheduled = false;
            }
        }

        private void UpdateConsoleOutputDisplay()
        {
            ConsoleOutput = _consoleHistory.Render(IsHistoryMode);
        }

        private void UpdateWaitingCommandsText()
        {
            WaitingCommandsText = _waitingCommandInputs.Count == 0
                ? string.Empty
                : "[待機中のコマンド]\n" + string.Join("\n", _waitingCommandInputs);
        }

        private void UpdateCurrentCommandContext(string commandName, string? status)
        {
            CurrentCommandName = commandName;
            CurrentCommandStatus = status ?? string.Empty;
        }

        private void UpdateCurrentCommandStatus(string status)
        {
            RunOnUiThread(() =>
            {
                CurrentCommandStatus = status;
            });
        }

        public void SetConsoleOutput(string header, string content, string? status = null)
        {
            RunOnUiThread(() =>
            {
                ResetPendingConsoleLines();
                _consoleHistory.SetEntry(header, content);
                UpdateCurrentCommandContext(header, status);
                UpdateConsoleOutputDisplay();
            });
        }

        private void ReplaceConsoleOutput(string header, string content, string? status = null)
        {
            RunOnUiThread(() =>
            {
                ResetPendingConsoleLines();
                _consoleHistory.SetEntry(header, content, replaceCurrent: true);
                UpdateCurrentCommandContext(header, status);
                UpdateConsoleOutputDisplay();
            });
        }

        private void AppendConsoleOutputLine(string line)
        {
            lock (_consoleBufferLock)
            {
                _pendingConsoleLines.Add(line);
            }

            ScheduleConsoleFlush();
        }

        private void AppendWaitingCommandLine(string rawInput)
        {
            RunOnUiThread(() =>
            {
                _waitingCommandInputs.Add(rawInput);
                UpdateWaitingCommandsText();
                UpdateConsoleOutputDisplay();
            });
        }

        private void RemoveWaitingCommandLine(string rawInput)
        {
            RunOnUiThread(() =>
            {
                int index = _waitingCommandInputs.FindIndex(cmd => string.Equals(cmd, rawInput, StringComparison.OrdinalIgnoreCase));
                if (index >= 0)
                {
                    _waitingCommandInputs.RemoveAt(index);
                }
                UpdateWaitingCommandsText();
                UpdateConsoleOutputDisplay();
            });
        }

        private bool IsImmediateCancelCommand(string rawInput)
        {
            string input = rawInput.Trim();
            if (string.Equals(input, "cancel", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            string[] parts = input.Split(new[] { ' ' }, 2, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0)
            {
                return false;
            }

            string cmdWord = parts[0];
            string cmdArgs = parts.Length > 1 ? parts[1] : string.Empty;
            var matchedAlias = _gitSettingsManager.CurrentSettings.AllAliases
                .FirstOrDefault(a => string.Equals(a.Alias, cmdWord, StringComparison.OrdinalIgnoreCase));

            if (matchedAlias == null || string.IsNullOrWhiteSpace(matchedAlias.TargetCommand))
            {
                return false;
            }

            if (matchedAlias.TargetCommand.Contains("{"))
            {
                return false;
            }

            input = matchedAlias.TargetCommand + (string.IsNullOrEmpty(cmdArgs) ? string.Empty : " " + cmdArgs);
            return string.Equals(input.Trim(), "cancel", StringComparison.OrdinalIgnoreCase);
        }

        private bool _isHistoryMode = false;
        public bool IsHistoryMode
        {
            get => _isHistoryMode;
            set
            {
                _isHistoryMode = value;
                OnPropertyChanged();
                if (!_isHistoryMode)
                {
                    ConsoleOutput = _consoleHistory.LatestBody;
                }
            }
        }

        private bool _isMoveMode = false;
        public bool IsMoveMode
        {
            get => _isMoveMode;
            set { _isMoveMode = value; OnPropertyChanged(); }
        }

        public ObservableCollection<BranchListItem> Branches { get; } = new();
        public ListCollectionView BranchView { get; }
        public BranchListItem CurrentBranchItem => Branches.FirstOrDefault(branch => branch.IsCurrent);
        public ObservableCollection<StashListItem> Stashes { get; } = new();

        private string _branchSearchText = string.Empty;
        public string BranchSearchText
        {
            get => _branchSearchText;
            set
            {
                if (_branchSearchText == value) return;
                _branchSearchText = value ?? string.Empty;
                OnPropertyChanged();
                BranchView.Refresh();
            }
        }

        private bool _isStashMode;
        public bool IsStashMode
        {
            get => _isStashMode;
            private set
            {
                _isStashMode = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(AddOperationToolTip));
            }
        }

        public string AddOperationToolTip => IsStashMode ? "スタッシュを作成" : "ブランチを作成";

        private bool _isOperateMode;
        public bool IsOperateMode
        {
            get => _isOperateMode;
            set { _isOperateMode = value; OnPropertyChanged(); }
        }

        private bool _isBranchOperationBusy;
        public bool IsBranchOperationBusy
        {
            get => _isBranchOperationBusy;
            private set { _isBranchOperationBusy = value; OnPropertyChanged(); }
        }

        private string _branchOperationMessage = string.Empty;
        private int _branchTrackingRefreshVersion;
        public string BranchOperationMessage
        {
            get => _branchOperationMessage;
            private set { _branchOperationMessage = value; OnPropertyChanged(); }
        }

        private void RefreshBranches()
        {
            using var repository = new Repository(_repositoryPath);
            var branches = repository.Branches.Where(branch => !branch.IsRemote)
                .OrderBy(branch => branch.FriendlyName, StringComparer.OrdinalIgnoreCase)
                .Select(branch => (Name: branch.FriendlyName, IsCurrent: branch.IsCurrentRepositoryHead))
                .ToList();
            var names = new HashSet<string>(branches.Select(branch => branch.Name), StringComparer.Ordinal);
            for (int i = Branches.Count - 1; i >= 0; i--)
            {
                if (!names.Contains(Branches[i].Name)) Branches.RemoveAt(i);
            }
            for (int i = 0; i < branches.Count; i++)
            {
                var branch = branches[i];
                int existingIndex = -1;
                for (int j = i; j < Branches.Count; j++)
                {
                    if (Branches[j].Name == branch.Name) { existingIndex = j; break; }
                }
                if (existingIndex < 0) Branches.Insert(i, new BranchListItem(branch.Name, branch.IsCurrent));
                else
                {
                    if (existingIndex != i) Branches.Move(existingIndex, i);
                    Branches[i].SetCurrent(branch.IsCurrent);
                }
            }
            CurrentBranch = GetCurrentBranchName();
            OnPropertyChanged(nameof(CurrentBranchItem));
            BranchView.Refresh();
            _ = RefreshBranchTrackingAsync();
        }

        private async Task RefreshBranchTrackingAsync()
        {
            int refreshVersion = ++_branchTrackingRefreshVersion;
            string repositoryPath = _repositoryPath;
            var items = Branches.ToArray();
            try
            {
                var statuses = await Task.Run(() =>
                {
                    using var repository = new Repository(repositoryPath);
                    var result = new List<(BranchListItem Item, string Text, string ToolTip)>();
                    foreach (var item in items)
                    {
                        if (Volatile.Read(ref _branchTrackingRefreshVersion) != refreshVersion) break;
                        var branch = repository.Branches[item.Name];
                        var trackedBranch = branch?.TrackedBranch;
                        if (trackedBranch?.Tip == null)
                        {
                            result.Add((item, "追跡先なし", "追跡先が未設定か、取得済みのリモート情報に追跡先がありません。"));
                            continue;
                        }

                        var tracking = branch.TrackingDetails;
                        int? ahead = tracking.AheadBy;
                        int? behind = tracking.BehindBy;
                        string text = ahead.HasValue && behind.HasValue ? $"↑{ahead.Value} ↓{behind.Value}" : "差分不明";
                        string toolTip = $"追跡先: {trackedBranch.FriendlyName}\n↑: ローカル側だけにあるコミット数\n↓: リモート側だけにあるコミット数\n最終 fetch / pull で取得したリモート情報に基づきます。";
                        result.Add((item, text, toolTip));
                    }
                    return result;
                });

                if (_branchTrackingRefreshVersion != refreshVersion) return;
                foreach (var status in statuses) status.Item.SetTrackingStatus(status.Text, status.ToolTip);
            }
            catch
            {
                if (_branchTrackingRefreshVersion != refreshVersion) return;
                foreach (var item in items) item.SetTrackingStatus("差分取得失敗", "リモートとの差分を取得できませんでした。一覧を開き直して再実行してください。");
            }
        }

        private void RefreshStashes()
        {
            using var repository = new Repository(_repositoryPath);
            var stashes = repository.Stashes.Select((stash, index) => new StashListItem($"stash@{{{index}}}", stash.Message, stash.WorkTree.Id.Sha)).ToList();
            for (int i = 0; i < stashes.Count; i++)
            {
                var stash = stashes[i];
                if (i >= Stashes.Count) Stashes.Add(stash);
                else if (Stashes[i].Name != stash.Name || Stashes[i].CommitId != stash.CommitId || Stashes[i].Message != stash.Message)
                    Stashes[i] = stash;
            }
            while (Stashes.Count > stashes.Count) Stashes.RemoveAt(Stashes.Count - 1);
        }

        public async Task<bool> OperateStashAsync(StashListItem? stash, bool delete, string? message = null)
        {
            if (IsBranchOperationBusy) return false;
            IsBranchOperationBusy = true;
            BranchOperationMessage = "実行中...";
            var output = new List<string>();
            try
            {
                if (stash != null)
                {
                    using var repository = new Repository(_repositoryPath);
                    var current = repository.Stashes.Select((item, index) => (Item: item, Name: $"stash@{{{index}}}"))
                        .FirstOrDefault(entry => entry.Name == stash.Name).Item;
                    if (current == null || current.WorkTree.Id.Sha != stash.CommitId || current.Message != stash.Message)
                    {
                        RefreshStashes();
                        BranchOperationMessage = "スタッシュ一覧が変更されました。一覧を確認して再実行してください。";
                        return false;
                    }
                }

                var psi = new ProcessStartInfo
                {
                    FileName = "git", WorkingDirectory = _repositoryPath, UseShellExecute = false,
                    CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true
                };
                psi.ArgumentList.Add("stash");
                psi.ArgumentList.Add(stash == null ? "push" : delete ? "drop" : "apply");
                if (stash != null) psi.ArgumentList.Add(stash.Name);
                else if (!string.IsNullOrWhiteSpace(message))
                {
                    psi.ArgumentList.Add("-m");
                    psi.ArgumentList.Add(message);
                }

                int previousCount = Stashes.Count;
                int exitCode = await _processRunner.RunAsync(psi, CancellationToken.None,
                    line => { lock (output) output.Add(line); }, line => { lock (output) output.Add(line); });
                string details = string.Join("\n", output);
                RefreshStashes();
                if (exitCode != 0)
                    BranchOperationMessage = $"操作に失敗しました。\n{details}";
                else if (stash == null && Stashes.Count == previousCount)
                    BranchOperationMessage = "保存できる追跡済みファイルの変更がありません。";
                else
                    BranchOperationMessage = stash == null ? "スタッシュを作成しました。" : delete ? $"{stash.Name} を削除しました。" : $"{stash.Name} を適用しました。";
                return exitCode == 0 && (stash != null || Stashes.Count != previousCount);
            }
            catch (Exception ex)
            {
                BranchOperationMessage = $"操作に失敗しました。\n{ex.Message}";
                return false;
            }
            finally { IsBranchOperationBusy = false; }
        }

        public Task OperateBranchAsync(BranchListItem branch, bool delete, bool forceDelete = false) =>
            RunBranchOperationAsync(branch.Name, delete ? "delete" : "switch", forceDelete);

        public Task<bool> CreateBranchAsync(string name) => RunBranchOperationAsync(name, "create");

        public Task<bool> PullCurrentBranchAsync() => RunBranchOperationAsync(GetCurrentBranchName(), "pull");

        public Task<bool> RenameCurrentBranchAsync(string newName) => RunBranchOperationAsync(GetCurrentBranchName(), "rename", newName: newName);

        public Task<bool> DeleteBranchesAsync(IEnumerable<BranchListItem> branches, bool forceDelete = false)
        {
            var names = branches.Select(branch => branch.Name).Distinct(StringComparer.Ordinal).ToArray();
            return names.Length == 0 ? Task.FromResult(false)
                : RunBranchOperationAsync(names.Length == 1 ? names[0] : $"{names.Length} 件のブランチ", "delete", forceDelete, branchNames: names);
        }

        public Task<bool> CommitAllAsync(string message) => RunRepositoryOperationAsync(false, message);

        public Task<bool> PushCurrentBranchAsync() => RunRepositoryOperationAsync(true);

        private async Task<bool> RunRepositoryOperationAsync(bool push, string message = null)
        {
            if (IsBranchOperationBusy) return false;
            if (!push && string.IsNullOrWhiteSpace(message))
            {
                BranchOperationMessage = "コミットメッセージを入力してください。";
                return false;
            }

            IsBranchOperationBusy = true;
            BranchOperationMessage = "実行中...";
            using var executionCts = new CancellationTokenSource();
            _currentExecutionCts = executionCts;
            var output = new List<string>();
            async Task<int> RunGitAsync(params string[] arguments)
            {
                var psi = new ProcessStartInfo
                {
                    FileName = "git", WorkingDirectory = _repositoryPath, UseShellExecute = false,
                    CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true
                };
                foreach (string argument in arguments) psi.ArgumentList.Add(argument);
                return await _processRunner.RunAsync(psi, executionCts.Token,
                    line => { lock (output) output.Add(line); }, line => { lock (output) output.Add(line); });
            }

            try
            {
                int exitCode;
                string branchName = GetCurrentBranchName();
                if (push)
                {
                    string remoteName, destination, source;
                    bool setUpstream;
                    using (var repository = new Repository(_repositoryPath))
                    {
                        if (repository.Info.IsHeadDetached || repository.Head.Tip == null)
                        {
                            BranchOperationMessage = "プッシュするブランチとコミットが必要です。";
                            return false;
                        }
                        var head = repository.Head;
                        branchName = head.FriendlyName;
                        source = head.CanonicalName;
                        remoteName = head.RemoteName;
                        destination = head.UpstreamBranchCanonicalName;
                        setUpstream = string.IsNullOrWhiteSpace(remoteName) || string.IsNullOrWhiteSpace(destination);
                        if (setUpstream)
                        {
                            var remotes = repository.Network.Remotes.Select(remote => remote.Name).ToArray();
                            remoteName = remotes.Contains("origin") ? "origin" : remotes.Length == 1 ? remotes[0] : null;
                            destination = source;
                        }
                    }
                    if (string.IsNullOrWhiteSpace(remoteName))
                    {
                        BranchOperationMessage = "プッシュ先を特定できません。リモートまたはブランチの追跡先を設定してください。";
                        return false;
                    }
                    var arguments = new List<string> { "push" };
                    if (setUpstream) arguments.Add("--set-upstream");
                    arguments.Add("--");
                    arguments.Add(remoteName);
                    arguments.Add($"{source}:{destination}");
                    exitCode = await RunGitAsync(arguments.ToArray());
                }
                else
                {
                    exitCode = await RunGitAsync("add", "--all");
                    if (exitCode == 0)
                    {
                        int diffExitCode = await RunGitAsync("diff", "--cached", "--quiet", "--exit-code");
                        if (diffExitCode == 0)
                        {
                            BranchOperationMessage = "コミットする変更はありません。";
                            RefreshBranches();
                            return false;
                        }
                        exitCode = diffExitCode == 1 ? await RunGitAsync("commit", "-m", message) : diffExitCode;
                    }
                }
                BranchOperationMessage = exitCode == 0
                    ? push ? $"{branchName} をプッシュしました。" : "現在の変更をすべてコミットしました。"
                    : $"操作に失敗しました。\n{string.Join("\n", output)}";
                RefreshBranches();
                return exitCode == 0;
            }
            catch (OperationCanceledException) { BranchOperationMessage = "操作をキャンセルしました。"; return false; }
            catch (Exception ex) { BranchOperationMessage = $"操作に失敗しました。\n{ex.Message}"; return false; }
            finally
            {
                if (ReferenceEquals(_currentExecutionCts, executionCts)) _currentExecutionCts = null;
                IsBranchOperationBusy = false;
            }
        }

        private async Task<bool> RunBranchOperationAsync(string name, string operation, bool forceDelete = false,
            string newName = null, IReadOnlyList<string> branchNames = null)
        {
            if (IsBranchOperationBusy) return false;
            IsBranchOperationBusy = true;
            BranchOperationMessage = "実行中...";
            var output = new List<string>();
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = "git",
                    WorkingDirectory = _repositoryPath,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                };
                psi.ArgumentList.Add(operation switch { "switch" => "switch", "pull" => "pull", _ => "branch" });
                if (operation != "pull")
                {
                    if (operation == "delete") psi.ArgumentList.Add(forceDelete ? "-D" : "-d");
                    if (operation == "rename") psi.ArgumentList.Add("-m");
                    psi.ArgumentList.Add("--");
                    foreach (string branchName in branchNames ?? new[] { name }) psi.ArgumentList.Add(branchName);
                    if (operation == "rename") psi.ArgumentList.Add(newName);
                }
                int exitCode = await _processRunner.RunAsync(psi, CancellationToken.None,
                    line => { lock (output) output.Add(line); },
                    line => { lock (output) output.Add(line); });
                string details = string.Join("\n", output);
                BranchOperationMessage = exitCode == 0
                    ? operation switch
                    {
                        "create" => $"{name} を作成しました。",
                        "delete" => $"{name} を削除しました。",
                        "pull" => $"{name} をプルしました。",
                        "rename" => $"{name} を {newName} に変更しました。",
                        _ => $"{name} に移動しました。"
                    }
                    : $"操作に失敗しました。\n{details}";
                RefreshBranches();
                return exitCode == 0;
            }
            catch (Exception ex)
            {
                BranchOperationMessage = $"操作に失敗しました。\n{ex.Message}";
                return false;
            }
            finally
            {
                IsBranchOperationBusy = false;
            }
        }

        private bool _isFrontMode = false;
        public bool IsFrontMode
        {
            get => _isFrontMode;
            set { _isFrontMode = value; OnPropertyChanged(); }
        }

        private string _repositoryName = string.Empty;
        public string RepositoryName
        {
            get => _repositoryName;
            set { _repositoryName = value; OnPropertyChanged(); }
        }

        private string _parentBranch = string.Empty;
        public string ParentBranch
        {
            get => _parentBranch;
            set
            {
                _parentBranch = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(IsCurrentBranchBase));
            }
        }

        private string _currentBranch = string.Empty;
        public string CurrentBranch
        {
            get => _currentBranch;
            set
            {
                _currentBranch = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(IsCurrentBranchBase));
            }
        }

        public bool IsCurrentBranchBase =>
            !string.IsNullOrWhiteSpace(CurrentBranch) &&
            !string.IsNullOrWhiteSpace(ParentBranch) &&
            string.Equals(CurrentBranch, ParentBranch, StringComparison.OrdinalIgnoreCase);

        private string _currentCommandName = string.Empty;
        public string CurrentCommandName
        {
            get => _currentCommandName;
            set { _currentCommandName = value; OnPropertyChanged(); }
        }

        private string _currentCommandStatus = string.Empty;
        public string CurrentCommandStatus
        {
            get => _currentCommandStatus;
            set { _currentCommandStatus = value; OnPropertyChanged(); }
        }

        private string _consoleOutput = string.Empty;
        public string ConsoleOutput
        {
            get => _consoleOutput;
            set { _consoleOutput = value; OnPropertyChanged(); }
        }

        private string _inputText = string.Empty;
        public string InputText
        {
            get => _inputText;
            set { _inputText = value; OnPropertyChanged(); }
        }

        private ICommand? _executeCommand;
        public ICommand ExecuteCommand => _executeCommand ??= new RelayCommand<object>(_ => ExecuteGitCommand());

        private async void ExecuteGitCommand()
        {
            if (string.IsNullOrWhiteSpace(InputText)) return;

            string rawInput = InputText.Trim();
            InputText = string.Empty;

            if (IsImmediateCancelCommand(rawInput))
            {
                if (!_processRunner.IsRunning && _currentExecutionCts == null)
                {
                    SetConsoleOutput("キャンセル", "実行中のGit操作はありません");
                }
                else
                {
                    CancelCurrentExecution();
                }

                return;
            }

            if (_commandHistory.Count == 0 || _commandHistory.Last() != rawInput)
            {
                _commandHistory.Add(rawInput);
            }
            _historyIndex = _commandHistory.Count;

            if (string.Equals(rawInput, "diff", StringComparison.OrdinalIgnoreCase))
            {
                RequestDiffWindow?.Invoke();
                return;
            }

            bool shouldStartQueueProcessor = false;
            lock (_commandQueueLock)
            {
                _pendingCommands.Enqueue(rawInput);
                if (_isProcessingCommandQueue)
                {
                    AppendWaitingCommandLine(rawInput);
                    return;
                }

                _isProcessingCommandQueue = true;
                shouldStartQueueProcessor = true;
            }

            if (shouldStartQueueProcessor)
            {
                await ProcessQueuedCommandsAsync();
            }
        }

        public void CancelCurrentExecution()
        {
            _currentExecutionCts?.Cancel();
            _processRunner.Cancel();
        }

        private void LoadGitInfo(bool includeStatus = true)
        {
            if (string.IsNullOrEmpty(_repositoryPath) || !Directory.Exists(_repositoryPath))
            {
                RepositoryName = "Invalid Repository Path";
                return;
            }

            try
            {
                var mapping = _gitSettingsManager.CurrentSettings.RepositoryNameMappings.FirstOrDefault(m => string.Equals(m.Path, _repositoryPath, StringComparison.OrdinalIgnoreCase));
                if (mapping != null && !string.IsNullOrWhiteSpace(mapping.OverwrittenName))
                {
                    RepositoryName = mapping.OverwrittenName;
                }
                else
                {
                    RepositoryName = new DirectoryInfo(_repositoryPath).Name;
                }
                CurrentBranch = GetCurrentBranchName();
                ParentBranch = mapping != null ? mapping.BaseBranch : string.Empty;

                if (includeStatus)
                {
                    string status = GetGitStatusSummary();
                    SetConsoleOutput("Git Status", status);
                }
            }
            catch
            {
                RepositoryName = "Error reading Git info";
                SetConsoleOutput("エラー", "Git情報の読み込みに失敗しました");
            }
        }

        private async Task ProcessQueuedCommandsAsync()
        {
            try
            {
                while (true)
                {
                    string? nextInput;
                    lock (_commandQueueLock)
                    {
                        if (_pendingCommands.Count == 0)
                        {
                            _isProcessingCommandQueue = false;
                            return;
                        }

                        nextInput = _pendingCommands.Dequeue();
                    }

                    if (nextInput != null)
                    {
                        RemoveWaitingCommandLine(nextInput);
                    }

                    await ProcessSingleGitCommandAsync(nextInput);
                }
            }
            finally
            {
                lock (_commandQueueLock)
                {
                    if (_pendingCommands.Count == 0)
                    {
                        _isProcessingCommandQueue = false;
                    }
                }
            }
        }

        private bool TryEnterOperateMode(string input)
        {
            bool stashMode = string.Equals(input.Trim(), "stash list", StringComparison.OrdinalIgnoreCase);
            if (!stashMode && !string.Equals(input.Trim(), "operate mode", StringComparison.OrdinalIgnoreCase)) return false;

            lock (_commandQueueLock)
            {
                if (_pendingCommands.Count > 0)
                {
                    SetConsoleOutput(stashMode ? "スタッシュ操作" : "ブランチ操作", "待機中のコマンドが終了してから実行してください。", "エラー");
                    return true;
                }
            }

            try
            {
                if (stashMode) RefreshStashes();
                else
                {
                    BranchSearchText = string.Empty;
                    RefreshBranches();
                }
                IsStashMode = stashMode;
                BranchOperationMessage = stashMode && Stashes.Count == 0 ? "スタッシュがありません。" : !stashMode && Branches.Count == 0 ? "ローカルブランチがありません。"
                    : stashMode ? "上下: 選択　左右: 操作選択　Enter: 実行　Esc: 終了"
                    : "上下: 選択　Shift+上下: 範囲選択　左右: 操作選択　Enter: 実行　Delete: 削除　Esc: 終了";
                IsOperateMode = true;
            }
            catch (Exception ex)
            {
                SetConsoleOutput(stashMode ? "スタッシュ操作" : "ブランチ操作", ex.Message, "エラー");
            }
            return true;
        }

        private async Task ProcessSingleGitCommandAsync(string rawInput)
        {
            if (TryEnterOperateMode(rawInput)) return;

            if (string.Equals(rawInput, "cancel", StringComparison.OrdinalIgnoreCase))
            {
                if (!_processRunner.IsRunning && _currentExecutionCts == null)
                {
                    SetConsoleOutput("キャンセル", "実行中のGit操作はありません");
                }
                else
                {
                    CancelCurrentExecution();
                }

                return;
            }
            string input = rawInput;
            
            // 先頭の単語を取り出してエイリアス/関数の展開を最初に行う
            string[] parts = input.Split(new[] { ' ' }, 2, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length > 0)
            {
                string cmdWord = parts[0];
                string cmdArgs = parts.Length > 1 ? parts[1] : "";

                // 先に関数チェックを行う
                var matchedFunction = _gitSettingsManager.CurrentSettings.Functions.FirstOrDefault(f => string.Equals(f.Name, cmdWord, StringComparison.OrdinalIgnoreCase));
                if (matchedFunction != null && matchedFunction.Commands != null && matchedFunction.Commands.Count > 0)
                {
                    using var functionExecutionCts = new CancellationTokenSource();
                    _currentExecutionCts = functionExecutionCts;
                    try
                    {
                        await ExecuteGitFunctionAsync(matchedFunction, cmdArgs);
                    }
                    catch (OperationCanceledException)
                    {
                        UpdateCurrentCommandStatus("キャンセル");
                    }
                    finally
                    {
                        if (ReferenceEquals(_currentExecutionCts, functionExecutionCts))
                        {
                            _currentExecutionCts = null;
                        }
                    }

                    return;
                }

                // 関数でなければエイリアスチェック
                var matchedAlias = _gitSettingsManager.CurrentSettings.AllAliases.FirstOrDefault(a => string.Equals(a.Alias, cmdWord, StringComparison.OrdinalIgnoreCase));
                if (matchedAlias != null && !string.IsNullOrWhiteSpace(matchedAlias.TargetCommand))
                {
                    string targetCmd = matchedAlias.TargetCommand;
                    int requiredArguments = GitCommandText.GetRequiredArgumentCount(new[] { targetCmd });

                    List<string> parsedArgs = string.IsNullOrWhiteSpace(cmdArgs) ? new List<string>() : GitCommandText.ParseArguments(cmdArgs);

                    if (requiredArguments > 0)
                    {
                        if (parsedArgs.Count < requiredArguments)
                        {
                            var tempCommand = new CommandEntry
                            {
                                Name = matchedAlias.Alias,
                                Value = targetCmd
                            };

                            string? argString = RequestArgumentInput?.Invoke(tempCommand, $"[{matchedAlias.Alias}] の引数を入力してください:");
                            if (argString == null) return; // ユーザーがキャンセルした場合は実行中断

                            parsedArgs = GitCommandText.ParseArguments(argString);
                        }

                        targetCmd = GitCommandText.ReplaceArguments(targetCmd, parsedArgs);
                        input = targetCmd;
                    }
                    else
                    {
                        // エイリアスの置換結果で input を上書きする
                        input = targetCmd + (string.IsNullOrEmpty(cmdArgs) ? "" : " " + cmdArgs);
                    }
                }

                string selectContextName = matchedAlias?.Alias ?? cmdWord;
                ResolveSelectPlaceholders(ref input, selectContextName);

                if (string.Equals(input.Trim(), "cancel", StringComparison.OrdinalIgnoreCase))
                {
                    if (!_processRunner.IsRunning && _currentExecutionCts == null)
                    {
                        SetConsoleOutput("キャンセル", "実行中のGit操作はありません");
                    }
                    else
                    {
                        CancelCurrentExecution();
                    }

                    return;
                }
            }

            if (TryEnterOperateMode(input)) return;

            // 展開後の文字列で {base} プレースホルダーをベースブランチ名に置換
            if (input.Contains("{base}"))
            {
                string baseBranch = string.IsNullOrWhiteSpace(ParentBranch) ? "main" : ParentBranch;
                input = input.Replace("{base}", baseBranch);
            }

            // {current} プレースホルダーを現在のブランチ名に置換
            if (input.Contains("{current}"))
            {
                string currentBranch = GetCurrentBranchName();
                if (string.IsNullOrEmpty(currentBranch) || currentBranch == "unknown")
                {
                    currentBranch = CurrentBranch;
                }
                input = input.Replace("{current}", currentBranch);
            }

            if (input.StartsWith("base ", StringComparison.OrdinalIgnoreCase))
            {
                string targetBase = input.Substring(5).Trim();
                if (!string.IsNullOrWhiteSpace(targetBase))
                {
                    ParentBranch = targetBase;
                    
                    var mapping = _gitSettingsManager.CurrentSettings.RepositoryNameMappings.FirstOrDefault(m => string.Equals(m.Path, _repositoryPath, StringComparison.OrdinalIgnoreCase));
                    if (mapping != null)
                    {
                        mapping.BaseBranch = targetBase;
                    }
                    else
                    {
                        _gitSettingsManager.CurrentSettings.RepositoryNameMappings.Add(new RepositoryNameMapping
                        {
                            Path = _repositoryPath,
                            BaseBranch = targetBase
                        });
                    }
                    _gitSettingsManager.SaveSettings();

                    SetConsoleOutput("ベースブランチ設定", $"ベースブランチを '{ParentBranch}' に設定しました。(設定ファイルに保存されました)", "完了");
                }
                return;
            }



            if (string.Equals(input, "front", StringComparison.OrdinalIgnoreCase))
            {
                IsFrontMode = true;
                SetConsoleOutput("最前面固定モード", "Gitウィンドウを常に最前面に表示します。\n・Esc: 解除", "完了");
                return;
            }

            if (string.Equals(input, "history", StringComparison.OrdinalIgnoreCase))
            {
                IsHistoryMode = true;
                ConsoleOutput = _consoleHistory.Render(historyMode: true);
                return;
            }

            // 移動コマンド (cd または repo) であるか検知
            if (input.StartsWith("cd ", StringComparison.OrdinalIgnoreCase) || 
                input.StartsWith("repo ", StringComparison.OrdinalIgnoreCase))
            {
                string target = input.Substring(input.IndexOf(' ') + 1).Trim();
                string? newRepoPath = null;

                // 絶対パスとして直接存在するか
                if (Path.IsPathRooted(target) && Directory.Exists(target))
                {
                    newRepoPath = Path.GetFullPath(target);
                }
                else
                {
                    // カレントリポジトリからの相対パス解決 (cd .. など)
                    string combined = Path.GetFullPath(Path.Combine(_repositoryPath, target));
                    if (Directory.Exists(combined))
                    {
                        newRepoPath = combined;
                    }
                }

                if (!string.IsNullOrEmpty(newRepoPath))
                {
                    _repositoryPath = newRepoPath;
                    LoadGitInfo(includeStatus: false);
                    SetConsoleOutput("リポジトリ移動", GetRepositoryDisplayName(_repositoryPath), "完了");
                }
                else
                {
                    SetConsoleOutput("エラー", $"指定された移動先が見つかりません: {target}");
                }
                return;
            }

            // 通常の OS コマンドとしてそのまま実行する (git の自動付与なし)
            var configService = new ConfigService();
            var appSettings = configService.LoadSettings();
            var constants = appSettings?.Constants ?? new List<ConstantEntry>();
            string command = ConstantReplacer.ReplaceConstants(input, constants);
            SetConsoleOutput(command, string.Empty, "実行中");

            using var commandExecutionCts = new CancellationTokenSource();
            _currentExecutionCts = commandExecutionCts;

            try
            {
                int exitCode = await Task.Run(async () =>
                {
                    bool isGitDirect = command.StartsWith("git ", StringComparison.OrdinalIgnoreCase)
                                       && !command.Contains("|")
                                       && !command.Contains(">")
                                       && !command.Contains("<")
                                       && !command.Contains("&");

                    var psi = new ProcessStartInfo
                    {
                        WorkingDirectory = _repositoryPath,
                        UseShellExecute = false,
                        CreateNoWindow = true,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        StandardOutputEncoding = ProcessOutputEncoding,
                        StandardErrorEncoding = ProcessOutputEncoding
                    };

                    if (isGitDirect)
                    {
                        psi.FileName = "git";
                        psi.Arguments = command.Substring(4).Trim();
                    }
                    else
                    {
                        psi.FileName = "cmd.exe";
                        psi.Arguments = $"/c {command}";
                    }

                    NativeMethods.AllowSetForegroundWindow(NativeMethods.ASFW_ANY);
                    int exitCode = await _processRunner.RunAsync(
                        psi,
                        _currentExecutionCts?.Token ?? CancellationToken.None,
                        line =>
                        {
                            AppendConsoleOutputLine(line);
                        },
                        line =>
                        {
                            AppendConsoleOutputLine(line);
                        });

                    return exitCode;
                });

                FlushPendingConsoleLines();

                UpdateCurrentCommandStatus(exitCode == 0 ? "完了" : "エラー");

                string currentBranch = await Task.Run(GetCurrentBranchName);
                RunOnUiThread(() => CurrentBranch = currentBranch);
            }
            catch (OperationCanceledException)
            {
                UpdateCurrentCommandStatus("キャンセル");
            }
            catch (Exception ex)
            {
                SetConsoleOutput("実行エラー", ex.Message, "エラー");
            }
            finally
            {
                if (ReferenceEquals(_currentExecutionCts, commandExecutionCts))
                {
                    _currentExecutionCts = null;
                }
            }
        }

        private async Task ExecuteGitFunctionAsync(GitFunctionEntry function, string cmdArgs)
        {
            int requiredArguments = GitCommandText.GetRequiredArgumentCount(function.Commands);

            List<string> parsedArgs = string.IsNullOrWhiteSpace(cmdArgs) ? new List<string>() : GitCommandText.ParseArguments(cmdArgs);

            if (parsedArgs.Count < requiredArguments)
            {
                var tempCommand = new CommandEntry
                {
                    Name = function.Name,
                    Value = string.Join("\n", function.Commands)
                };

                string? argString = RequestArgumentInput?.Invoke(tempCommand, $"[{function.Name}] の引数を入力してください:");
                if (argString == null) return; // キャンセルされた場合は実行中断

                parsedArgs = GitCommandText.ParseArguments(argString);
            }

            SetConsoleOutput($"関数: {function.Name}", string.Empty, "実行中");

            try
            {
                await Task.Run(async () =>
                {
                    bool isSuccess = true;
                    foreach (var cmdTemplate in function.Commands)
                    {
                        string commandToRun = cmdTemplate.Trim();

                        // 先頭の単語を取り出してエイリアス展開を最初に行う
                        string[] parts = commandToRun.Split(new[] { ' ' }, 2, StringSplitOptions.RemoveEmptyEntries);
                        if (parts.Length > 0)
                        {
                            string subCmdWord = parts[0];
                            string subCmdArgs = parts.Length > 1 ? parts[1] : "";
                            var matchedAlias = _gitSettingsManager.CurrentSettings.AllAliases.FirstOrDefault(a => string.Equals(a.Alias, subCmdWord, StringComparison.OrdinalIgnoreCase));
                            if (matchedAlias != null && !string.IsNullOrWhiteSpace(matchedAlias.TargetCommand))
                            {
                                commandToRun = matchedAlias.TargetCommand + (string.IsNullOrEmpty(subCmdArgs) ? "" : " " + subCmdArgs);
                            }
                        }

                        commandToRun = GitCommandText.ReplaceArguments(commandToRun, parsedArgs);

                        var configService = new ConfigService();
                        var appSettings = configService.LoadSettings();
                        var constants = appSettings?.Constants ?? new List<ConstantEntry>();
                        string command = ConstantReplacer.ReplaceConstants(commandToRun.TrimStart(), constants);

                        // 展開後の文字列で {base} プレースホルダーをベースブランチ名に置換
                        if (command.Contains("{base}"))
                        {
                            string baseBranch = string.IsNullOrWhiteSpace(ParentBranch) ? "main" : ParentBranch;
                            command = command.Replace("{base}", baseBranch);
                        }

                        // {current} プレースホルダーを現在のブランチ名に置換
                        if (command.Contains("{current}"))
                        {
                            string currentBranch = GetCurrentBranchName();
                            if (string.IsNullOrEmpty(currentBranch) || currentBranch == "unknown")
                            {
                                currentBranch = CurrentBranch;
                            }
                            command = command.Replace("{current}", currentBranch);
                        }

                        if (string.Equals(command.Trim(), "cancel", StringComparison.OrdinalIgnoreCase))
                        {
                            CancelCurrentExecution();
                            UpdateCurrentCommandStatus("キャンセル");
                            throw new OperationCanceledException();
                        }

                        if (command.StartsWith("base ", StringComparison.OrdinalIgnoreCase))
                        {
                            string targetBase = command.Substring(5).Trim();
                            if (!string.IsNullOrWhiteSpace(targetBase))
                            {
                                ParentBranch = targetBase;
                                
                                var mapping = _gitSettingsManager.CurrentSettings.RepositoryNameMappings.FirstOrDefault(m => string.Equals(m.Path, _repositoryPath, StringComparison.OrdinalIgnoreCase));
                                if (mapping != null)
                                {
                                    mapping.BaseBranch = targetBase;
                                }
                                else
                                {
                                    _gitSettingsManager.CurrentSettings.RepositoryNameMappings.Add(new RepositoryNameMapping
                                    {
                                        Path = _repositoryPath,
                                        BaseBranch = targetBase
                                    });
                                }
                                _gitSettingsManager.SaveSettings();

                                AppendConsoleOutputLine($"[ベースブランチ設定] ベースブランチを '{ParentBranch}' に設定しました。(設定ファイルに保存されました)");
                            }
                            continue; // 外部プロセスの起動はスキップして次のコマンドへ
                        }

                        if (command.StartsWith("cd ", StringComparison.OrdinalIgnoreCase) || 
                            command.StartsWith("repo ", StringComparison.OrdinalIgnoreCase))
                        {
                            string target = command.Substring(command.IndexOf(' ') + 1).Trim();
                            string? newRepoPath = null;

                            if (Path.IsPathRooted(target) && Directory.Exists(target))
                            {
                                newRepoPath = Path.GetFullPath(target);
                            }
                            else
                            {
                                string combined = Path.GetFullPath(Path.Combine(_repositoryPath, target));
                                if (Directory.Exists(combined))
                                {
                                    newRepoPath = combined;
                                }
                            }

                            if (!string.IsNullOrEmpty(newRepoPath))
                            {
                                _repositoryPath = newRepoPath;
                                AppendConsoleOutputLine($"[リポジトリ移動] {GetRepositoryDisplayName(_repositoryPath)}");
                            }
                            else
                            {
                                AppendConsoleOutputLine($"[エラー] 指定された移動先が見つかりません: {target}");
                            }
                            continue; // 外部プロセスの起動はスキップして次のコマンドへ
                        }

                        bool isGitDirect = command.StartsWith("git ", StringComparison.OrdinalIgnoreCase) 
                                           && !command.Contains("|") 
                                           && !command.Contains(">") 
                                           && !command.Contains("<") 
                                           && !command.Contains("&");

                        var psi = new ProcessStartInfo
                        {
                            WorkingDirectory = _repositoryPath,
                            UseShellExecute = false,
                            CreateNoWindow = true,
                            RedirectStandardOutput = true,
                            RedirectStandardError = true,
                            StandardOutputEncoding = ProcessOutputEncoding,
                            StandardErrorEncoding = ProcessOutputEncoding
                        };

                        if (isGitDirect)
                        {
                            psi.FileName = "git";
                            psi.Arguments = command.Substring(4).Trim();
                        }
                        else
                        {
                            psi.FileName = "cmd.exe";
                            psi.Arguments = $"/c {command}";
                        }

                        NativeMethods.AllowSetForegroundWindow(NativeMethods.ASFW_ANY);
                        int exitCode = await _processRunner.RunAsync(
                            psi,
                            _currentExecutionCts?.Token ?? CancellationToken.None,
                            line =>
                            {
                                AppendConsoleOutputLine(line);
                            },
                            line =>
                            {
                                AppendConsoleOutputLine(line);
                            });

                        // 終了コードが0以外の場合は連鎖を中断
                        if (exitCode != 0)
                        {
                            UpdateCurrentCommandStatus("エラー");
                            isSuccess = false;
                            break;
                        }
                    }

                    if (isSuccess)
                    {
                        UpdateCurrentCommandStatus("完了");
                    }
                });

                FlushPendingConsoleLines();

                System.Windows.Application.Current.Dispatcher.Invoke(() =>
                {
                    LoadGitInfo(includeStatus: false);
                });
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                SetConsoleOutput($"関数エラー: {function.Name}", ex.Message, "エラー");
            }
        }

        private string RunGitCommand(string args)
        {
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = "git",
                    Arguments = args,
                    WorkingDirectory = _repositoryPath,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    StandardOutputEncoding = ProcessOutputEncoding,
                    StandardErrorEncoding = ProcessOutputEncoding,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                using var process = Process.Start(psi);
                if (process != null)
                {
                    string output = process.StandardOutput.ReadToEnd();
                    process.WaitForExit();
                    return output.Trim();
                }
            }
            catch { }
            return "unknown";
        }

        private string GetParentBranch(string currentBranch)
        {
            if (string.IsNullOrEmpty(currentBranch)) return string.Empty;
            string output = RunGitCommand("log -n 50 --decorate --simplify-by-decoration --oneline --no-color");
            if (string.IsNullOrWhiteSpace(output)) return string.Empty;

            var lines = output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (var line in lines)
            {
                int start = line.IndexOf('(');
                int end = line.IndexOf(')');
                if (start > 0 && end > start)
                {
                    string decor = line.Substring(start + 1, end - start - 1);
                    var parts = decor.Split(',').Select(p => p.Trim());
                    var branches = new List<string>();
                    foreach (var part in parts)
                    {
                        var branch = part;
                        if (branch.StartsWith("HEAD -> "))
                            branch = branch.Substring(8).Trim();
                        if (branch.StartsWith("origin/"))
                            continue;
                        if (branch == "HEAD" || branch == currentBranch)
                            continue;
                        if (branch.Contains("tag: "))
                            continue;

                        branches.Add(branch);
                    }
                    if (branches.Count > 0)
                    {
                        return branches.First();
                    }
                }
            }
            return string.Empty;
        }

        public void NavigateHistory(bool up)
        {
            if (_commandHistory.Count == 0) return;

            if (up)
            {
                if (_historyIndex > 0)
                {
                    _historyIndex--;
                    InputText = _commandHistory[_historyIndex];
                }
            }
            else
            {
                if (_historyIndex < _commandHistory.Count - 1)
                {
                    _historyIndex++;
                    InputText = _commandHistory[_historyIndex];
                }
                else if (_historyIndex == _commandHistory.Count - 1)
                {
                    _historyIndex = _commandHistory.Count;
                    InputText = string.Empty;
                }
            }
        }

        public event Action? RequestDiffWindow;
        public event Func<CommandEntry, string, string?>? RequestArgumentInput;
        public event Func<CommandEntry, string, List<string>, string?>? RequestSelectInput;

        private static T? InvokeOnUiThread<T>(Func<T?> action)
        {
            T? result = default;
            RunOnUiThread(() => result = action());
            return result;
        }

        private List<SelectTemplate> GetSelectTemplates()
        {
            return _mainVm?.AppSettings?.SelectTemplates ?? new List<SelectTemplate>();
        }

        private bool ResolveSelectPlaceholders(ref string text, string contextName)
        {
            if (string.IsNullOrEmpty(text))
            {
                return false;
            }

            List<SelectTemplate> templates = GetSelectTemplates();
            if (templates.Count == 0)
            {
                return false;
            }

            bool wasReplaced = false;
            var processedPlaceholders = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var matches = Regex.Matches(text, @"\{([^{}]+)\}").Cast<Match>().ToList();
            string displayName = string.IsNullOrWhiteSpace(contextName) ? "Git" : contextName;
            string commandSnapshot = text;

            foreach (var match in matches)
            {
                string templateName = match.Groups[1].Value;
                if (!processedPlaceholders.Add(templateName))
                {
                    continue;
                }

                var template = templates.FirstOrDefault(t => string.Equals(t.Name, templateName, StringComparison.OrdinalIgnoreCase));
                if (template == null || template.Options.Count == 0)
                {
                    continue;
                }

                string? selected = InvokeOnUiThread(() =>
                    RequestSelectInput?.Invoke(
                        new CommandEntry { Name = displayName, Value = commandSnapshot },
                        $"[{displayName}] の選択肢を選んでください:",
                        template.Options));

                if (selected == null)
                {
                    throw new OperationCanceledException("Selection canceled by user.");
                }

                text = text.Replace(match.Value, selected);
                wasReplaced = true;
            }

            return wasReplaced;
        }

        private string GetCurrentBranchName()
        {
            try
            {
                if (LibGit2Sharp.Repository.IsValid(_repositoryPath))
                {
                    using var repo = new LibGit2Sharp.Repository(_repositoryPath);
                    return repo.Head.FriendlyName;
                }
            }
            catch { }
            return "unknown";
        }

        private string GetRepositoryDisplayName(string repositoryPath)
        {
            var mapping = _gitSettingsManager.CurrentSettings.RepositoryNameMappings
                .FirstOrDefault(m => string.Equals(m.Path, repositoryPath, StringComparison.OrdinalIgnoreCase));

            if (mapping != null && !string.IsNullOrWhiteSpace(mapping.OverwrittenName))
            {
                return mapping.OverwrittenName;
            }

            try
            {
                return new DirectoryInfo(repositoryPath).Name;
            }
            catch
            {
                return repositoryPath;
            }
        }

        private string GetGitStatusSummary()
        {
            try
            {
                if (LibGit2Sharp.Repository.IsValid(_repositoryPath))
                {
                    using var repo = new LibGit2Sharp.Repository(_repositoryPath);
                    var status = repo.RetrieveStatus();
                    if (!status.IsDirty) return "変更はありません";
                    
                    var lines = new List<string>();
                    foreach (var item in status)
                    {
                        if (item.State == LibGit2Sharp.FileStatus.Ignored || item.State == LibGit2Sharp.FileStatus.Unaltered) continue;
                        
                        string stateStr = " *";
                        if (item.State.HasFlag(LibGit2Sharp.FileStatus.NewInIndex) || item.State.HasFlag(LibGit2Sharp.FileStatus.NewInWorkdir)) stateStr = "??";
                        else if (item.State.HasFlag(LibGit2Sharp.FileStatus.ModifiedInIndex) || item.State.HasFlag(LibGit2Sharp.FileStatus.ModifiedInWorkdir)) stateStr = " M";
                        else if (item.State.HasFlag(LibGit2Sharp.FileStatus.DeletedFromIndex) || item.State.HasFlag(LibGit2Sharp.FileStatus.DeletedFromWorkdir)) stateStr = " D";
                        
                        lines.Add($"{stateStr} {item.FilePath}");
                    }
                    if (lines.Count == 0) return "変更はありません";
                    return string.Join("\n", lines);
                }
            }
            catch { }
            return "unknown";
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
