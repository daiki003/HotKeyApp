using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using HotKeyCommandApp.Models;
using LibGit2Sharp;

namespace HotKeyCommandApp.Services
{
    public sealed class GitStageService
    {
        private static readonly Regex HunkHeader = new(@"^@@ -(\d+)(?:,\d+)? \+(\d+)(?:,\d+)? @@", RegexOptions.Compiled);
        private readonly string _repositoryPath;
        private readonly GitDiffService _diffService;

        public GitStageService(string repositoryPath)
        {
            _repositoryPath = repositoryPath;
            _diffService = new GitDiffService(repositoryPath);
        }

        public async Task StageFileAsync(GitDiffFile file)
        {
            var args = new List<string> { "--literal-pathspecs", "add", "--all", "--" };
            args.AddRange(Paths(file));
            await RunRequiredAsync(args);
        }

        public Task StageAllAsync() => RunRequiredAsync(new[] { "add", "--all", "--", ":/" });

        public async Task DiscardFileAsync(GitDiffFile file, GitDiffContent displayed)
        {
            await ValidateDiscardAsync(file, displayed);
            var tracked = await RunGitAsync(new[] { "--literal-pathspecs", "ls-files", "--error-unmatch", "--", file.Path });
            if (tracked.ExitCode == 0)
                await RunRequiredAsync(new[] { "--literal-pathspecs", "restore", "--worktree", "--", file.Path });
            else if (tracked.ExitCode == 1 && file.Change == "追加")
                await RunRequiredAsync(new[] { "--literal-pathspecs", "clean", "-f", "--", file.Path });
            else
                throw new InvalidOperationException("ファイルの状態を確認できませんでした。F5 で更新してください。");
        }

        public async Task DiscardLinesAsync(GitDiffFile file, GitDiffContent displayed, IReadOnlyCollection<int> selectedLines)
        {
            await ValidateDiscardAsync(file, displayed);
            var selected = selectedLines.Where(i => i >= 0 && i < displayed.Lines.Count &&
                displayed.Lines[i].Kind is GitDiffLineKind.Added or GitDiffLineKind.Deleted).ToHashSet();
            if (selected.Count == 0) throw new InvalidOperationException("破棄する追加行または削除行を選択してください。");
            string patch = BuildDiscardPatch(displayed.RawPatch, selected);
            await RunRequiredAsync(new[] { "apply", "--recount", "--unidiff-zero", "--whitespace=nowarn", "-" }, patch);
        }

        private async Task ValidateDiscardAsync(GitDiffFile file, GitDiffContent displayed)
        {
            if (file == null || file.IsStaged || file.IsConflicted || (!string.IsNullOrEmpty(file.OldPath) && file.OldPath != file.Path))
                throw new InvalidOperationException("この変更は破棄できません。競合や名前変更を確認してください。");
            if (displayed == null || string.IsNullOrEmpty(displayed.RawPatch))
                throw new InvalidOperationException("このファイルは差分を確認できないため破棄できません。");
            var current = await Task.Run(() => _diffService.ReadDiff(file, CancellationToken.None));
            if (!string.Equals(current.RawPatch, displayed.RawPatch, StringComparison.Ordinal) ||
                current.Message != displayed.Message || current.Added != displayed.Added || current.Deleted != displayed.Deleted)
                throw new InvalidOperationException("差分が変更されました。F5 で更新してから再選択してください。");
        }

