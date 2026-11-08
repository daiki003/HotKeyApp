using System.IO;
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

        public string ReadCurrentBranch(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            using var repository = new Repository(_repositoryPath);
            return repository.Info.IsHeadDetached
                ? $"detached HEAD ({repository.Head.Tip.Id.Sha[..7]})"
                : repository.Head.FriendlyName;
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

        public GitDiffContent ReadDiff(GitDiffFile file, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (file.IsConflicted)
                return new GitDiffContent(Array.Empty<GitDiffLine>(), 0, 0, "競合中のファイルです。エディターで競合マーカーを確認してください。");

            using var repository = new Repository(_repositoryPath);
            string[] paths = new[] { file.Path, file.OldPath }.Where(path => !string.IsNullOrEmpty(path)).Distinct(StringComparer.Ordinal).ToArray();
            var explicitPaths = new ExplicitPathsOptions { ShouldFailOnUnmatchedPath = false };
            var options = new CompareOptions { ContextLines = 3, Similarity = SimilarityOptions.Renames };
            using var patch = file.IsStaged
                ? repository.Diff.Compare<Patch>(repository.Head.Tip?.Tree, DiffTargets.Index, paths, explicitPaths, options)
                : repository.Diff.Compare<Patch>(paths, true, explicitPaths, options);
            cancellationToken.ThrowIfCancellationRequested();
            if (patch.Any(entry => entry.IsBinaryComparison))
                return new GitDiffContent(Array.Empty<GitDiffLine>(), 0, 0, "バイナリファイルの内容が変更されています。テキスト差分は表示できません。");
            if (!patch.Any())
                return new GitDiffContent(Array.Empty<GitDiffLine>(), 0, 0, "現在、このファイルに表示できる差分はありません。F5 で一覧を更新できます。");

            var lines = ParsePatch(patch.Content, cancellationToken, out bool truncated);
            string message = truncated ? $"差分が大きいため、先頭 {MaxDisplayedLines:N0} 行まで表示しています。" :
                lines.Count == 0 ? "ファイル名またはファイル属性のみが変更されています。" : string.Empty;
            return new GitDiffContent(lines, patch.LinesAdded, patch.LinesDeleted, message, patch.Content);
        }

        private static IReadOnlyList<GitDiffLine> ParsePatch(string patch, CancellationToken cancellationToken, out bool truncated)
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
                if (result.Count >= MaxDisplayedLines) { truncated = true; break; }
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
