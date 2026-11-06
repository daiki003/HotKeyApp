namespace HotKeyCommandApp.Models
{
    public sealed record GitDiffFile(string Path, string OldPath, bool IsStaged, string Change, bool IsConflicted = false)
    {
        public string Section => IsStaged ? "ステージ済み" : "未ステージ";
        public string Key => $"{IsStaged}:{Path}";
        public string DisplayPath => string.IsNullOrEmpty(OldPath) || OldPath == Path ? Path : $"{OldPath} → {Path}";
        public string FileName => System.IO.Path.GetFileName(Path);
        public string DirectoryName => System.IO.Path.GetDirectoryName(Path)?.Replace('\\', '/') ?? "";
        public string Comparison => IsStaged ? "HEAD → インデックス" : "インデックス → 作業ツリー";
    }

    public enum GitDiffLineKind { Context, Added, Deleted, Hunk, Notice }

    public sealed record GitDiffLine(string OldNumber, string NewNumber, string Marker, string Text, GitDiffLineKind Kind);

    public sealed record GitDiffContent(IReadOnlyList<GitDiffLine> Lines, int Added, int Deleted, string Message);
}
