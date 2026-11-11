using System.Diagnostics;
using System.IO;
using System.Text.RegularExpressions;

namespace HotKeyCommandApp.Services
{
    public static class VsCodeFileOpener
    {
        public static async Task OpenAsync(string repositoryPath, string relativePath, int? lineNumber)
        {
            string filePath = Path.GetFullPath(Path.Combine(repositoryPath, relativePath));
            if (!File.Exists(filePath)) throw new FileNotFoundException("作業ツリーにファイルがありません。", filePath);
            string executable = FindExecutable();
            if (executable == null) throw new FileNotFoundException("VS Code が見つかりませんでした。");
            using var process = Process.Start(CreateStartInfo(executable, repositoryPath, filePath, lineNumber))
                ?? throw new InvalidOperationException("VS Code を起動できませんでした。");
            Task<string> output = process.StandardOutput.ReadToEndAsync(), error = process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync();
            string details = string.Join("\n", new[] { await error, await output }.Where(text => !string.IsNullOrWhiteSpace(text)));
            if (process.ExitCode != 0)
                throw new InvalidOperationException(string.IsNullOrWhiteSpace(details) ? "VS Code の起動に失敗しました。" : details.Trim());
        }

        private static ProcessStartInfo CreateStartInfo(string executable, string repositoryPath, string filePath, int? lineNumber)
        {
            string directory = Path.GetDirectoryName(executable);
            string cliPath = Path.Combine(directory, "resources", "app", "out", "cli.js");
            string launcher = Path.Combine(directory, "bin", "code.cmd");
            if (File.Exists(launcher))
            {
                var match = Regex.Match(File.ReadAllText(launcher), "\"%~dp0\\.\\.[\\\\/](?<path>[^\"\\r\\n]+cli\\.js)\"", RegexOptions.IgnoreCase);
                if (match.Success) cliPath = Path.GetFullPath(Path.Combine(directory, match.Groups["path"].Value));
            }
            if (!File.Exists(cliPath)) throw new FileNotFoundException("VS Code の起動用 CLI が見つかりませんでした。", cliPath);
            var start = new ProcessStartInfo(executable)
            {
                UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = repositoryPath,
                RedirectStandardOutput = true, RedirectStandardError = true
            };
            // code.cmd と同じ CLI を直接実行し、継承した Electron の起動モードに左右されないようにする。
            start.Environment["ELECTRON_RUN_AS_NODE"] = "1";
            start.Environment.Remove("VSCODE_DEV");
            start.Environment.Remove("VSCODE_IPC_HOOK_CLI");
            start.ArgumentList.Add(cliPath);
            start.ArgumentList.Add("--reuse-window");
            start.ArgumentList.Add(repositoryPath);
            if (lineNumber > 0)
            {
                start.ArgumentList.Add("--goto");
                start.ArgumentList.Add($"{filePath}:{lineNumber}");
            }
            else start.ArgumentList.Add(filePath);
            return start;
        }

        private static string FindExecutable()
        {
            foreach (var process in Process.GetProcessesByName("Code"))
            {
                using (process)
                {
                    try
                    {
                        string path = process.MainModule?.FileName;
                        if (path != null && File.Exists(path)) return path;
                    }
                    catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException) { }
                }
            }
            var paths = new[]
            {
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "Microsoft VS Code", "Code.exe"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Microsoft VS Code", "Code.exe"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Microsoft VS Code", "Code.exe")
            };
            string installed = paths.FirstOrDefault(File.Exists);
            if (installed != null) return installed;
            foreach (string entry in (Environment.GetEnvironmentVariable("PATH") ?? string.Empty).Split(Path.PathSeparator))
            {
                if (string.IsNullOrWhiteSpace(entry)) continue;
                try
                {
                    string directory = entry.Trim().Trim('"');
                    string direct = Path.Combine(directory, "Code.exe");
                    if (File.Exists(direct)) return direct;
                    if (File.Exists(Path.Combine(directory, "code.cmd")))
                    {
                        string parent = Path.GetFullPath(Path.Combine(directory, "..", "Code.exe"));
                        if (File.Exists(parent)) return parent;
                    }
                }
                catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException) { }
            }
            return null;
        }
    }
}
