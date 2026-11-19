using System.Diagnostics;
using System.IO;
using HotKeyCommandApp.Models;
using HotKeyCommandApp.Services;
using HotKeyCommandApp.ViewModels;
using LibGit2Sharp;
using static TestSupport;

internal static class GitCommandTests
{
    public static async Task RunAsync(string workspace)
    {
        var arguments = GitCommandText.ParseArguments("  feature \"message with spaces\"  日本語 ");
        Check(arguments.SequenceEqual(new[] { "feature", "message with spaces", "日本語" }), "引用符・連続空白・日本語の引数");
        Check(GitCommandText.GetRequiredArgumentCount(new[] { "git {0}", "git {3} {base} {select:x}" }) == 4,
            "複数コマンドで必要な引数の数");
        Check(GitCommandText.ReplaceArguments("{1}/{0}/{1}/{9}/{base}", arguments) == "message with spaces/feature/message with spaces//{base}",
            "引数の順序・再利用・不足引数と名前付きプレースホルダー");

        var history = new GitConsoleHistory();
        history.SetEntry("first", "before\r\n");
        history.SetEntry("running", "");
        history.AppendLines(Enumerable.Range(0, 405).Select(index => "line-" + index));
        string latest = history.Render(historyMode: false);
        Check(latest.StartsWith("... 5 lines omitted ...\nline-5\n") && latest.EndsWith("line-404"), "大量出力を末尾400行に制限");
        string all = history.Render(historyMode: true);
        Check(all.Contains("[first]\nbefore") && all.Contains("[running]\n... 5 lines omitted ..."), "実行中の出力を同じ履歴へ反映");
        history.SetEntry("finished", "after", replaceCurrent: true);
        all = history.Render(historyMode: true);
        Check(all.Contains("[first]\nbefore") && all.Contains("[finished]\nafter") && !all.Contains("[running]"), "再描画で履歴を重複させない");
        Check(history.Render(historyMode: false) == "after", "履歴表示から最新出力へ復帰");

        var runner = new GitProcessRunner();
        foreach (string mode in new[] { "--utf8", "--sjis" })
        {
            var stdout = new List<string>();
            var stderr = new List<string>();
            int code = await runner.RunAsync(CreateProcess(mode), CancellationToken.None, stdout.Add, stderr.Add);
            Check(code == 7 && stdout.SequenceEqual(new[] { "日本語", "", "last" }) && stderr.SequenceEqual(new[] { "エラー" }),
                mode + " の標準出力・標準エラー・最終行・終了コード");
            Check(!runner.IsRunning, "終了したプロセスの状態を解除");
        }

        using var cancellation = new CancellationTokenSource();
        var started = new TaskCompletionSource<int>();
        Task<int> execution = runner.RunAsync(CreateProcess("--wait"), cancellation.Token,
            line => started.TrySetResult(int.Parse(line)));
        int childId = await started.Task.WaitAsync(TimeSpan.FromSeconds(10));
        Check(runner.IsRunning, "実行中プロセスの検出");
        cancellation.Cancel();
        bool canceled = false;
        try { await execution.WaitAsync(TimeSpan.FromSeconds(10)); }
        catch (OperationCanceledException) { canceled = true; }
        Check(canceled && !runner.IsRunning && !ProcessExists(childId), "キャンセル時にプロセスを終了して状態を解除");

        await TestViewModelAsync(workspace);
    }

    private static ProcessStartInfo CreateProcess(string mode)
    {
        var start = new ProcessStartInfo(Environment.ProcessPath!)
        {
            UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardOutput = true, RedirectStandardError = true
        };
        start.ArgumentList.Add(mode);
        return start;
    }

    private static bool ProcessExists(int id)
    {
        try { using var process = Process.GetProcessById(id); return !process.HasExited; }
        catch (ArgumentException) { return false; }
    }

    private static async Task TestViewModelAsync(string workspace)
    {
        string repositoryPath = Path.Combine(workspace, "commands");
        Repository.Init(repositoryPath);
        var settings = new GitSettings
        {
            Aliases = new() { new GitAliasEntry { Alias = "say", TargetCommand = "echo {0}" } },
            Functions = new() { new GitFunctionEntry { Name = "twice", Commands = new() { "echo first-{0}", "echo second-{0}" } } }
        };
        new GitSettingsManager().SaveSettings(settings);
        var viewModel = new GitCommandViewModel(repositoryPath);
        viewModel.InputText = "say \"hello world\"";
        viewModel.ExecuteCommand.Execute(null);
        viewModel.InputText = "twice sample";
        viewModel.ExecuteCommand.Execute(null);
        await WaitUntilAsync(() => viewModel.CurrentCommandStatus == "完了" &&
            viewModel.ConsoleOutput.Contains("second-sample") && string.IsNullOrEmpty(viewModel.WaitingCommandsText));
        viewModel.InputText = "history";
        viewModel.ExecuteCommand.Execute(null);
        await WaitUntilAsync(() => viewModel.IsHistoryMode);
        Check(viewModel.ConsoleOutput.Contains("hello world") && viewModel.ConsoleOutput.Contains("first-sample") &&
            viewModel.ConsoleOutput.Contains("second-sample"), "画面からエイリアス・関数を順番に実行して履歴表示");
        viewModel.IsHistoryMode = false;
        Check(viewModel.ConsoleOutput.Contains("second-sample") && !viewModel.ConsoleOutput.Contains("hello world"), "画面の履歴モード解除");
    }
}
