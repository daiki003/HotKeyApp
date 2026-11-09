using System.Diagnostics;
using System.Text;
using LibGit2Sharp;

namespace HotKeyCommandApp.Services
{
    public sealed class GitBranchService
    {
        private readonly string _repositoryPath;

        public GitBranchService(string repositoryPath) => _repositoryPath = repositoryPath;

        public async Task PushCurrentBranchAsync()
        {
            string remoteName, source, destination;
            bool setUpstream;
            using (var repository = new Repository(_repositoryPath))
            {
                var head = RequireCurrentBranch(repository);
                if (head.Tip == null) throw new InvalidOperationException("プッシュするコミットがありません。");
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
                throw new InvalidOperationException("プッシュ先を特定できません。リモートまたはブランチの追跡先を設定してください。");
            var arguments = new List<string> { "push" };
            if (setUpstream) arguments.Add("--set-upstream");
            arguments.AddRange(new[] { "--", remoteName, $"{source}:{destination}" });
            await RunRequiredAsync(arguments);
        }

        public async Task PullCurrentBranchAsync()
        {
            using (var repository = new Repository(_repositoryPath)) RequireCurrentBranch(repository);
            await RunRequiredAsync(new[] { "pull" });
        }

        public async Task RenameCurrentBranchAsync(string expectedName, string newName)
        {
            if (string.IsNullOrWhiteSpace(newName)) throw new InvalidOperationException("ブランチ名を入力してください。");
            using (var repository = new Repository(_repositoryPath))
            {
                if (RequireCurrentBranch(repository).FriendlyName != expectedName)
                    throw new InvalidOperationException("現在のブランチが変更されました。表示を確認して再実行してください。");
            }
            await RunRequiredAsync(new[] { "branch", "-m", "--", expectedName, newName.Trim() });
        }

        private static Branch RequireCurrentBranch(Repository repository)
        {
            if (repository.Info.IsHeadDetached) throw new InvalidOperationException("ブランチを選択してから操作してください。");
            return repository.Head;
        }

        private async Task RunRequiredAsync(IEnumerable<string> arguments)
        {
            var startInfo = new ProcessStartInfo("git")
            {
                WorkingDirectory = _repositoryPath, UseShellExecute = false, CreateNoWindow = true,
                RedirectStandardOutput = true, RedirectStandardError = true,
                StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8
            };
            startInfo.Environment["GIT_TERMINAL_PROMPT"] = "0";
            foreach (string argument in arguments) startInfo.ArgumentList.Add(argument);
            using var process = Process.Start(startInfo) ?? throw new InvalidOperationException("Git を起動できませんでした。");
            Task<string> stdout = process.StandardOutput.ReadToEndAsync(), stderr = process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync();
            string output = string.Join("\n", new[] { await stderr, await stdout }.Where(text => !string.IsNullOrWhiteSpace(text)));
            if (process.ExitCode != 0)
                throw new InvalidOperationException(string.IsNullOrWhiteSpace(output) ? "Git 操作に失敗しました。" : output.Trim());
        }
    }
}
