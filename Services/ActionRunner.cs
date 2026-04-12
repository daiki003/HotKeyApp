using System;
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
                string value = command.Value ?? string.Empty;
                // Batch以外の場合は従来通り{0}を置換する
                if (argument != null && command.Type != CommandType.Batch)
                {
                    value = value.Replace("{0}", argument);
                }

                if (command.Type == CommandType.Window)
                {
                    // ハンドルが直接指定されている場合は、それを使用する（より正確で高速）
                    if (command.WindowHandle != IntPtr.Zero)
                    {
                        _windowService.FocusWindow(command.WindowHandle);
                        return true;
                    }

                    int count = _windowService.ActivateWindowByTitle(value);
                    // 検索に引っかかるウィンドウが複数ある場合、ウィンドウを閉じずにそのままにしたい
                    return count <= 1;
                }

                // 「ファイルを開く」の場合、既存の開いているウィンドウがあればそれを最前面に出す
                if (command.Type == CommandType.File)
                {
                    // アプリそのものを起動しようとしているか、または特定のファイル/フォルダを開こうとしているかを判定
                    bool isOpeningAppItself = string.IsNullOrEmpty(value) || 
                                              (value.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) && 
                                               (string.IsNullOrEmpty(command.AppPath) || string.Equals(value, command.AppPath, StringComparison.OrdinalIgnoreCase)));

                    if (isOpeningAppItself)
                    {
                        // アプリパスが指定されている場合、そのアプリがすでに起動しているか確認
                        if (!string.IsNullOrEmpty(command.AppPath))
                        {
                            if (_windowService.ActivateWindowByProcessPath(command.AppPath))
                            {
                                return true;
                            }
                        }
                        // アプリパスがなく、Value自体が実行ファイルの場合
                        else if (value.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                        {
                            if (_windowService.ActivateWindowByProcessPath(value))
                            {
                                return true;
                            }
                        }
                        // ショートカット（.lnk）の場合、実行ファイル名と一致するか確認
                        else if (value.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase))
                        {
                            string processName = System.IO.Path.GetFileNameWithoutExtension(value);
                            if (_windowService.ActivateWindowByProcessName(processName))
                            {
                                return true;
                            }
                        }

                        // アプリ本体のタイトルマッチング
                        string titleToMatch = !string.IsNullOrEmpty(command.AppPath) 
                            ? System.IO.Path.GetFileNameWithoutExtension(command.AppPath) 
                            : System.IO.Path.GetFileNameWithoutExtension(value);

                        if (!string.IsNullOrEmpty(titleToMatch) && _windowService.ActivateWindowByTitle(titleToMatch) > 0)
                        {
                            return true;
                        }
                    }
                    else
                    {
                        // ファイル/フォルダを開く場合
                        // VS Codeなどでその特定のファイル/フォルダがすでに開いているかを確認
                        if (_windowService.ActivateVscodeWindow(value))
                        {
                            return true;
                        }
                        
                        // 開いていない場合は、後の Process.Start へ処理を移す
                    }
                }

                var psi = CreateProcessStartInfo(command, value, argument);

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

        private ProcessStartInfo? CreateProcessStartInfo(CommandEntry command, string value, string? argument = null)
        {
            if (command.Type == CommandType.Menu) return null;

            var psi = new ProcessStartInfo { UseShellExecute = true };

            // 起動先の設定
            if (!string.IsNullOrEmpty(command.AppPath))
            {
                psi.FileName = command.AppPath;
                psi.Arguments = (command.Type == CommandType.File ? "-n " : "") + $"\"{value}\"";
            }
            else
            {
                psi.FileName = value;
                // Batchタイプかつ引数がある場合、直接引数として渡す
                if (command.Type == CommandType.Batch && !string.IsNullOrEmpty(argument))
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
            return command.Type switch
            {
                CommandType.Batch => Path.GetDirectoryName(value) ?? AppDomain.CurrentDomain.BaseDirectory,
                CommandType.Command => AppDomain.CurrentDomain.BaseDirectory,
                _ => string.Empty
            };
        }
    }
}