        private static string BuildDiscardPatch(string rawPatch, HashSet<int> selected)
        {
            if (string.IsNullOrEmpty(rawPatch)) throw new InvalidOperationException("この差分は行単位で破棄できません。");
            string[] input = rawPatch.Split('\n');
            int firstHunk = Array.FindIndex(input, line => HunkHeader.IsMatch(line));
            if (firstHunk < 0 || input.Skip(1).Any(line => line.StartsWith("diff --git ", StringComparison.Ordinal)))
                throw new InvalidOperationException("この差分は行単位で破棄できません。");
            var header = input.Take(firstHunk).ToArray();
            if (header.Any(line => line.StartsWith("old mode ", StringComparison.Ordinal) || line.StartsWith("new mode ", StringComparison.Ordinal) ||
                line.StartsWith("rename ", StringComparison.Ordinal) || line.StartsWith("copy ", StringComparison.Ordinal)))
                throw new InvalidOperationException("名前または属性の変更を含むため、行単位で破棄できません。");
            string oldPath = header.First(line => line.StartsWith("--- ", StringComparison.Ordinal))[4..];
            string newPath = header.First(line => line.StartsWith("+++ ", StringComparison.Ordinal))[4..];
            if (oldPath == "/dev/null") oldPath = newPath.StartsWith("\"b/", StringComparison.Ordinal) ? "\"a/" + newPath[3..] : "a/" + newPath[2..];
            if (newPath == "/dev/null") newPath = oldPath.StartsWith("\"a/", StringComparison.Ordinal) ? "\"b/" + oldPath[3..] : "b/" + oldPath[2..];
            var result = new StringBuilder();
            result.Append("--- ").Append(header.Any(line => line.StartsWith("deleted file mode ", StringComparison.Ordinal)) ? "/dev/null" : oldPath).Append('\n');
            result.Append("+++ ").Append(newPath).Append('\n');
            int position = firstHunk, displayIndex = 0, cumulativeDelta = 0, included = 0;
            while (position < input.Length)
            {
                var match = HunkHeader.Match(input[position++]);
                if (!match.Success) throw new InvalidOperationException("差分の構造を解析できませんでした。");
                int start = int.Parse(match.Groups[2].Value, CultureInfo.InvariantCulture);
                displayIndex++;
                var body = new List<string>();
                int oldCount = 0, newCount = 0, chosen = 0;
                while (position < input.Length && !HunkHeader.IsMatch(input[position]))
                {
                    string line = input[position++];
                    if (line.Length == 0) continue;
                    bool take = selected.Contains(displayIndex++);
                    string marker = null;
                    if (position < input.Length && input[position].StartsWith("\\ No newline at end of file", StringComparison.Ordinal))
                    { marker = input[position++]; displayIndex++; }
                    char kind = line[0];
                    if (kind == ' ' || (kind == '+' && !take))
                    { body.Add(" " + line[1..]); oldCount++; newCount++; if (marker != null) body.Add(marker); }
                    else if (kind == '+' && take)
                    { body.Add("-" + line[1..]); oldCount++; chosen++; if (marker != null) body.Add(marker); }
                    else if (kind == '-' && take)
                    { body.Add("+" + line[1..]); newCount++; chosen++; if (marker != null) body.Add(marker); }
                    else if (kind != '-') throw new InvalidOperationException("この差分は行単位で破棄できません。");
                }
                if (chosen == 0) continue;
                int target = start + cumulativeDelta;
                if (oldCount == 0 && newCount > 0) target++;
                else if (oldCount > 0 && newCount == 0) target--;
                result.Append(CultureInfo.InvariantCulture, $"@@ -{start},{oldCount} +{target},{newCount} @@\n");
                foreach (string line in body) result.Append(line).Append('\n');
                cumulativeDelta += newCount - oldCount;
                included += chosen;
            }
            if (included != selected.Count) throw new InvalidOperationException("選択した行が差分と一致しません。F5 で更新してください。");
            return result.ToString();
        }

        public async Task UnstageFileAsync(GitDiffFile file)
        {
            bool hasHead;
            using (var repository = new Repository(_repositoryPath)) hasHead = repository.Head.Tip != null;
            var args = hasHead
                ? new List<string> { "--literal-pathspecs", "reset", "-q", "HEAD", "--" }
                : new List<string> { "--literal-pathspecs", "rm", "--cached", "-r", "--" };
            args.AddRange(Paths(file));
            await RunRequiredAsync(args);
        }

