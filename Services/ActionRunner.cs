using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using HotKeyCommandApp.Models;

namespace HotKeyCommandApp.Services
{
    public class ActionRunner
    {
        private readonly WindowService _windowService = new WindowService();

        public bool Run(CommandEntry command, string? argument = null)
        {
            try
            {
                var configService = new ConfigService();
                var settings = configService.LoadSettings();
                var constants = settings?.Constants ?? new List<ConstantEntry>();

                string value = command.Value ?? string.Empty;
                value = ConstantReplacer.ReplaceConstants(value, constants);

                string? appPath = command.AppPath;
                if (!string.IsNullOrEmpty(appPath))
                {
                    appPath = ConstantReplacer.ReplaceConstants(appPath, constants);
                }

                bool wasReplaced = false;

                // {0}などのプレースホルダーが含まれる場合のみ、引数を後ろのモードに関わらず置換する
                bool hasPlaceholder = false;
                for (int i = 0; i < 10; i++)
                {
                    if (value.Contains($"{{{i}}}"))
                    {
                        hasPlaceholder = true;
                        break;
                    }
                }

                if (argument != null && hasPlaceholder)
                {
                    var parsedArgs = ParseArguments(argument);
                    for (int i = 0; i < 10; i++)
                    {
                        string placeholder = $"{{{i}}}";
                        if (value.Contains(placeholder))
                        {
                            string replacement = i < parsedArgs.Count ? parsedArgs[i] : "";
                            value = value.Replace(placeholder, replacement);
                            wasReplaced = true;
                        }
                    }
                }

                // ウィンドウハンドルが直接指定されている場合は、タイプに関わらず最優先でフォーカスする
                if (command.WindowHandle != IntPtr.Zero)
                {
                    _windowService.FocusWindow(command.WindowHandle);
                    return true;
                }

                // ウィンドウフォーカスロジックが有効な場合
                if (command.Behavior.UseWindowFocusLogic)
                {
                    // アプリそのものを起動しようとしているか、それとも特定のファイル/フォルダを開こうとしているかを判定
                    bool isOpeningAppItself = string.IsNullOrEmpty(value) ||
                                              (value.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) &&
                                               (string.IsNullOrEmpty(appPath) || string.Equals(value, appPath, StringComparison.OrdinalIgnoreCase)));

                    if (isOpeningAppItself)
                    {
                        if (!string.IsNullOrEmpty(appPath) && _windowService.ActivateWindowByProcessPath(appPath))
                        {
                            return true;
                        }
                        else if (value.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) && _windowService.ActivateWindowByProcessPath(value))
                        {
                            return true;
                        }
                        else if (value.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase))
                        {
                            string processName = Path.GetFileNameWithoutExtension(value);
                            if (_windowService.ActivateWindowByProcessName(processName))
                            {
                                return true;
                            }
                        }

                        string titleToMatch = !string.IsNullOrEmpty(appPath)
                            ? Path.GetFileNameWithoutExtension(appPath)
                            : Path.GetFileNameWithoutExtension(value);

                        if (!string.IsNullOrEmpty(titleToMatch) && _windowService.ActivateWindowByTitle(titleToMatch) > 0)
                        {
                            return true;
                        }
                    }
                    else
                    {
                        Logger.Log($"[VSCode] 既存ウィンドウ確認を開始: command='{command.Name}', target='{value}', appPath='{appPath}'");
                        if (_windowService.ActivateVscodeWindow(value))
                        {
                            return true;
                        }

                        Logger.Log($"[VSCode] 既存ウィンドウ再利用に失敗したため新規起動へフォールバック: command='{command.Name}', target='{value}', appPath='{appPath}'");
                    }
                }

                var psi = CreateProcessStartInfo(command, value, appPath, argument, wasReplaced);

