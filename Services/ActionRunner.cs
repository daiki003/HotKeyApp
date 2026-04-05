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
                    int count = _windowService.ActivateWindowByTitle(value);
                    // 検索に引っかかるウィンドウが複数ある場合、ウィンドウを閉じずにそのままにしたい
                    return count <= 1;
                }

                // 「ファイルを開く」の場合、既存の開いているウィンドウがあればそれを最前面に出す
                if (command.Type == CommandType.File && _windowService.ActivateVscodeWindow(value))
                {
                    return true;
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
