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

                // {0}などのプレースホルダーが含まれる場合は置換を行う（モードに関わらず）
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
                    // アプリそのものを起動しようとしているか、または特定のファイル/フォルダを開こうとしているかを判定
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
                            string processName = System.IO.Path.GetFileNameWithoutExtension(value);
                            if (_windowService.ActivateWindowByProcessName(processName))
                            {
                                return true;
                            }
                        }

                        string titleToMatch = !string.IsNullOrEmpty(appPath) 
                            ? System.IO.Path.GetFileNameWithoutExtension(appPath) 
                            : System.IO.Path.GetFileNameWithoutExtension(value);

                        if (!string.IsNullOrEmpty(titleToMatch) && _windowService.ActivateWindowByTitle(titleToMatch) > 0)
                        {
                            return true;
                        }
                    }
                    else
                    {
                        if (_windowService.ActivateVscodeWindow(value))
                        {
                            return true;
                        }
                    }
                }

                var psi = CreateProcessStartInfo(command, value, appPath, argument, wasReplaced);

                if (psi != null && !string.IsNullOrEmpty(psi.FileName))
                {
                    Process.Start(psi);
                }
                return true;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error running command: {ex.Message}");
                return true;
            }
        }

        private ProcessStartInfo? CreateProcessStartInfo(CommandEntry command, string value, string? appPath, string? argument, bool wasReplaced)
        {
            if (command.Category == CommandCategory.Hierarchy) return null;

            var psi = new ProcessStartInfo { UseShellExecute = true };

            // 起動先の設定
            if (!string.IsNullOrEmpty(appPath))
            {
                psi.FileName = appPath;
                // ファイル/フォルダ系かつAppPathがある場合は -n を付与（とりあえずUseWindowFocusLogicがONなら付与とする）
                string args = (command.Behavior.UseWindowFocusLogic ? "-n " : "") + $"\"{value}\"";
                
                // Batchタイプかつ引数があり、まだ置換されていない場合は追加
                if (command.Behavior.IsBatchMode && !string.IsNullOrEmpty(argument) && !wasReplaced)
                {
                    args += " " + argument;
                }
                psi.Arguments = args;
            }
            else
            {
                psi.FileName = value;
                // Batchタイプかつ引数があり、まだ置換されていない場合は直接引数として渡す
                if (command.Behavior.IsBatchMode && !string.IsNullOrEmpty(argument) && !wasReplaced)
                {
                    psi.Arguments = argument;
                }
            }

            // 作業ディレクトリの設定
            psi.WorkingDirectory = GetWorkingDirectory(command, value);

            return psi;
        }

        private string GetWorkingDirectory(CommandEntry command, string value)
        {
            if (command.Behavior.IsBatchMode)
            {
                return Path.GetDirectoryName(value) ?? AppDomain.CurrentDomain.BaseDirectory;
            }
            return string.Empty;
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
