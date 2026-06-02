using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Input;
using LibGit2Sharp;
using HotKeyCommandApp.Models;
using HotKeyCommandApp.Services;

namespace HotKeyCommandApp.ViewModels
{
    public class GitCommandViewModel : INotifyPropertyChanged
    {
        private string _repositoryPath;
        private readonly MainViewModel? _mainVm;
        private readonly GitSettingsManager _gitSettingsManager;
        private readonly List<string> _commandHistory = new();
        private int _historyIndex = 0;
        private CancellationTokenSource? _currentExecutionCts;
        private Process? _currentProcess;

        public GitCommandViewModel(string repositoryPath, MainViewModel? mainVm = null)
        {
            _repositoryPath = repositoryPath;
            _mainVm = mainVm;
            _gitSettingsManager = new GitSettingsManager();
            LoadGitInfo();
        }

        public void ReloadSettings()
        {
            _gitSettingsManager.LoadSettings();
        }

        public double MovementSpeed => _mainVm?.MovementSpeed ?? 1200.0;

        private readonly List<string> _consoleHistoryList = new();
        private string _latestConsoleOutput = string.Empty;
        private int _activeConsoleHistoryIndex = -1;

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

        private void UpdateConsoleOutputDisplay()
        {
            if (!IsHistoryMode)
            {
                ConsoleOutput = _latestConsoleOutput;
                return;
            }

            ConsoleOutput = "[コンソール履歴 (Console History)]\n=================================\n" + string.Join("\n\n---------------------------------\n", _consoleHistoryList);
        }

        public void SetConsoleOutput(string header, string content)
        {
            RunOnUiThread(() =>
            {
                string entry = string.IsNullOrEmpty(header) ? content.TrimEnd() : $"[{header}]\n{content.TrimEnd()}";
                _latestConsoleOutput = entry;
                _consoleHistoryList.Add(entry);
                _activeConsoleHistoryIndex = _consoleHistoryList.Count - 1;
                UpdateConsoleOutputDisplay();
            });
        }

        private void ReplaceConsoleOutput(string header, string content)
        {
            RunOnUiThread(() =>
            {
                string entry = string.IsNullOrEmpty(header) ? content.TrimEnd() : $"[{header}]\n{content.TrimEnd()}";

                if (_activeConsoleHistoryIndex < 0 || _activeConsoleHistoryIndex >= _consoleHistoryList.Count)
                {
                    _consoleHistoryList.Add(entry);
                    _activeConsoleHistoryIndex = _consoleHistoryList.Count - 1;
                }
                else
                {
                    _consoleHistoryList[_activeConsoleHistoryIndex] = entry;
                }

                _latestConsoleOutput = entry;
                UpdateConsoleOutputDisplay();
            });
        }

