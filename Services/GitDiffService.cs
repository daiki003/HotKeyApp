using System.IO;
using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;
using HotKeyCommandApp.Models;
using LibGit2Sharp;

namespace HotKeyCommandApp.Services
{
    public sealed class GitDiffService
    {
        private const int MaxDisplayedLines = 20000;
        private static readonly Regex HunkHeader = new(@"^@@ -(\d+)(?:,\d+)? \+(\d+)(?:,\d+)? @@", RegexOptions.Compiled);
        private readonly string _repositoryPath;

        public GitDiffService(string repositoryPath) => _repositoryPath = repositoryPath;

        public GitDiffCommit ReadCommit(string revision)
        {
            using var repository = new Repository(_repositoryPath);
            var commit = repository.Lookup<Commit>(revision?.Trim() ?? string.Empty) ?? throw new InvalidOperationException("コミットが見つかりません。");
            return new GitDiffCommit(commit.Sha, commit.MessageShort, commit.Author.Name, commit.Author.When);
        }

        public IReadOnlyList<GitDiffCommit> ReadRecentCommits(CancellationToken token, int count = 50)
        {
            using var repository = new Repository(_repositoryPath);
            if (repository.Head.Tip == null) return Array.Empty<GitDiffCommit>();
            return repository.Commits.QueryBy(new CommitFilter { IncludeReachableFrom = repository.Head, SortBy = CommitSortStrategies.Topological | CommitSortStrategies.Time })
                .Take(count).Select(commit =>
            {
                token.ThrowIfCancellationRequested();
                return new GitDiffCommit(commit.Sha, commit.MessageShort, commit.Author.Name, commit.Author.When);
            }).ToArray();
        }

        public IReadOnlyList<GitDiffFile> ReadComparisonFiles(GitDiffMode mode, string revision, CancellationToken token)
        {
            if (mode == GitDiffMode.Uncommitted) return ReadFiles(token);
            using var repository = new Repository(_repositoryPath);
            Tree source, target = null;
            string sourceId, targetId = null, label;
            if (mode == GitDiffMode.Commit)
            {
                var commit = repository.Lookup<Commit>(revision) ?? throw new InvalidOperationException("コミットが見つかりません。");
                source = commit.Parents.FirstOrDefault()?.Tree;
                sourceId = commit.Parents.FirstOrDefault()?.Sha ?? string.Empty;
                target = commit.Tree;
                targetId = commit.Sha;
                label = $"{(sourceId.Length == 0 ? "空のツリー" : sourceId[..7])} → {targetId[..7]}（第1親との比較）";
            }
            else
            {
                var branch = ResolveBaseBranch(repository, revision);
                source = branch.Tip.Tree;
                sourceId = branch.Tip.Sha;
                label = $"{branch.FriendlyName} → 作業ツリー（未コミット含む）";
            }
            var options = new CompareOptions { Similarity = SimilarityOptions.Renames };
            using var changes = target == null
                ? repository.Diff.Compare<TreeChanges>(source, DiffTargets.Index | DiffTargets.WorkingDirectory, null, new ExplicitPathsOptions(), options)
                : repository.Diff.Compare<TreeChanges>(source, target, options);
            var files = new List<GitDiffFile>();
            foreach (var change in changes)
            {
                token.ThrowIfCancellationRequested();
                if (change.Status == ChangeKind.Unmodified) continue;
                files.Add(new GitDiffFile(change.Path, change.OldPath, false, change.Status switch
                {
                    ChangeKind.Added or ChangeKind.Untracked => "追加", ChangeKind.Deleted => "削除",
                    ChangeKind.Renamed => "名前変更", ChangeKind.Conflicted => "競合", _ => "変更"
                }, change.Status == ChangeKind.Conflicted, SourceRevision: sourceId, TargetRevision: targetId, ComparisonLabel: label));
            }
            if (target == null)
            {
                foreach (var file in ReadFiles(token).Where(file => !file.IsStaged && file.Change == "追加"))
                {
                    if (source[file.Path]?.Target is Blob original)
                    {
                        // インデックスから消えたパスも、作業ツリーに再作成されていればベースの内容と比較する。
                        var content = ReadRecreatedFileDiff(original, file.Path, token, false);
                        files.RemoveAll(existing => existing.Path == file.Path);
                        if (content.RawPatch.Length > 0)
                            files.Add(file with { Change = "変更", SourceRevision = sourceId, ComparisonLabel = label });
                        continue;
                    }
                    if (!files.Any(existing => existing.Path == file.Path))
                        files.Add(file with { SourceRevision = sourceId, ComparisonLabel = label });
                }
            }
            return files.OrderBy(file => file.Path, StringComparer.OrdinalIgnoreCase).ToArray();
        }

