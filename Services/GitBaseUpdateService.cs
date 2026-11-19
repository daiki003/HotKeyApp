using System.Diagnostics;
using System.Text;
using LibGit2Sharp;

namespace HotKeyCommandApp.Services
{
    public sealed class GitBaseUpdateService
    {
        private readonly string _repositoryPath;
        public GitBaseUpdateService(string repositoryPath) => _repositoryPath = repositoryPath;

        public async Task UpdateAsync(string baseBranch)
        {
            string headName, headSha, mergeRef, remote;
            using (var repository = new Repository(_repositoryPath))
            {
                if (repository.Info.IsHeadDetached || repository.Head.Tip == null)
                    throw new InvalidOperationException("コミットのあるブランチを選択してから実行してください。");
                if (repository.Info.CurrentOperation != CurrentOperation.None || repository.Index.Conflicts.Any())
                    throw new InvalidOperationException("進行中のマージなどの操作・競合を解消してから実行してください。");
                var branch = GitDiffService.ResolveBaseBranch(repository, baseBranch);
                if (branch.CanonicalName == repository.Head.CanonicalName)
                    throw new InvalidOperationException("現在のブランチとは別のベースブランチを設定してください。");
                var tracked = branch.IsRemote ? branch : branch.TrackedBranch;
                mergeRef = (tracked ?? branch).CanonicalName;
                remote = tracked?.RemoteName;
                headName = repository.Head.CanonicalName;
                headSha = repository.Head.Tip.Sha;
                var changes = new GitDiffService(_repositoryPath).ReadFiles(CancellationToken.None);
                if (changes.Any(file => repository.Submodules.Any(module => module.Path == file.Path)))
                    throw new InvalidOperationException("サブモジュールの変更を整理してから実行してください。");
            }

            string marker = "HotKeyApp-base-update-" + Guid.NewGuid().ToString("N");
            string stashSha = null;
            bool restored = false;
            try
            {
                var stash = await RunAsync("stash", "push", "--include-untracked", "-m", marker);
                // 他の操作でスタッシュの番号が変わっても、自分が作成したエントリーだけを扱う。
                var created = (await RequiredAsync("stash", "list", "--format=%H%x09%gd%x09%gs"))
                    .Split('\n').FirstOrDefault(line => line.Contains(marker, StringComparison.Ordinal));
                stashSha = created?.Split('\t')[0];
                RequireSuccess(stash);
                if (!string.IsNullOrEmpty(remote)) await RequiredAsync("fetch", "--", remote);
                using (var repository = new Repository(_repositoryPath))
                {
                    if (repository.Head.CanonicalName != headName || repository.Head.Tip?.Sha != headSha)
                        throw new InvalidOperationException("実行中に現在のブランチが変更されました。取り込みを中止しました。");
                    if (repository.RetrieveStatus().IsDirty)
                        throw new InvalidOperationException("退避後に新しい変更が見つかりました。取り込みを中止しました。");
                }
                await RequiredAsync("merge", "--no-edit", "--", mergeRef);
                if (stashSha == null) return;
                await RequiredAsync("stash", "apply", "--index", stashSha);
                restored = true;
                var entry = (await RequiredAsync("stash", "list", "--format=%H%x09%gd"))
                    .Split('\n').Select(line => line.Trim().Split('\t'))
                    .FirstOrDefault(parts => parts.Length == 2 && parts[0] == stashSha);
                if (entry != null) await RequiredAsync("stash", "drop", entry[1]);
            }
            catch (Exception ex)
            {
                string recovery = stashSha == null ? string.Empty : restored
                    ? $"\n変更は復元済みです。スタッシュ {stashSha} の削除を確認してください。"
                    : $"\n退避した変更はスタッシュ {stashSha} に残しています。競合や進行中の操作を確認・解消してから復元してください。\n復元コマンド: git stash apply --index {stashSha}";
                throw new InvalidOperationException(ex.Message + recovery, ex);
            }
        }

        private async Task<string> RequiredAsync(params string[] arguments)
        {
            var result = await RunAsync(arguments);
            RequireSuccess(result);
            return result.Output;
        }

        private static void RequireSuccess((int Code, string Output, string Error) result)
        {
            if (result.Code != 0) throw new InvalidOperationException(string.Join("\n", new[] { result.Error, result.Output }
                .Where(text => !string.IsNullOrWhiteSpace(text))).Trim());
        }

        private async Task<(int Code, string Output, string Error)> RunAsync(params string[] arguments)
        {
            var psi = new ProcessStartInfo("git")
            {
                WorkingDirectory = _repositoryPath, UseShellExecute = false, CreateNoWindow = true,
                RedirectStandardOutput = true, RedirectStandardError = true,
                StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8
            };
            psi.Environment["GIT_TERMINAL_PROMPT"] = "0";
            psi.Environment["GIT_MERGE_AUTOEDIT"] = "no";
            foreach (string argument in arguments) psi.ArgumentList.Add(argument);
            using var process = Process.Start(psi) ?? throw new InvalidOperationException("Git を起動できませんでした。");
            var output = process.StandardOutput.ReadToEndAsync();
            var error = process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync();
            return (process.ExitCode, await output, await error);
        }
    }
}