                if (psi != null && !string.IsNullOrEmpty(psi.FileName))
                {
                    Logger.Log($"[ActionRunner] Process.Start 実行: command='{command.Name}', file='{psi.FileName}', args='{psi.Arguments}', workDir='{psi.WorkingDirectory}'");
                    var processCountsBefore = GetCodeProcessCounts();
                    var startedProcess = Process.Start(psi);
                    string startedProcessId = startedProcess?.Id.ToString() ?? "null";
                    var processCountsAfter = GetCodeProcessCounts();

                    Logger.Log($"[ActionRunner] Process.Start 結果: command='{command.Name}', startedPid='{startedProcessId}', codeProcessCountBefore='{processCountsBefore.Code}', codeProcessCountAfter='{processCountsAfter.Code}', codeProcessDelta='{processCountsAfter.Code - processCountsBefore.Code}', codeInsidersProcessCountBefore='{processCountsBefore.CodeInsiders}', codeInsidersProcessCountAfter='{processCountsAfter.CodeInsiders}', codeInsidersProcessDelta='{processCountsAfter.CodeInsiders - processCountsBefore.CodeInsiders}'");
                }
                return true;
            }
            catch (Exception ex)
            {
                Logger.Log($"[ActionRunner] 実行失敗: command='{command.Name}', value='{command.Value}', appPath='{command.AppPath}', argument='{argument}', error='{ex}'");
                return true;
            }
        }

        private ProcessStartInfo? CreateProcessStartInfo(CommandEntry command, string value, string? appPath, string? argument, bool wasReplaced)
        {
            if (command.Category == CommandCategory.Hierarchy) return null;

            string? resolvedAppPath = ResolveExecutablePath(appPath);
            string args = BuildLaunchArguments(command, value, argument, wasReplaced);
            string workingDirectory = GetWorkingDirectory(command, value);

            if (!string.IsNullOrEmpty(resolvedAppPath))
            {
                if (IsBatchFile(resolvedAppPath))
                {
                    return CreateHiddenBatchStartInfo(resolvedAppPath, args, workingDirectory);
                }

                return new ProcessStartInfo
                {
                    FileName = resolvedAppPath,
                    Arguments = args,
                    UseShellExecute = true,
                    WorkingDirectory = workingDirectory
                };
            }

            return new ProcessStartInfo
            {
                FileName = value,
                Arguments = command.Behavior.IsBatchMode && !string.IsNullOrEmpty(argument) && !wasReplaced
                    ? argument
                    : string.Empty,
                UseShellExecute = true,
                WorkingDirectory = workingDirectory
            };
        }

        private string BuildLaunchArguments(CommandEntry command, string value, string? argument, bool wasReplaced)
        {
            string args = (command.Behavior.UseWindowFocusLogic ? "-n " : "") + $"\"{value}\"";

            if (command.Behavior.IsBatchMode && !string.IsNullOrEmpty(argument) && !wasReplaced)
            {
                args += " " + argument;
            }

            return args;
        }

        private ProcessStartInfo CreateHiddenBatchStartInfo(string batchPath, string args, string workingDirectory)
        {
            string quotedBatchPath = QuoteForCommand(batchPath);
            string commandLine = string.IsNullOrWhiteSpace(args)
                ? quotedBatchPath
                : $"{quotedBatchPath} {args}";

            Logger.Log($"[VSCode] code.cmd を非表示で起動: batch='{batchPath}', args='{args}'");

            return new ProcessStartInfo
            {
                FileName = Environment.GetEnvironmentVariable("ComSpec") ?? "cmd.exe",
                Arguments = $"/d /s /c \"{commandLine}\"",
                UseShellExecute = false,
                CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden,
                WorkingDirectory = workingDirectory
            };
        }

        private bool IsBatchFile(string? path)
        {
            return !string.IsNullOrWhiteSpace(path) &&
                   string.Equals(Path.GetExtension(path), ".cmd", StringComparison.OrdinalIgnoreCase);
        }

        private string QuoteForCommand(string value)
        {
            return $"\"{value}\"";
        }

        private string GetWorkingDirectory(CommandEntry command, string value)
        {
            if (command.Behavior.IsBatchMode)
            {
                return Path.GetDirectoryName(value) ?? AppDomain.CurrentDomain.BaseDirectory;
            }
            return string.Empty;
        }

        private string? ResolveExecutablePath(string? appPath)
        {
            if (string.IsNullOrWhiteSpace(appPath))
            {
                return appPath;
            }

            if (!appPath.EndsWith("Code.exe", StringComparison.OrdinalIgnoreCase))
            {
                return appPath;
            }

            try
            {
                string? codeDirectory = Path.GetDirectoryName(appPath);
                if (string.IsNullOrEmpty(codeDirectory))
                {
                    return appPath;
                }

                string codeCmdPath = Path.Combine(codeDirectory, "bin", "code.cmd");
                if (File.Exists(codeCmdPath))
                {
                    Logger.Log($"[VSCode] 起動実体を code.cmd に切り替え: original='{appPath}', resolved='{codeCmdPath}'");
                    return codeCmdPath;
                }
            }
            catch (Exception ex)
            {
                Logger.Log($"[VSCode] 起動実体の解決失敗: appPath='{appPath}', error='{ex.Message}'");
            }

            return appPath;
        }

        private (int Code, int CodeInsiders) GetCodeProcessCounts()
        {
            try
            {
                return (
                    Process.GetProcessesByName("Code").Length,
                    Process.GetProcessesByName("Code - Insiders").Length
                );
            }
            catch (Exception processCountEx)
            {
                Logger.Log($"[ActionRunner] プロセス数取得失敗: error='{processCountEx.Message}'");
                return (0, 0);
            }
        }

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
    }
}
