using System.IO;
using HotKeyCommandApp.Models;
using HotKeyCommandApp.Services;
using static TestSupport;

internal static class PaletteTests
{
    public static async Task RunAsync(string workspace)
    {
        TestHierarchy();
        TestHistory();
        await TestFileSearchAsync(workspace);
        TestSettingsSession();
    }

    private static void TestHierarchy()
    {
        var target = new CommandEntry { Name = "same", Hotkey = "Ctrl+X", HotkeyType = HotkeyType.WindowGlobal };
        var other = new CommandEntry { Name = "same", Hotkey = "Ctrl+X", HotkeyType = HotkeyType.HierarchyLocal };
        var parent = new CommandEntry { Name = "parent", Children = new() { target } };
        var root = new List<CommandEntry> { other, parent };
        Check(ReferenceEquals(CommandTreeService.FindByHotkey(root, "Ctrl+X", HotkeyType.WindowGlobal), target), "階層とショートカット種別を考慮した検索");
        var path = new List<CommandEntry>();
        Check(CommandTreeService.TryFindPath(root, target, path) && path.SequenceEqual(new[] { parent }), "対象自身を含めない祖先パスの取得");
        var replacement = new CommandEntry { Name = "replacement" };
        Check(CommandTreeService.Replace(root, target, replacement) && ReferenceEquals(parent.Children[0], replacement) && root[0] == other,
            "同名の別項目に影響せず階層内の対象を置換");
        Check(CommandTreeService.Remove(root, replacement) && parent.Children.Count == 0 && !CommandTreeService.Remove(root, target),
            "階層内の削除と存在しない項目の処理");
        path.Clear();
        Check(!CommandTreeService.TryFindPath(root, target, path) && path.Count == 0, "探索失敗時に途中のパスを残さない");
    }

    private static void TestHistory()
    {
        var history = new PaletteNavigationHistory();
        var selected = new CommandEntry { Name = "selected" };
        var first = new PaletteNavigationState { Title = "first", SelectedItem = selected };
        var second = new PaletteNavigationState { Title = "second", IsInputMode = true, InputText = "query", InputPrompt = "prompt" };
        history.Record(first);
        history.Record(first);
        history.Record(second);
        Check(history.TryGoBack(out var back) && ReferenceEquals(back, first) && !history.TryGoBack(out _), "重複する状態を履歴に増やさない");
        Check(history.TryGoForward(out var forward) && ReferenceEquals(forward, second) && forward.InputText == "query", "入力状態を含む履歴の復元");
        history.TryGoBack(out _);
        history.Record(new PaletteNavigationState { Title = "third" });
        Check(!history.TryGoForward(out _), "戻った位置から新しい操作をすると進む履歴を破棄");
        for (int i = 0; i < 60; i++) history.Record(new PaletteNavigationState { Title = i.ToString() });
        int steps = 0;
        PaletteNavigationState? oldest = null;
        while (history.TryGoBack(out var state)) { steps++; oldest = state; }
        Check(steps == 49 && oldest?.Title == "10", "履歴を直近50件に制限");
    }

    private static async Task TestFileSearchAsync(string workspace)
    {
        string root = Path.Combine(workspace, "files");
        Directory.CreateDirectory(Path.Combine(root, "nested"));
        Directory.CreateDirectory(Path.Combine(root, "Windows"));
        File.WriteAllText(Path.Combine(root, "needle.txt"), "");
        File.WriteAllText(Path.Combine(root, "nested", "NEEDLE.md"), "");
        File.WriteAllText(Path.Combine(root, "Windows", "needle-hidden.txt"), "");
        var search = new CommandFileSearchService();
        var result = await search.SearchAsync(Path.Combine(root, "needle"), CancellationToken.None);
        Check(!result.IsDriveList && result.Paths.Count == 2 && result.Paths.All(path => !path.Contains("hidden")),
            "絶対パス検索は子階層を探索しシステム名のフォルダを除外");
        Check(search.SearchFolder(root, "NEEDLE").SequenceEqual(new[] { Path.Combine(root, "needle.txt") }), "フォルダ内検索は直下だけを大文字小文字を区別せず検索");
        for (int i = 0; i < 35; i++) File.WriteAllText(Path.Combine(root, $"cap-{i}.txt"), "");
        Check((await search.SearchAsync(Path.Combine(root, "cap-"), CancellationToken.None)).Paths.Count == 30 && search.SearchFolder(root, "cap-").Count == 30,
            "検索結果を30件に制限");
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        bool canceled = false;
        try { await search.SearchAsync(Path.Combine(root, "needle"), cancellation.Token); }
        catch (OperationCanceledException) { canceled = true; }
        Check(canceled, "開始前にキャンセルした検索を実行しない");
        Check((await search.SearchAsync(Path.Combine(workspace, "missing", "needle"), CancellationToken.None)).IsDriveList,
            "検索の起点が存在しない場合のドライブ一覧");
    }

    private static void TestSettingsSession()
    {
        var source = new AppSettings
        {
            Constants = new() { new ConstantEntry { Name = "ROOT", Value = "before" } },
            SelectTemplates = new() { new SelectTemplate { Name = "branch", Options = new() { "main" } } }
        };
        var session = AppSettingsEditorSession.Create(source);
        session.Constants[0].Value = "after";
        session.SelectTemplates[0].Options.Add("feature");
        Check(source.Constants[0].Value == "before" && source.SelectTemplates[0].Options.Count == 1, "設定の編集中は保存前の値を維持");
        session.ApplyTo(source);
        session.SelectTemplates[0].Options.Clear();
        Check(source.Constants[0].Value == "after" && source.SelectTemplates[0].Options.SequenceEqual(new[] { "main", "feature" }),
            "設定保存後も編集セッションと値を共有しない");
    }
}