        public async Task StageLinesAsync(GitDiffFile file, GitDiffContent displayed, IReadOnlyCollection<int> selectedLines)
        {
            if (file.IsStaged || file.IsConflicted || (!string.IsNullOrEmpty(file.OldPath) && file.OldPath != file.Path))
                throw new InvalidOperationException("この変更は行単位でステージできません。ファイル単位で操作してください。");
            if (displayed == null || string.IsNullOrEmpty(displayed.RawPatch))
                throw new InvalidOperationException("差分の読み込みが完了していません。");

            var selected = selectedLines.Where(i => i >= 0 && i < displayed.Lines.Count &&
                displayed.Lines[i].Kind is GitDiffLineKind.Added or GitDiffLineKind.Deleted).ToHashSet();
            if (selected.Count == 0) throw new InvalidOperationException("ステージする追加行または削除行を選択してください。");

            var current = await Task.Run(() => _diffService.ReadDiff(file, CancellationToken.None));
            if (!string.Equals(current.RawPatch, displayed.RawPatch, StringComparison.Ordinal))
                throw new InvalidOperationException("差分が変更されました。F5 で更新してから再選択してください。");
            bool allChangesSelected = string.IsNullOrEmpty(displayed.Message) &&
                selected.Count == displayed.Lines.Count(line => line.Kind is GitDiffLineKind.Added or GitDiffLineKind.Deleted);
            if (file.Change == "削除" && allChangesSelected)
            {
                await StageFileAsync(file);
                return;
            }

            string patch = BuildPartialPatch(displayed.RawPatch, selected);
            await RunRequiredAsync(new[] { "apply", "--cached", "--recount", "--unidiff-zero", "--whitespace=nowarn", "-" }, patch);
        }

        public async Task CommitStagedAsync(string message)
        {
            if (string.IsNullOrWhiteSpace(message)) throw new InvalidOperationException("コミットメッセージを入力してください。");
            var check = await RunGitAsync(new[] { "diff", "--cached", "--quiet", "--exit-code" });
            if (check.ExitCode == 0) throw new InvalidOperationException("ステージ済みの変更はありません。");
            if (check.ExitCode != 1) throw new InvalidOperationException(check.Output);
            await RunRequiredAsync(new[] { "commit", "-m", message });
        }

        private static IEnumerable<string> Paths(GitDiffFile file) =>
            new[] { file.Path, file.OldPath }.Where(path => !string.IsNullOrEmpty(path)).Distinct(StringComparer.Ordinal);