        internal static Branch ResolveBaseBranch(Repository repository, string name)
        {
            if (string.IsNullOrWhiteSpace(name)) throw new InvalidOperationException("Git 設定のリポジトリ名でベースブランチを設定してください。");
            var branch = repository.Branches[name];
            if (branch?.Tip == null) throw new InvalidOperationException($"ベースブランチ「{name}」が見つかりません。");
            return branch;
        }

        public string ReadCurrentBranch(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            using var repository = new Repository(_repositoryPath);
            return repository.Info.IsHeadDetached
                ? $"detached HEAD ({repository.Head.Tip.Id.Sha[..7]})"
                : repository.Head.FriendlyName;
        }

        public GitDiffBranchInfo ReadBranchInfo(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            using var repository = new Repository(_repositoryPath);
            var branch = repository.Head;
            string name = repository.Info.IsHeadDetached
                ? $"detached HEAD ({branch.Tip.Id.Sha[..7]})" : branch.FriendlyName;
            string branchName = repository.Info.IsHeadDetached ? string.Empty : branch.FriendlyName;
            if (repository.Info.IsHeadDetached || branch.Tip == null)
                return new GitDiffBranchInfo(name, branchName, string.Empty, null, null);

            var trackedBranch = branch.TrackedBranch;
            if (trackedBranch?.Tip == null) return new GitDiffBranchInfo(name, branchName, string.Empty, null, null);
            try
            {
                var tracking = branch.TrackingDetails;
                int? ahead = tracking.AheadBy;
                int? behind = tracking.BehindBy;
                cancellationToken.ThrowIfCancellationRequested();
                return new GitDiffBranchInfo(name, branchName, trackedBranch.FriendlyName, ahead, behind);
            }
            catch (LibGit2SharpException)
            {
                // コミット履歴を集計できなくても、ファイル差分は表示できるようにする。
                return new GitDiffBranchInfo(name, branchName, trackedBranch.FriendlyName, null, null);
            }
        }

        public IReadOnlyList<GitDiffFile> ReadFiles(CancellationToken cancellationToken)
        {
            using var repository = new Repository(_repositoryPath);
            var status = repository.RetrieveStatus(new StatusOptions
            {
                IncludeUntracked = true, RecurseUntrackedDirs = true,
                DetectRenamesInIndex = true, DetectRenamesInWorkDir = true
            });
            var files = new List<GitDiffFile>();
            const FileStatus staged = FileStatus.NewInIndex | FileStatus.ModifiedInIndex | FileStatus.DeletedFromIndex |
                FileStatus.RenamedInIndex | FileStatus.TypeChangeInIndex;
            const FileStatus unstaged = FileStatus.NewInWorkdir | FileStatus.ModifiedInWorkdir | FileStatus.DeletedFromWorkdir |
                FileStatus.RenamedInWorkdir | FileStatus.TypeChangeInWorkdir | FileStatus.Unreadable | FileStatus.Conflicted;
            foreach (var entry in status)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if ((entry.State & staged) != 0)
                {
                    string path = entry.HeadToIndexRenameDetails?.NewFilePath ?? entry.IndexToWorkDirRenameDetails?.OldFilePath ?? entry.FilePath;
                    files.Add(new GitDiffFile(path, entry.HeadToIndexRenameDetails?.OldFilePath, true, GetChange(entry.State, true)));
                }
                if ((entry.State & unstaged) != 0)
                    files.Add(new GitDiffFile(entry.IndexToWorkDirRenameDetails?.NewFilePath ?? entry.FilePath,
                        entry.IndexToWorkDirRenameDetails?.OldFilePath, false, GetChange(entry.State, false), entry.State.HasFlag(FileStatus.Conflicted)));
            }
            return files.OrderBy(file => file.IsStaged).ThenBy(file => file.Path, StringComparer.OrdinalIgnoreCase).ToArray();
        }

