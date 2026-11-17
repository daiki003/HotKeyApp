using System.Diagnostics;
using System.IO;
using System.Text;
using HotKeyCommandApp.Models;
using LibGit2Sharp;

namespace HotKeyCommandApp.Services
{
    public sealed class GitStashService
    {
        private readonly string _repositoryPath;

        public GitStashService(string repositoryPath) => _repositoryPath = repositoryPath;

        public async Task StashFilesAsync(IReadOnlyCollection<GitDiffFile> files)
        {
            if (files == null || files.Count == 0)
                throw new InvalidOperationException("スタッシュ準備群にファイルを追加してください。");
            await Task.Run(() =>
            {
                using var repository = new Repository(_repositoryPath);
                if (repository.Head.Tip == null)
                    throw new InvalidOperationException("初回コミット前はスタッシュできません。");
                if (repository.Index.Conflicts.Any())
                    throw new InvalidOperationException("競合を解消してからスタッシュしてください。");
                if (files.Any(file => file.IsConflicted || repository.Submodules.Any(module =>
                    module.Path == file.Path || module.Path == file.OldPath)))
                    throw new InvalidOperationException("競合中のファイルやサブモジュールはスタッシュできません。");
            });

            var paths = files.SelectMany(file => new[] { file.Path, file.OldPath })
                .Where(path => !string.IsNullOrEmpty(path)).ToHashSet(StringComparer.Ordinal);
            string pathInput = string.Join('\0', paths) + '\0';
            var currentFiles = await Task.Run(() => new GitDiffService(_repositoryPath).ReadFiles(CancellationToken.None));
            if (files.Any(file => !currentFiles.Any(current => current.Path == file.Path)))
                throw new InvalidOperationException("ファイル一覧が変更されました。F5 で更新してから準備し直してください。");
            // ステージ済み削除のパスは Git が直接指定を受け付けないため、対象外の変更を除外して指定する。
            string stashPathInput = ":/\0" + string.Concat(currentFiles.SelectMany(file => new[] { file.Path, file.OldPath })
                .Where(path => !string.IsNullOrEmpty(path) && !paths.Contains(path)).Distinct(StringComparer.Ordinal)
                .Select(path => ":(top,exclude,literal)" + path + '\0'));
            string temporaryIndex = Path.Combine(Path.GetTempPath(), "HotKeyStashIndex-" + Guid.NewGuid().ToString("N"));
            try
            {
                // 通常のパス指定スタッシュにも対象外のステージ内容が混入するため、対象だけの一時インデックスを使う。
                string indexEntries = await RunRequiredAsync(new[] { "ls-files", "--stage", "-z" });
                string selectedEntries = string.Concat(indexEntries.Split('\0', StringSplitOptions.RemoveEmptyEntries)
                    .Where(entry => paths.Contains(entry[(entry.IndexOf('\t') + 1)..])).Select(entry => entry + '\0'));
                await RunRequiredAsync(new[] { "read-tree", "HEAD" }, indexFile: temporaryIndex);
                await RunRequiredAsync(new[] { "update-index", "--force-remove", "-z", "--stdin" }, pathInput, temporaryIndex);
                if (selectedEntries.Length > 0)
                    await RunRequiredAsync(new[] { "update-index", "-z", "--index-info" }, selectedEntries, temporaryIndex);

                var previous = await RunGitAsync(new[] { "rev-parse", "--quiet", "--verify", "refs/stash" });
                await RunRequiredAsync(new[] { "stash", "push", "--include-untracked",
                    "--pathspec-from-file=-", "--pathspec-file-nul" }, stashPathInput, temporaryIndex);
                string current = await RunRequiredAsync(new[] { "rev-parse", "--quiet", "--verify", "refs/stash" });
                if (current == previous.Output)
                    throw new InvalidOperationException("保存できる変更がありません。F5 で一覧を更新してください。");

                try
                {
                    await RunRequiredAsync(new[] { "--literal-pathspecs", "reset", "--quiet", "HEAD",
                        "--pathspec-from-file=-", "--pathspec-file-nul" }, pathInput);
                }
                catch (Exception ex)
                {
                    throw new InvalidOperationException($"スタッシュは作成されましたが、対象ファイルのステージ解除に失敗しました。\n{ex.Message}", ex);
                }
            }
            finally
            {
                if (File.Exists(temporaryIndex)) File.Delete(temporaryIndex);
                if (File.Exists(temporaryIndex + ".lock")) File.Delete(temporaryIndex + ".lock");
            }
        }

        private async Task<string> RunRequiredAsync(IEnumerable<string> args, string input = null, string indexFile = null)
        {
            var result = await RunGitAsync(args, input, indexFile);
            if (result.ExitCode != 0)
                throw new InvalidOperationException(string.IsNullOrWhiteSpace(result.Error) ? "Git 操作に失敗しました。" : result.Error.Trim());
            return result.Output;
        }

        private async Task<(int ExitCode, string Output, string Error)> RunGitAsync(IEnumerable<string> args, string input = null, string indexFile = null)
        {
            var psi = new ProcessStartInfo("git")
            {
                WorkingDirectory = _repositoryPath, UseShellExecute = false, CreateNoWindow = true,
                RedirectStandardOutput = true, RedirectStandardError = true, RedirectStandardInput = input != null,
                StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8
            };
            if (input != null) psi.StandardInputEncoding = new UTF8Encoding(false);
            if (indexFile != null) psi.Environment["GIT_INDEX_FILE"] = indexFile;
            psi.Environment["GIT_TERMINAL_PROMPT"] = "0";
            foreach (string arg in args) psi.ArgumentList.Add(arg);
            using var process = Process.Start(psi) ?? throw new InvalidOperationException("Git を起動できませんでした。");
            Task<string> stdout = process.StandardOutput.ReadToEndAsync(), stderr = process.StandardError.ReadToEndAsync();
            if (input != null) { await process.StandardInput.WriteAsync(input); process.StandardInput.Close(); }
            await process.WaitForExitAsync();
            return (process.ExitCode, await stdout, await stderr);
        }
    }
}