        private static string BuildPartialPatch(string rawPatch, HashSet<int> selected)
        {
            string[] input = rawPatch.Split('\n');
            int firstHunk = Array.FindIndex(input, line => HunkHeader.IsMatch(line));
            if (firstHunk < 0 || input.Skip(1).Any(line => line.StartsWith("diff --git ", StringComparison.Ordinal)))
                throw new InvalidOperationException("この差分は行単位でステージできません。ファイル単位で操作してください。");
            var header = input.Take(firstHunk).ToList();
            if (header.Any(line => line.StartsWith("old mode ", StringComparison.Ordinal) || line.StartsWith("new mode ", StringComparison.Ordinal) ||
                line.StartsWith("rename ", StringComparison.Ordinal) || line.StartsWith("copy ", StringComparison.Ordinal)))
                throw new InvalidOperationException("名前または属性の変更を含むため、ファイル単位でステージしてください。");

            if (header.Any(line => line.StartsWith("deleted file mode ", StringComparison.Ordinal)))
            {
                string oldPath = header.First(line => line.StartsWith("--- ", StringComparison.Ordinal))[4..];
                string newPath = oldPath.StartsWith("\"a/", StringComparison.Ordinal) ? "\"b/" + oldPath[3..]
                    : oldPath.StartsWith("a/", StringComparison.Ordinal) ? "b/" + oldPath[2..] : null;
                if (newPath == null) throw new InvalidOperationException("削除ファイルのパスを解析できませんでした。");
                header.RemoveAll(line => line.StartsWith("deleted file mode ", StringComparison.Ordinal) || line.StartsWith("index ", StringComparison.Ordinal));
                int pathIndex = header.FindIndex(line => line.StartsWith("+++ ", StringComparison.Ordinal));
                if (pathIndex < 0) throw new InvalidOperationException("差分のパスを解析できませんでした。");
                header[pathIndex] = "+++ " + newPath;
            }

            var result = new StringBuilder();
            foreach (string line in header) result.Append(line).Append('\n');
            int displayIndex = 0, cumulativeDelta = 0, position = firstHunk, includedChanges = 0;
            while (position < input.Length)
            {
                var match = HunkHeader.Match(input[position]);
                if (!match.Success) throw new InvalidOperationException("差分の構造を解析できませんでした。");
                int oldStart = int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture);
                displayIndex++;
                position++;
                var body = new List<string>();
                int oldCount = 0, newCount = 0, chosen = 0;
                while (position < input.Length && !HunkHeader.IsMatch(input[position]))
                {
                    string line = input[position++];
                    if (line.Length == 0) continue;
                    bool take = selected.Contains(displayIndex++);
                    bool noNewline = position < input.Length && input[position].StartsWith("\\ No newline at end of file", StringComparison.Ordinal);
                    string noNewlineMarker = noNewline ? input[position++] : null;
                    if (noNewline) displayIndex++;
                    switch (line[0])
                    {
                        case ' ':
                            body.Add(line); oldCount++; newCount++;
                            if (noNewline) body.Add(noNewlineMarker);
                            break;
                        case '-':
                            oldCount++;
                            if (take)
                            {
                                body.Add(line); chosen++;
                                if (noNewline) body.Add(noNewlineMarker);
                            }
                            else if (noNewline && HasSelectedAddition(input, position, displayIndex, selected))
                            {
                                // 末尾改行のない旧行の後へ追加する場合、区切りの改行を補う。
                                body.Add(line); body.Add(noNewlineMarker); body.Add("+" + line[1..]); newCount++;
                            }
                            else
                            {
                                body.Add(" " + line[1..]); newCount++;
                                if (noNewline) body.Add(noNewlineMarker);
                            }
                            break;
                        case '+':
                            if (take) { body.Add(line); newCount++; chosen++; if (noNewline) body.Add(noNewlineMarker); }
                            break;
                        default: throw new InvalidOperationException("この差分は行単位でステージできません。ファイル単位で操作してください。");
                    }
                }
                if (chosen == 0) continue;
                int newStart = oldStart + cumulativeDelta;
                if (oldCount == 0 && newCount > 0) newStart++;
                else if (oldCount > 0 && newCount == 0) newStart--;
                result.Append(CultureInfo.InvariantCulture, $"@@ -{oldStart},{oldCount} +{newStart},{newCount} @@\n");
                foreach (string line in body) result.Append(line).Append('\n');
                cumulativeDelta += newCount - oldCount;
                includedChanges += chosen;
            }
            if (includedChanges != selected.Count) throw new InvalidOperationException("選択した行が差分と一致しません。F5 で更新してください。");
            return result.ToString();
        }

        private static bool HasSelectedAddition(string[] input, int position, int displayIndex, HashSet<int> selected)
        {
            while (position < input.Length && !HunkHeader.IsMatch(input[position]))
            {
                string line = input[position++];
                if (line.Length == 0) continue;
                if (line[0] == '+' && selected.Contains(displayIndex)) return true;
                displayIndex++;
            }
            return false;
        }

        private async Task RunRequiredAsync(IEnumerable<string> args, string input = null)
        {
            var result = await RunGitAsync(args, input);
            if (result.ExitCode != 0) throw new InvalidOperationException(string.IsNullOrWhiteSpace(result.Output) ? "Git 操作に失敗しました。" : result.Output.Trim());
        }

        private async Task<(int ExitCode, string Output)> RunGitAsync(IEnumerable<string> args, string input = null)
        {
            var psi = new ProcessStartInfo("git")
            {
                WorkingDirectory = _repositoryPath, UseShellExecute = false, CreateNoWindow = true,
                RedirectStandardOutput = true, RedirectStandardError = true, RedirectStandardInput = input != null,
                StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8
            };
            if (input != null) psi.StandardInputEncoding = new UTF8Encoding(false);
            psi.Environment["GIT_TERMINAL_PROMPT"] = "0";
            foreach (string arg in args) psi.ArgumentList.Add(arg);
            using var process = Process.Start(psi) ?? throw new InvalidOperationException("Git を起動できませんでした。");
            Task<string> stdout = process.StandardOutput.ReadToEndAsync(), stderr = process.StandardError.ReadToEndAsync();
            if (input != null) { await process.StandardInput.WriteAsync(input); process.StandardInput.Close(); }
            await process.WaitForExitAsync();
            return (process.ExitCode, string.Join("\n", new[] { await stderr, await stdout }.Where(s => !string.IsNullOrWhiteSpace(s))));
        }
    }
}