        private void AppendConsoleOutputLine(string line)
        {
            RunOnUiThread(() =>
            {
                string current = _latestConsoleOutput;
                string updated = string.IsNullOrEmpty(current)
                    ? line
                    : current + "\n" + line;

                if (_activeConsoleHistoryIndex < 0 || _activeConsoleHistoryIndex >= _consoleHistoryList.Count)
                {
                    _consoleHistoryList.Add(updated);
                    _activeConsoleHistoryIndex = _consoleHistoryList.Count - 1;
                }
                else
                {
                    _consoleHistoryList[_activeConsoleHistoryIndex] = updated;
                }

                _latestConsoleOutput = updated;
                UpdateConsoleOutputDisplay();
            });
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
                    ConsoleOutput = _latestConsoleOutput;
                }
            }
        }

        private bool _isMoveMode = false;
        public bool IsMoveMode
        {
            get => _isMoveMode;
            set { _isMoveMode = value; OnPropertyChanged(); }
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
            set { _parentBranch = value; OnPropertyChanged(); }
        }

        private string _currentBranch = string.Empty;
        public string CurrentBranch
        {
            get => _currentBranch;
            set { _currentBranch = value; OnPropertyChanged(); }
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

        public void CancelCurrentExecution()
        {
            _currentExecutionCts?.Cancel();

            try
            {
                if (_currentProcess != null && !_currentProcess.HasExited)
                {
                    _currentProcess.Kill(entireProcessTree: true);
                }
            }
            catch
            {
                // プロセス終了直後の競合は無視する
            }
        }

        private async Task<int> RunProcessAsync(ProcessStartInfo psi, CancellationToken cancellationToken, Action<string>? onStdOutLine = null, Action<string>? onStdErrLine = null)
        {
            using var process = Process.Start(psi);
            if (process == null)
            {
                return -1;
            }

            _currentProcess = process;

            try
            {
                var stdoutTask = ReadProcessStreamAsync(process.StandardOutput, onStdOutLine, cancellationToken);
                var stderrTask = ReadProcessStreamAsync(process.StandardError, onStdErrLine, cancellationToken);
                var exitTask = process.WaitForExitAsync();
                var cancelTask = Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);

                var completedTask = await Task.WhenAny(exitTask, cancelTask);
                if (completedTask == cancelTask)
                {
                    CancelCurrentExecution();

                    try
                    {
                        await exitTask;
                    }
                    catch
                    {
                        // キャンセルに伴う終了待ちの失敗は無視する
                    }

                    cancellationToken.ThrowIfCancellationRequested();
                }

                await Task.WhenAll(stdoutTask, stderrTask);
                await exitTask;
                return process.ExitCode;
            }
            finally
            {
                if (ReferenceEquals(_currentProcess, process))
                {
                    _currentProcess = null;
                }
            }
        }

        private static async Task ReadProcessStreamAsync(System.IO.StreamReader reader, Action<string>? onLine, CancellationToken cancellationToken)
        {
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();

                string? line = await reader.ReadLineAsync();
                if (line == null)
                {
                    break;
                }

                onLine?.Invoke(line);
            }
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

        private async void ExecuteGitCommand()
        {
            if (string.IsNullOrWhiteSpace(InputText)) return;

            string rawInput = InputText.Trim();
            InputText = string.Empty;

            if (string.Equals(rawInput, "cancel", StringComparison.OrdinalIgnoreCase))
            {
                if (_currentProcess == null && _currentExecutionCts == null)
                {
                    SetConsoleOutput("キャンセル", "実行中のGit操作はありません");
                }
                else
                {
                    CancelCurrentExecution();
                    SetConsoleOutput("キャンセル", "実行中のGit操作を中断しています");
                }

                return;
            }

            if (_commandHistory.Count == 0 || _commandHistory.Last() != rawInput)
            {
                _commandHistory.Add(rawInput);
            }
            _historyIndex = _commandHistory.Count;

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
                        SetConsoleOutput($"関数: {matchedFunction.Name}", "キャンセルしました");
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
                    bool hasPlaceholder = false;
                    int maxPlaceholderIndex = -1;
                    for (int i = 0; i < 10; i++)
                    {
                        if (targetCmd.Contains($"{{{i}}}"))
                        {
                            hasPlaceholder = true;
                            if (i > maxPlaceholderIndex)
                            {
                                maxPlaceholderIndex = i;
                            }
                        }
                    }

                    List<string> parsedArgs = string.IsNullOrWhiteSpace(cmdArgs) ? new List<string>() : ParseArguments(cmdArgs);

                    if (hasPlaceholder)
                    {
                        if (parsedArgs.Count < maxPlaceholderIndex + 1)
                        {
                            var tempCommand = new CommandEntry
                            {
                                Name = matchedAlias.Alias,
                                Value = targetCmd
                            };

                            string? argString = RequestArgumentInput?.Invoke(tempCommand, $"[{matchedAlias.Alias}] の引数を入力してください:");
                            if (argString == null) return; // ユーザーがキャンセルした場合は実行中断

                            parsedArgs = ParseArguments(argString);
                        }

                        for (int i = 0; i < 10; i++)
                        {
                            string placeholder = $"{{{i}}}";
                            if (targetCmd.Contains(placeholder))
                            {
                                string replacement = i < parsedArgs.Count ? parsedArgs[i] : "";
                                targetCmd = targetCmd.Replace(placeholder, replacement);
                            }
                        }
                        input = targetCmd;
                    }
                    else
                    {
                        // エイリアスの置換結果で input を上書きする
                        input = targetCmd + (string.IsNullOrEmpty(cmdArgs) ? "" : " " + cmdArgs);
                    }
                }
            }

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

                    SetConsoleOutput("ベースブランチ設定", $"ベースブランチを '{ParentBranch}' に設定しました。(設定ファイルに保存されました)");
                }
                return;
            }



            if (string.Equals(input, "front", StringComparison.OrdinalIgnoreCase))
            {
                IsFrontMode = true;
                SetConsoleOutput("最前面固定モード", "Gitウィンドウを常に最前面に表示します。\n・Esc: 解除");
                return;
            }

            if (string.Equals(input, "history", StringComparison.OrdinalIgnoreCase))
            {
                IsHistoryMode = true;
                ConsoleOutput = "[コンソール履歴 (Console History)]\n=================================\n" + string.Join("\n\n---------------------------------\n", _consoleHistoryList);
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
                    SetConsoleOutput("リポジトリ移動", GetRepositoryDisplayName(_repositoryPath));
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
            SetConsoleOutput(command, "実行中... (Running...)");

            using var commandExecutionCts = new CancellationTokenSource();
            _currentExecutionCts = commandExecutionCts;

            try
            {
                bool sawOutput = false;
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
                        StandardOutputEncoding = Encoding.UTF8,
                        StandardErrorEncoding = Encoding.UTF8
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

                    return await RunProcessAsync(
                        psi,
                        _currentExecutionCts?.Token ?? CancellationToken.None,
                        line =>
                        {
                            sawOutput = true;
                            AppendConsoleOutputLine(line);
                        });
                });

                System.Windows.Application.Current.Dispatcher.Invoke(() =>
                {
                    try
                    {
                        CurrentBranch = GetCurrentBranchName();
                    }
                    catch { }

                    if (!sawOutput)
                    {
                        if (exitCode == 0)
                        {
                            ReplaceConsoleOutput(command, GetGitStatusSummary());
                        }
                        else
                        {
                            ReplaceConsoleOutput(command, $"エラーで終了しました。 (ExitCode: {exitCode})");
                        }
                    }
                    else
                    {
                        AppendConsoleOutputLine(exitCode == 0
                            ? "[成功] 正常に完了しました。"
                            : $"[失敗] エラーで終了しました。 (ExitCode: {exitCode})");
                    }
                });
            }
            catch (OperationCanceledException)
            {
                SetConsoleOutput("キャンセル", "実行を中断しました");
            }
            catch (Exception ex)
            {
                SetConsoleOutput("実行エラー", ex.Message);
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
            bool hasPlaceholder = false;
            int maxPlaceholderIndex = -1;
            foreach (var cmdTemplate in function.Commands)
            {
                for (int i = 0; i < 10; i++)
                {
                    if (cmdTemplate.Contains($"{{{i}}}"))
                    {
                        hasPlaceholder = true;
                        if (i > maxPlaceholderIndex)
                        {
                            maxPlaceholderIndex = i;
                        }
                    }
                }
            }

            List<string> parsedArgs = string.IsNullOrWhiteSpace(cmdArgs) ? new List<string>() : ParseArguments(cmdArgs);

            if (hasPlaceholder && parsedArgs.Count < maxPlaceholderIndex + 1)
            {
                var tempCommand = new CommandEntry
                {
                    Name = function.Name,
                    Value = string.Join("\n", function.Commands)
                };

                string? argString = RequestArgumentInput?.Invoke(tempCommand, $"[{function.Name}] の引数を入力してください:");
                if (argString == null) return; // キャンセルされた場合は実行中断

                parsedArgs = ParseArguments(argString);
            }

            SetConsoleOutput($"関数: {function.Name}", "実行中... (Running...)");

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

                        // 置換 {0}, {1}... 
                        for (int i = 0; i < 10; i++)
                        {
                            string replacement = i < parsedArgs.Count ? parsedArgs[i] : "";
                            commandToRun = commandToRun.Replace($"{{{i}}}", replacement);
                        }

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
                            StandardOutputEncoding = Encoding.UTF8,
                            StandardErrorEncoding = Encoding.UTF8
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

                        int exitCode = await RunProcessAsync(
                            psi,
                            _currentExecutionCts?.Token ?? CancellationToken.None,
                            line =>
                            {
                                AppendConsoleOutputLine(line);
                            });

                        // 終了コードが0以外の場合は連鎖を中断
                        if (exitCode != 0)
                        {
                            AppendConsoleOutputLine("[エラー終了] コマンドが失敗したため中断しました。");
                            isSuccess = false;
                            break;
                        }
                    }

                    if (isSuccess)
                    {
                        AppendConsoleOutputLine("[成功] すべての処理が正常に完了しました。");
                    }
                });

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
                SetConsoleOutput($"関数エラー: {function.Name}", ex.Message);
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
                    StandardOutputEncoding = Encoding.UTF8,
                    StandardErrorEncoding = Encoding.UTF8,
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

        public event Func<CommandEntry, string, string?>? RequestArgumentInput;

        private List<string> ParseArguments(string commandLine)
        {
            var args = new List<string>();
            bool inQuotes = false;
            string currentArg = "";
            for (int i = 0; i < commandLine.Length; i++)
            {
                char c = commandLine[i];
                if (c == '"')
                {
                    inQuotes = !inQuotes;
                }
                else if (c == ' ' && !inQuotes)
                {
                    if (!string.IsNullOrEmpty(currentArg))
                    {
                        args.Add(currentArg);
                        currentArg = "";
                    }
                }
                else
                {
                    currentArg += c;
                }
            }
            if (!string.IsNullOrEmpty(currentArg))
            {
                args.Add(currentArg);
            }
            return args;
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
