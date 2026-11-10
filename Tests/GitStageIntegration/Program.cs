using System.Diagnostics;
using System.IO;
using System.Text;
using HotKeyCommandApp.Models;
using HotKeyCommandApp.Services;

internal static class Program
{
    private static readonly Encoding Utf8 = new UTF8Encoding(false);

    private static async Task Main()
    {
        string workspace = Path.Combine(Path.GetTempPath(), "HotKeyGitStageTests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(workspace);
        string config = Path.Combine(workspace, "gitconfig");
        File.WriteAllText(config, "", Utf8);
        Environment.SetEnvironmentVariable("GIT_CONFIG_GLOBAL", config);
        Environment.SetEnvironmentVariable("GIT_CONFIG_NOSYSTEM", "1");
        try
        {
            await TestPartialChangesAsync(Path.Combine(workspace, "repository"));
            await TestInitialCommitAsync(Path.Combine(workspace, "unborn"));
            await TestDiscardAndStageAllAsync(Path.Combine(workspace, "discard"));
            Console.WriteLine("すべての Git ステージ操作テストに成功しました。");
        }
        finally
        {
            foreach (string file in Directory.EnumerateFiles(workspace, "*", SearchOption.AllDirectories))
                File.SetAttributes(file, FileAttributes.Normal);
            Directory.Delete(workspace, recursive: true);
        }
    }

    private static async Task TestPartialChangesAsync(string root)
    {
        Initialize(root);
        string path = "sample 日本語 file.txt";
        string file = Path.Combine(root, path);
        var contents = Enumerable.Range(0, 30).Select(i => "line-" + i).ToArray();
        contents[2] = "old-first";
        contents[22] = "old-second";
        WriteLines(file, contents);
        WriteLines(Path.Combine(root, "side.txt"), "base");
        Git(root, "add", "--all");
        Git(root, "commit", "-m", "fixture");

        contents[2] = "new-first";
        contents[22] = "new-second";
        WriteLines(file, contents);
        WriteLines(Path.Combine(root, "side.txt"), "staged-side");
        Git(root, "add", "--", "side.txt");
        var diff = new GitDiffService(root);
        var stage = new GitStageService(root);
        var entry = Entry(diff, path);
        var snapshot = diff.ReadDiff(entry, CancellationToken.None);
        await stage.StageLinesAsync(entry, snapshot, Select(snapshot, "old-first", "new-first"));
        string index = Git(root, "show", ":" + path);
        Check(index.Contains("new-first\n") && index.Contains("old-second\n") && !index.Contains("new-second\n"), "選択行のみステージ");
        Check(Git(root, "show", ":side.txt") == "staged-side\n", "別ファイルのステージを維持");

        entry = Entry(diff, path);
        snapshot = diff.ReadDiff(entry, CancellationToken.None);
        await stage.StageLinesAsync(entry, snapshot, Select(snapshot, "old-second", "new-second"));
        index = Git(root, "show", ":" + path);
        Check(index.Contains("new-first\n") && index.Contains("new-second\n"), "同一ファイルの既存ステージを維持");
        await stage.UnstageFileAsync(Entry(diff, path, staged: true));
        Check(Git(root, "show", ":" + path).Contains("old-first\n") && Git(root, "show", ":side.txt") == "staged-side\n", "ファイル単位の解除");

        entry = Entry(diff, path);
        snapshot = diff.ReadDiff(entry, CancellationToken.None);
        await stage.StageLinesAsync(entry, snapshot, Select(snapshot, "new-first"));
        Check(Git(root, "show", ":" + path).Contains("old-first\nnew-first\n"), "追加行だけをステージ");
        await stage.UnstageFileAsync(Entry(diff, path, staged: true));
        entry = Entry(diff, path);
        snapshot = diff.ReadDiff(entry, CancellationToken.None);
        await stage.StageLinesAsync(entry, snapshot, Select(snapshot, "old-first"));
        index = Git(root, "show", ":" + path);
        Check(!index.Contains("old-first\n") && !index.Contains("new-first\n"), "削除行だけをステージ");
        await stage.UnstageFileAsync(Entry(diff, path, staged: true));

        entry = Entry(diff, path);
        snapshot = diff.ReadDiff(entry, CancellationToken.None);
        File.AppendAllText(file, "external edit\n", Utf8);
        bool rejected = false;
        try { await stage.StageLinesAsync(entry, snapshot, Select(snapshot, "old-first")); }
        catch (InvalidOperationException ex) { rejected = ex.Message.Contains("変更されました"); }
        Check(rejected && Git(root, "show", ":" + path).Contains("old-first\n"), "更新前の差分を拒否");
        entry = Entry(diff, path);
        snapshot = diff.ReadDiff(entry, CancellationToken.None);
        await stage.StageLinesAsync(entry, snapshot, Select(snapshot, "old-first", "new-first"));
        await stage.CommitStagedAsync("partial commit fixture");
        string committed = Git(root, "show", "HEAD:" + path);
        Check(committed.Contains("new-first\n") && committed.Contains("old-second\n") &&
              Git(root, "show", "HEAD:side.txt") == "staged-side\n" && File.ReadAllText(file).Contains("new-second\n"), "ステージ済みだけをコミット");

        string added = "new 日本語.txt";
        WriteLines(Path.Combine(root, added), "new-a", "new-b", "new-c");
        entry = Entry(diff, added);
        snapshot = diff.ReadDiff(entry, CancellationToken.None);
        await stage.StageLinesAsync(entry, snapshot, Select(snapshot, "new-b"));
        Check(Git(root, "show", ":" + added) == "new-b\n", "新規ファイルの部分ステージ");
        await stage.UnstageFileAsync(Entry(diff, added, staged: true));
        Check(File.Exists(Path.Combine(root, added)), "新規ファイルの解除で作業ファイルを維持");

        string deleted = "delete 日本語.txt";
        string deletedPath = Path.Combine(root, deleted);
        WriteLines(deletedPath, "delete-a", "delete-b", "delete-c");
        Git(root, "add", "--", deleted);
        Git(root, "commit", "-m", "delete fixture");
        File.Delete(deletedPath);
        entry = Entry(diff, deleted);
        snapshot = diff.ReadDiff(entry, CancellationToken.None);
        await stage.StageLinesAsync(entry, snapshot, Select(snapshot, "delete-b"));
        index = Git(root, "show", ":" + deleted);
        Check(index == "delete-a\ndelete-c\n", "削除ファイルの部分ステージ");
        await stage.UnstageFileAsync(Entry(diff, deleted, staged: true));
        entry = Entry(diff, deleted);
        snapshot = diff.ReadDiff(entry, CancellationToken.None);
        await stage.StageLinesAsync(entry, snapshot, Select(snapshot, "delete-a", "delete-b", "delete-c"));
        Check(Entry(diff, deleted, staged: true).Change == "削除", "ファイル全体の削除をステージ");

        string noEnd = "no-end.txt";
        string noEndPath = Path.Combine(root, noEnd);
        File.WriteAllText(noEndPath, "old", Utf8);
        Git(root, "add", "--", noEnd);
        Git(root, "commit", "-m", "no newline fixture");
        File.WriteAllText(noEndPath, "new", Utf8);
        entry = Entry(diff, noEnd);
        snapshot = diff.ReadDiff(entry, CancellationToken.None);
        await stage.StageLinesAsync(entry, snapshot, Select(snapshot, "new"));
        Check(Git(root, "show", ":" + noEnd) == "old\nnew", "末尾改行のないファイルへの行追加");
        await stage.UnstageFileAsync(Entry(diff, noEnd, staged: true));
        entry = Entry(diff, noEnd);
        snapshot = diff.ReadDiff(entry, CancellationToken.None);
        await stage.StageLinesAsync(entry, snapshot, Select(snapshot, "old", "new"));
        Check(Git(root, "show", ":" + noEnd) == "new", "末尾改行のない行の置換");

        string newNoEnd = "new-no-end.txt";
        File.WriteAllText(Path.Combine(root, newNoEnd), "first\nlast", Utf8);
        entry = Entry(diff, newNoEnd);
        snapshot = diff.ReadDiff(entry, CancellationToken.None);
        await stage.StageLinesAsync(entry, snapshot, Select(snapshot, "last"));
        Check(Git(root, "show", ":" + newNoEnd) == "last", "新規ファイルの末尾改行を維持");

        string crlf = "crlf.txt";
        File.WriteAllText(Path.Combine(root, ".gitattributes"), "crlf.txt text eol=crlf\n", Utf8);
        File.WriteAllText(Path.Combine(root, crlf), "one\r\nold\r\nend\r\n", Utf8);
        Git(root, "add", "--", ".gitattributes", crlf);
        Git(root, "commit", "-m", "crlf fixture");
        File.WriteAllText(Path.Combine(root, crlf), "one\r\nnew\r\nend\r\n", Utf8);
        entry = Entry(diff, crlf);
        snapshot = diff.ReadDiff(entry, CancellationToken.None);
        await stage.StageLinesAsync(entry, snapshot, Select(snapshot, "old", "new"));
        Check(Git(root, "show", ":" + crlf) == "one\nnew\nend\n" && File.ReadAllText(Path.Combine(root, crlf)).Contains("\r\n"), "CRLFと属性フィルター");

        string shifts = "shifts.txt";
        var shiftLines = Enumerable.Range(1, 40).Select(i => "shift-" + i).ToList();
        WriteLines(Path.Combine(root, shifts), shiftLines.ToArray());
        Git(root, "add", "--", shifts);
        Git(root, "commit", "-m", "multiple hunks fixture");
        shiftLines.Insert(2, "inserted");
        shiftLines.Remove("shift-35");
        WriteLines(Path.Combine(root, shifts), shiftLines.ToArray());
        entry = Entry(diff, shifts);
        snapshot = diff.ReadDiff(entry, CancellationToken.None);
        await stage.StageLinesAsync(entry, snapshot, Select(snapshot, "inserted", "shift-35"));
        index = Git(root, "show", ":" + shifts);
        Check(index.Contains("shift-2\ninserted\nshift-3") && !index.Contains("shift-35\n"), "複数の変更箇所の行番号を維持");

        string oldName = "rename old 日本語.txt", newName = "rename new 日本語.txt";
        WriteLines(Path.Combine(root, oldName), "rename-content");
        Git(root, "add", "--", oldName);
        Git(root, "commit", "-m", "rename fixture");
        Git(root, "mv", "--", oldName, newName);
        var renamed = Entry(diff, newName, staged: true);
        Check(renamed.OldPath == oldName, "ステージ済みの改名を検出");
        await stage.UnstageFileAsync(renamed);
        Check(Git(root, "show", ":" + oldName) == "rename-content\n" && File.Exists(Path.Combine(root, newName)), "改名のステージ解除");
    }

    private static async Task TestInitialCommitAsync(string root)
    {
        Initialize(root);
        WriteLines(Path.Combine(root, "first.txt"), "first");
        var diff = new GitDiffService(root);
        var stage = new GitStageService(root);
        await stage.StageFileAsync(Entry(diff, "first.txt"));
        await stage.UnstageFileAsync(Entry(diff, "first.txt", staged: true));
        Check(File.Exists(Path.Combine(root, "first.txt")) && !Entry(diff, "first.txt").IsStaged, "初回コミット前の解除");
        await stage.StageFileAsync(Entry(diff, "first.txt"));
        await stage.CommitStagedAsync("first commit fixture");
        Check(Git(root, "show", "HEAD:first.txt") == "first\n", "初回コミット");
    }

    private static async Task TestDiscardAndStageAllAsync(string root)
    {
        Initialize(root);
        string path = Path.Combine(root, "tracked.txt");
        WriteLines(path, "one", "old", "end");
        WriteLines(Path.Combine(root, "other.txt"), "base");
        Git(root, "add", "--all");
        Git(root, "commit", "-m", "fixture");
        var diff = new GitDiffService(root);
        var stage = new GitStageService(root);
        WriteLines(Path.Combine(root, "other.txt"), "staged");
        Git(root, "add", "other.txt");
        WriteLines(path, "one", "new", "end");
        var file = Entry(diff, "tracked.txt");
        var snapshot = diff.ReadDiff(file, CancellationToken.None);
        await stage.DiscardLinesAsync(file, snapshot, Select(snapshot, "old", "new"));
        Check(File.ReadAllText(path) == "one\nold\nend\n" && Git(root, "show", ":other.txt") == "staged\n", "選択行の破棄と別ファイルのステージを維持");
        WriteLines(path, "one", "new", "end");
        file = Entry(diff, "tracked.txt"); snapshot = diff.ReadDiff(file, CancellationToken.None);
        await stage.DiscardLinesAsync(file, snapshot, Select(snapshot, "new"));
        Check(File.ReadAllText(path) == "one\nend\n", "追加行のみ破棄");
        WriteLines(path, "one", "new", "end");
        file = Entry(diff, "tracked.txt"); snapshot = diff.ReadDiff(file, CancellationToken.None);
        await stage.DiscardLinesAsync(file, snapshot, Select(snapshot, "old"));
        Check(File.ReadAllText(path) == "one\nold\nnew\nend\n", "削除行のみ破棄");
        file = Entry(diff, "tracked.txt"); snapshot = diff.ReadDiff(file, CancellationToken.None);
        File.AppendAllText(path, "external\n", Utf8);
        bool rejected = false;
        try { await stage.DiscardLinesAsync(file, snapshot, Select(snapshot, "new")); }
        catch (InvalidOperationException ex) { rejected = ex.Message.Contains("変更されました"); }
        Check(rejected && File.ReadAllText(path).Contains("external"), "古い差分の破棄を拒否");
        file = Entry(diff, "tracked.txt"); snapshot = diff.ReadDiff(file, CancellationToken.None);
        await stage.DiscardFileAsync(file, snapshot);
        Check(File.ReadAllText(path) == "one\nold\nend\n", "ファイル単位の破棄");
        WriteLines(Path.Combine(root, "new.txt"), "untracked");
        file = Entry(diff, "new.txt"); snapshot = diff.ReadDiff(file, CancellationToken.None);
        await stage.DiscardFileAsync(file, snapshot);
        Check(!File.Exists(Path.Combine(root, "new.txt")), "未追跡ファイルの破棄");
        WriteLines(Path.Combine(root, "new.txt"), "first", "second");
        file = Entry(diff, "new.txt"); snapshot = diff.ReadDiff(file, CancellationToken.None);
        await stage.DiscardLinesAsync(file, snapshot, Select(snapshot, "first"));
        Check(File.ReadAllText(Path.Combine(root, "new.txt")) == "second\n", "未追跡ファイルの選択行を破棄");
        File.Delete(path);
        file = Entry(diff, "tracked.txt"); snapshot = diff.ReadDiff(file, CancellationToken.None);
        await stage.DiscardLinesAsync(file, snapshot, Select(snapshot, "old"));
        Check(File.ReadAllText(path) == "old\n", "削除ファイルの選択行を復元");
        WriteLines(path, "one", "old", "end");
        File.WriteAllText(path, "one\nnew\nend", Utf8);
        file = Entry(diff, "tracked.txt"); snapshot = diff.ReadDiff(file, CancellationToken.None);
        await stage.DiscardLinesAsync(file, snapshot, Select(snapshot, "old", "new"));
        Check(File.ReadAllText(path) == "one\nold\nend", "末尾改行のない差分の部分破棄");
        WriteLines(Path.Combine(root, ".gitattributes"), "crlf.txt text eol=crlf");
        File.WriteAllText(Path.Combine(root, "crlf.txt"), "one\r\nold\r\nend\r\n", Utf8);
        Git(root, "add", "--", ".gitattributes", "crlf.txt");
        Git(root, "commit", "-m", "crlf fixture");
        File.WriteAllText(Path.Combine(root, "crlf.txt"), "one\r\nnew\r\nend\r\n", Utf8);
        file = Entry(diff, "crlf.txt"); snapshot = diff.ReadDiff(file, CancellationToken.None);
        await stage.DiscardLinesAsync(file, snapshot, Select(snapshot, "old", "new"));
        Check(File.ReadAllText(Path.Combine(root, "crlf.txt")) == "one\r\nold\r\nend\r\n", "CRLFを維持して選択行を破棄");
        File.WriteAllText(Path.Combine(root, "no-end.txt"), "old", Utf8);
        Git(root, "add", "--", "no-end.txt");
        Git(root, "commit", "-m", "no newline fixture");
        File.WriteAllText(Path.Combine(root, "no-end.txt"), "new", Utf8);
        file = Entry(diff, "no-end.txt"); snapshot = diff.ReadDiff(file, CancellationToken.None);
        await stage.DiscardLinesAsync(file, snapshot, Select(snapshot, "old", "new"));
        Check(File.ReadAllText(Path.Combine(root, "no-end.txt")) == "old", "末尾改行のない最終行の置換を破棄");
        var many = Enumerable.Range(0, 40).Select(i => "line-" + i).ToArray();
        WriteLines(Path.Combine(root, "many.txt"), many);
        Git(root, "add", "--", "many.txt");
        Git(root, "commit", "-m", "multiple hunks fixture");
        many[2] = "changed-2"; many[35] = "changed-35";
        WriteLines(Path.Combine(root, "many.txt"), many);
        file = Entry(diff, "many.txt"); snapshot = diff.ReadDiff(file, CancellationToken.None);
        await stage.DiscardLinesAsync(file, snapshot, Select(snapshot, "line-2", "changed-2", "line-35", "changed-35"));
        Check(File.ReadAllText(Path.Combine(root, "many.txt")).Contains("line-2\n") &&
            File.ReadAllText(Path.Combine(root, "many.txt")).Contains("line-35\n"), "複数箇所の選択行を破棄");
        WriteLines(Path.Combine(root, "new.txt"), "new");
        WriteLines(path, "updated");
        await stage.StageAllAsync();
        Check(Git(root, "show", ":new.txt") == "new\n" && Git(root, "show", ":tracked.txt") == "updated\n" &&
            Git(root, "show", ":other.txt") == "staged\n", "すべてステージして既存ステージを維持");
    }

    private static void Initialize(string root)
    {
        Directory.CreateDirectory(root);
        Git(root, "init", "-b", "main");
        Git(root, "config", "user.name", "Checks");
        Git(root, "config", "user.email", "checks@example.invalid");
        Git(root, "config", "core.autocrlf", "false");
    }

    private static void WriteLines(string path, params string[] lines) => File.WriteAllText(path, string.Join("\n", lines) + "\n", Utf8);
    private static GitDiffFile Entry(GitDiffService service, string path, bool staged = false) =>
        service.ReadFiles(CancellationToken.None).First(file => file.Path == path && file.IsStaged == staged);
    private static int[] Select(GitDiffContent diff, params string[] text) => diff.Lines.Select((line, index) => (line, index))
        .Where(item => item.line.Kind is GitDiffLineKind.Added or GitDiffLineKind.Deleted && text.Contains(item.line.Text))
        .Select(item => item.index).ToArray();

    private static void Check(bool condition, string name)
    {
        if (!condition) throw new InvalidOperationException("失敗: " + name);
        Console.WriteLine("成功: " + name);
    }

    private static string Git(string root, params string[] args)
    {
        var psi = new ProcessStartInfo("git")
        {
            WorkingDirectory = root, UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardOutput = true, RedirectStandardError = true, StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8
        };
        foreach (string arg in args) psi.ArgumentList.Add(arg);
        using var process = Process.Start(psi) ?? throw new InvalidOperationException("Git を起動できませんでした。");
        Task<string> stdout = process.StandardOutput.ReadToEndAsync(), stderr = process.StandardError.ReadToEndAsync();
        process.WaitForExit();
        if (process.ExitCode != 0) throw new InvalidOperationException(stderr.GetAwaiter().GetResult());
        return stdout.GetAwaiter().GetResult();
    }
}