        private static string GetChange(FileStatus state, bool staged)
        {
            if (state.HasFlag(FileStatus.Conflicted)) return "競合";
            if ((state & (staged ? FileStatus.RenamedInIndex : FileStatus.RenamedInWorkdir)) != 0) return "名前変更";
            if ((state & (staged ? FileStatus.NewInIndex : FileStatus.NewInWorkdir)) != 0) return "追加";
            if ((state & (staged ? FileStatus.DeletedFromIndex : FileStatus.DeletedFromWorkdir)) != 0) return "削除";
            if (state.HasFlag(FileStatus.Unreadable)) return "読取不可";
            return "変更";
        }

        public GitDiffContent ReadDiff(GitDiffFile file, CancellationToken cancellationToken, GitDiffContent previous = null, bool fullContent = false)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (file.IsConflicted)
                return new GitDiffContent(Array.Empty<GitDiffLine>(), 0, 0, "競合中のファイルです。エディターで競合マーカーを確認してください。");

            using var repository = new Repository(_repositoryPath);
            if (file.IsReadOnly && file.TargetRevision == null && repository.Index[file.Path] == null &&
                File.Exists(System.IO.Path.Combine(_repositoryPath, file.Path)) &&
                repository.Lookup<Commit>(file.SourceRevision).Tree[file.Path]?.Target is Blob original)
                return ReadRecreatedFileDiff(original, file.Path, cancellationToken, fullContent);
            string[] paths = new[] { file.Path, file.OldPath }.Where(path => !string.IsNullOrEmpty(path)).Distinct(StringComparer.Ordinal).ToArray();
            var explicitPaths = new ExplicitPathsOptions { ShouldFailOnUnmatchedPath = false };
            var options = new CompareOptions { ContextLines = 3, Similarity = SimilarityOptions.Renames };
            using var patch = file.IsReadOnly
                ? file.TargetRevision != null
                    ? repository.Diff.Compare<Patch>(string.IsNullOrEmpty(file.SourceRevision) ? null : repository.Lookup<Commit>(file.SourceRevision).Tree,
                        repository.Lookup<Commit>(file.TargetRevision).Tree, paths, explicitPaths, options)
                    : repository.Diff.Compare<Patch>(repository.Lookup<Commit>(file.SourceRevision).Tree,
                        DiffTargets.Index | DiffTargets.WorkingDirectory, paths, explicitPaths, options)
                : file.IsStaged
                ? repository.Diff.Compare<Patch>(repository.Head.Tip?.Tree, DiffTargets.Index, paths, explicitPaths, options)
                : repository.Diff.Compare<Patch>(paths, true, explicitPaths, options);
            // ツリー比較に含まれない未追跡ファイルも、通常の新規ファイル差分で表示する。
            if (!patch.Any() && file.IsReadOnly && file.TargetRevision == null && file.Change == "追加")
                return ReadDiff(file with { SourceRevision = null, ComparisonLabel = null }, cancellationToken, previous, fullContent);
            cancellationToken.ThrowIfCancellationRequested();
            if (patch.Any(entry => entry.IsBinaryComparison))
                return new GitDiffContent(Array.Empty<GitDiffLine>(), 0, 0, "バイナリファイルの内容が変更されています。テキスト差分は表示できません。");
            if (!patch.Any())
                return new GitDiffContent(Array.Empty<GitDiffLine>(), 0, 0, "現在、このファイルに表示できる差分はありません。F5 で一覧を更新できます。");

