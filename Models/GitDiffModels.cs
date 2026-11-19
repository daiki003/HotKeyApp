namespace HotKeyCommandApp.Models
{
    public sealed record GitDiffFile(string Path, string OldPath, bool IsStaged, string Change, bool IsConflicted = false, bool IsStashPrepared = false,
        string SourceRevision = null, string TargetRevision = null, string ComparisonLabel = null)
    {
        public bool IsReadOnly => SourceRevision != null;
        public string Section => IsReadOnly ? TargetRevision == null ? "ベースとの差分" : "コミット差分" : IsStashPrepared ? "スタッシュ準備群" : IsStaged ? "ステージ済み" : "未ステージ";
        public string Key => $"{SourceRevision}:{TargetRevision}:{IsStaged}:{Path}";
        public string DisplayPath => string.IsNullOrEmpty(OldPath) || OldPath == Path ? Path : $"{OldPath} → {Path}";
        public string FileName => System.IO.Path.GetFileName(Path);
        public string DirectoryName => System.IO.Path.GetDirectoryName(Path)?.Replace('\\', '/') ?? "";
        public string Comparison => ComparisonLabel ?? (IsStaged ? "HEAD → インデックス" : "インデックス → 作業ツリー");
    }

    public enum GitDiffMode { Uncommitted, BaseBranch, Commit }
    public enum GitDiffSortOrder { Path, FileName, Change, Manual }
    public sealed record GitDiffCommit(string Sha, string Subject, string Author, DateTimeOffset Date)
    {
        public string ShortSha => Sha[..7];
        public string Display => $"{ShortSha}  {Subject}";
        public string Details => $"{Author} · {Date.ToLocalTime():yyyy/MM/dd HH:mm}";
    }
    public sealed record GitDiffSearchResult(GitDiffFile File, int LineIndex, int Start, int Length, GitDiffLine Line)
    {
        public string Location => $"{File.DisplayPath}  ·  {Line.Marker}{(string.IsNullOrEmpty(Line.NewNumber) ? Line.OldNumber : Line.NewNumber)}";
        public string Preview => Line.Text.Trim();
    }

    public enum GitDiffLineKind { Context, Added, Deleted, Hunk, Notice }

    public sealed record GitDiffLine(string OldNumber, string NewNumber, string Marker, string Text, GitDiffLineKind Kind);

    public sealed record GitDiffContent(IReadOnlyList<GitDiffLine> Lines, int Added, int Deleted, string Message, string RawPatch = "");

    public sealed record GitDiffBranchInfo(string CurrentBranch, string BranchName, string TrackedBranch, int? AheadBy, int? BehindBy);
}