            string content = patch.Content;
            if (previous != null && previous.RawPatch == content && previous.Added == patch.LinesAdded && previous.Deleted == patch.LinesDeleted)
                return previous;
            var lines = ParsePatch(content, cancellationToken, out bool truncated, fullContent ? int.MaxValue : MaxDisplayedLines);
            string message = truncated ? $"差分が大きいため、先頭 {MaxDisplayedLines:N0} 行まで表示しています。" :
                lines.Count == 0 ? "ファイル名またはファイル属性のみが変更されています。" : string.Empty;
            return new GitDiffContent(lines, patch.LinesAdded, patch.LinesDeleted, message, content);
        }

        private GitDiffContent ReadRecreatedFileDiff(Blob original, string path, CancellationToken token, bool fullContent)
        {
            string temporaryPath = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "HotKeyBaseDiff-" + Guid.NewGuid().ToString("N"));
            try
            {
                using (var input = original.GetContentStream())
                using (var output = File.Create(temporaryPath)) input.CopyTo(output);
                token.ThrowIfCancellationRequested();
                var psi = new ProcessStartInfo("git")
                {
                    WorkingDirectory = _repositoryPath, UseShellExecute = false, CreateNoWindow = true,
                    RedirectStandardOutput = true, RedirectStandardError = true,
                    StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8
                };
                foreach (string argument in new[] { "diff", "--no-index", "--no-ext-diff", "--no-textconv", "--no-color", "--unified=3", "--",
                    temporaryPath, System.IO.Path.Combine(_repositoryPath, path) }) psi.ArgumentList.Add(argument);
                using var process = Process.Start(psi) ?? throw new InvalidOperationException("Git を起動できませんでした。");
                using var registration = token.Register(() =>
                {
                    try { process.Kill(entireProcessTree: true); }
                    catch (InvalidOperationException) { }
                });
                var stdout = process.StandardOutput.ReadToEndAsync();
                var stderr = process.StandardError.ReadToEndAsync();
                process.WaitForExit();
                string content = stdout.GetAwaiter().GetResult(), error = stderr.GetAwaiter().GetResult();
                token.ThrowIfCancellationRequested();
                if (process.ExitCode > 1) throw new InvalidOperationException(error.Trim());
                if (content.Contains("\nBinary files ", StringComparison.Ordinal))
                    return new GitDiffContent(Array.Empty<GitDiffLine>(), 0, 0, "バイナリファイルの内容が変更されています。テキスト差分は表示できません。", content);
                var lines = ParsePatch(content, token, out _, int.MaxValue);
                bool truncated = !fullContent && lines.Count > MaxDisplayedLines;
                return new GitDiffContent(truncated ? lines.Take(MaxDisplayedLines).ToArray() : lines,
                    lines.Count(line => line.Kind == GitDiffLineKind.Added), lines.Count(line => line.Kind == GitDiffLineKind.Deleted),
                    truncated ? $"差分が大きいため、先頭 {MaxDisplayedLines:N0} 行まで表示しています。" : string.Empty, content);
            }
            finally { if (File.Exists(temporaryPath)) File.Delete(temporaryPath); }
        }

        private static IReadOnlyList<GitDiffLine> ParsePatch(string patch, CancellationToken cancellationToken, out bool truncated, int maxLines)
        {
            var result = new List<GitDiffLine>();
            using var reader = new StringReader(patch);
            int oldLine = 0, newLine = 0;
            bool inHunk = false;
            truncated = false;
            string line;
            while ((line = reader.ReadLine()) != null)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (result.Count >= maxLines) { truncated = true; break; }
                var match = HunkHeader.Match(line);
                if (match.Success)
                {
                    oldLine = int.Parse(match.Groups[1].Value);
                    newLine = int.Parse(match.Groups[2].Value);
                    inHunk = true;
                    result.Add(new GitDiffLine("", "", "", line, GitDiffLineKind.Hunk));
                }
                else if (line.StartsWith("diff --git ", StringComparison.Ordinal)) inHunk = false;
                else if (inHunk && line.Length > 0)
                {
                    switch (line[0])
                    {
                        case '+': result.Add(new GitDiffLine("", (newLine++).ToString(), "+", line[1..], GitDiffLineKind.Added)); break;
                        case '-': result.Add(new GitDiffLine((oldLine++).ToString(), "", "−", line[1..], GitDiffLineKind.Deleted)); break;
                        case ' ': result.Add(new GitDiffLine((oldLine++).ToString(), (newLine++).ToString(), "", line[1..], GitDiffLineKind.Context)); break;
                        default: result.Add(new GitDiffLine("", "", "", line, GitDiffLineKind.Notice)); break;
                    }
                }
            }
            return result;
        }
    }
}
