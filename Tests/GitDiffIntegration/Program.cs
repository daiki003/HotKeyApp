using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using HotKeyCommandApp.Models;
using HotKeyCommandApp.Services;
using HotKeyCommandApp.ViewModels;
using HotKeyCommandApp.ViewModels.Settings;
using HotKeyCommandApp.Views;
using HotKeyCommandApp.Views.Controls;
using LibGit2Sharp;

internal static class Program
{
    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr FindWindowEx(IntPtr parent, IntPtr after, string className, string windowName);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr GetWindow(IntPtr window, uint command);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool PostMessage(IntPtr window, uint message, IntPtr wParam, IntPtr lParam);

    [STAThread]
    private static int Main(string[] args)
    {
        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        app.Resources.MergedDictionaries.Add(new ResourceDictionary
        {
            Source = new Uri("/HotKeyCommandApp;component/Views/Controls/SettingsSharedResources.xaml", UriKind.Relative)
        });
        if (args.Length == 2 && args[0] == "--preview")
        {
            app.ShutdownMode = ShutdownMode.OnLastWindowClose;
            app.Startup += (_, _) => GitDiffWindow.ShowForRepository(args[1]);
            return app.Run();
        }
        string workspace = Path.Combine(Path.GetTempPath(), "HotKeyGitDiffTests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(workspace);
        app.Startup += async (_, _) =>
        {
            int result = 0;
            try
            {
                TestShortcutSettings();
                TestSyntaxHighlighting();
                TestIntralineHighlighting();
                await TestVsCodeArgumentsAsync(workspace);
                TestSyntaxContext(workspace);
                TestBranchInfo(workspace);
                await TestBranchOperationsAsync(workspace);
                await TestRepositoryTabsAsync(workspace);
                await TestRefreshOnActivationAsync(workspace);
                await TestIntralineViewAsync(workspace);
                await TestFileNavigationAsync(workspace);
                await TestSyntaxViewAsync(workspace);
                await TestFileActionNavigationAsync(workspace);
                await TestPlacementAndInitialFocusAsync(workspace);
                Console.WriteLine("差分タブとショートカット設定のテストに成功しました。");
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine(ex);
                result = 1;
            }
            finally
            {
                foreach (Window window in app.Windows.Cast<Window>().ToArray()) window.Close();
                foreach (string file in Directory.EnumerateFiles(workspace, "*", SearchOption.AllDirectories))
                    File.SetAttributes(file, FileAttributes.Normal);
                Directory.Delete(workspace, recursive: true);
                app.Shutdown(result);
            }
        };
        return app.Run();
    }

    private static void TestIntralineHighlighting()
    {
        var lines = new[]
        {
            new GitDiffLine("1", "", "−", "alpha old value end", GitDiffLineKind.Deleted),
            new GitDiffLine("", "1", "+", "alpha new value end", GitDiffLineKind.Added),
            new GitDiffLine("2", "2", "", "context", GitDiffLineKind.Context),
            new GitDiffLine("3", "", "−", "call(foo);", GitDiffLineKind.Deleted),
            new GitDiffLine("", "3", "+", "call(foo, bar);", GitDiffLineKind.Added)
        };
        var spans = GitDiffIntralineHighlighter.Highlight(lines, CancellationToken.None);
        Check(string.Concat(spans[0].Select(span => lines[0].Text.Substring(span.Start, span.Length))) == "old" &&
            string.Concat(spans[1].Select(span => lines[1].Text.Substring(span.Start, span.Length))) == "new", "変更された文字だけを強調");
        Check(spans[2].Count == 0 && spans[3].Count == 0 && spans[4].Single() == new GitDiffChangeSpan(8, 5), "追加文字と変更なしの箇所を区別");
        var unrelated = new[]
        {
            new GitDiffLine("1", "", "−", "    delete previous content", GitDiffLineKind.Deleted),
            new GitDiffLine("", "1", "+", "    entirely different replacement", GitDiffLineKind.Added)
        };
        Check(GitDiffIntralineHighlighter.Highlight(unrelated, CancellationToken.None).All(line => line.Count == 0), "大部分が異なる行にはマーカーを付けない");
        Check(GitDiffIntralineHighlighter.Highlight(new[] { lines[0], lines[0], lines[1], lines[1] }, CancellationToken.None)
            .All(line => line.Count == 0), "複数行の置換を強制的に対応付けない");
        var wordChange = new[]
        {
            new GitDiffLine("1", "", "−", "return settings.CurrentBranch;", GitDiffLineKind.Deleted),
            new GitDiffLine("", "1", "+", "return settings.DefaultBranch;", GitDiffLineKind.Added)
        };
        var words = GitDiffIntralineHighlighter.Highlight(wordChange, CancellationToken.None);
        Check(words[0].Single() == new GitDiffChangeSpan(16, 13) && words[1].Single() == new GitDiffChangeSpan(16, 13),
            "変更された単語の中を細切れにしない");
    }

    private static async Task TestVsCodeArgumentsAsync(string workspace)
    {
        var method = typeof(VsCodeFileOpener).GetMethod("CreateStartInfo", BindingFlags.NonPublic | BindingFlags.Static);
        string directory = Path.Combine(workspace, "VS Code");
        string cliPath = Path.Combine(directory, "resources", "app", "out", "cli.js");
        Directory.CreateDirectory(Path.GetDirectoryName(cliPath));
        File.WriteAllText(cliPath, "");
        var start = (ProcessStartInfo)method.Invoke(null, new object[] { Path.Combine(directory, "Code.exe"), @"C:\repo space", @"C:\repo space\file name.cs", 42 });
        Check(start.ArgumentList.SequenceEqual(new[] { cliPath, "--reuse-window", @"C:\repo space", "--goto", @"C:\repo space\file name.cs:42" }) &&
            start.WorkingDirectory == @"C:\repo space" && start.Environment["ELECTRON_RUN_AS_NODE"] == "1", "VS Code の CLI、リポジトリ、行番号を引数に指定");
        var fileOnly = (ProcessStartInfo)method.Invoke(null, new object[] { Path.Combine(directory, "Code.exe"), @"C:\repo space", @"C:\repo space\file name.cs", null });
        Check(fileOnly.ArgumentList.SequenceEqual(new[] { cliPath, "--reuse-window", @"C:\repo space", @"C:\repo space\file name.cs" }),
            "ファイル名から開く場合は行移動の引数を付けない");
        string executable = (string)typeof(VsCodeFileOpener).GetMethod("FindExecutable", BindingFlags.NonPublic | BindingFlags.Static).Invoke(null, null);
        if (executable == null) return;
        start = (ProcessStartInfo)method.Invoke(null, new object[] { executable, workspace, cliPath, 42 });
        start.ArgumentList.Add("--version");
        using var process = Process.Start(start);
        Task<string> stdout = process.StandardOutput.ReadToEndAsync(), stderr = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        Check(process.ExitCode == 0 && (await stdout).Contains('.') && string.IsNullOrWhiteSpace(await stderr), "実際の VS Code CLI が継承環境でも正常起動");
    }

    private static void TestSyntaxHighlighting()
    {
        var lines = new[]
        {
            new GitDiffLine("", "", "", "@@ -1,2 +1,2 @@", GitDiffLineKind.Hunk),
            new GitDiffLine("1", "", "−", "/* old comment", GitDiffLineKind.Deleted),
            new GitDiffLine("", "1", "+", "int value = 42; // comment", GitDiffLineKind.Added),
            new GitDiffLine("2", "2", "", "int next = 1;", GitDiffLineKind.Context)
        };
        var spans = GitDiffSyntaxHighlighter.Highlight(lines, "example.cs", CancellationToken.None);
        Check(spans[1].Any(span => span.Color.Equals("#6A9955", StringComparison.OrdinalIgnoreCase)), "C#のコメントをDark+配色で表示");
        Check(spans[2].Any(span => span.Color.Equals("#569CD6", StringComparison.OrdinalIgnoreCase)) &&
              spans[3].Any(span => span.Color.Equals("#569CD6", StringComparison.OrdinalIgnoreCase)), "追加・削除側の構文状態を分離");
        var json = new[] { new GitDiffLine("", "1", "+", "{ \"name\": \"value\" }", GitDiffLineKind.Added) };
        Check(GitDiffSyntaxHighlighter.Highlight(json, "sample.json", CancellationToken.None)[0]
            .Any(span => span.Color.Equals("#CE9178", StringComparison.OrdinalIgnoreCase)), "JSONの文字列をDark+配色で表示");
        Check(GitDiffSyntaxHighlighter.Highlight(json, "unknown.no-such-language", CancellationToken.None)[0].Count == 0,
            "未知のファイル形式は単色表示");
        var dart = new[] { new GitDiffLine("", "1", "+", "void main() { print(\"hello\"); }", GitDiffLineKind.Added) };
        Check(GitDiffSyntaxHighlighter.Highlight(dart, "sample.dart", CancellationToken.None)[0]
            .Select(span => span.Color).Distinct(StringComparer.OrdinalIgnoreCase).Count() > 1, "Dartを拡張子に応じて色分け");
        var xaml = new[] { new GitDiffLine("", "1", "+", "<Grid Name=\"root\" />", GitDiffLineKind.Added) };
        Check(GitDiffSyntaxHighlighter.Highlight(xaml, "sample.xaml", CancellationToken.None)[0]
            .Select(span => span.Color).Distinct(StringComparer.OrdinalIgnoreCase).Count() > 1, "XAMLをXMLの文法で色分け");
    }

    private static void TestSyntaxContext(string workspace)
    {
        string path = CreateRepository(workspace, "syntax-context", "comment.cs");
        string filePath = Path.Combine(path, "comment.cs");
        var lines = new[] { "/*", "one", "two", "three", "four", "five", "six", "old comment", "eight", "nine", "*/", "int end = 1;" };
        File.WriteAllLines(filePath, lines);
        using (var repository = new Repository(path))
        {
            Commands.Stage(repository, "comment.cs");
            var signature = new Signature("Test", "test@example.invalid", DateTimeOffset.UtcNow);
            repository.Commit("initial", signature, signature);
        }
        lines[7] = "new comment";
        File.WriteAllLines(filePath, lines);
        var service = new GitDiffService(path);
        var file = service.ReadFiles(CancellationToken.None).Single();
        var diff = service.ReadDiff(file, CancellationToken.None);
        var spans = GitDiffSyntaxHighlighter.Highlight(diff.Lines, file.Path, CancellationToken.None, path, file);
        int index = diff.Lines.ToList().FindIndex(line => line.Text == "new comment");
        Check(index >= 0 && spans[index].Any(span => span.Color.Equals("#6A9955", StringComparison.OrdinalIgnoreCase)),
            "差分外で始まる複数行コメントの色を維持");
        using (var repository = new Repository(path)) Commands.Stage(repository, "comment.cs");
        file = service.ReadFiles(CancellationToken.None).Single();
        diff = service.ReadDiff(file, CancellationToken.None);
        spans = GitDiffSyntaxHighlighter.Highlight(diff.Lines, file.Path, CancellationToken.None, path, file);
        index = diff.Lines.ToList().FindIndex(line => line.Text == "new comment");
        Check(index >= 0 && spans[index].Any(span => span.Color.Equals("#6A9955", StringComparison.OrdinalIgnoreCase)),
            "ステージ済み差分でも前後の構文状態を維持");
    }

    private static void TestShortcutSettings()
    {
        var config = new ConfigService();
        config.SaveSettings(new AppSettings());
        var source = new MainViewModel();
        source.OpenSettingsCommand.Execute(null);
        var section = new ShortcutSettingsSectionViewModel(source);
        Check(section.Records.Any(record => record.Key == "GitDiffFocus"), "差分フォーカスの設定行");
        section.BeginCapture("GitDiffFocus");
        section.CompleteCapture("Ctrl+Alt+F8");
        section.Save();
        source.SaveSettingsCommand.Execute(null);
        Check(config.LoadSettings().GitDiffFocusShortcut == "Ctrl+Alt+F8", "ショートカット設定の保存");

        var reloaded = new MainViewModel();
        reloaded.OpenSettingsCommand.Execute(null);
        var reloadedSection = new ShortcutSettingsSectionViewModel(reloaded);
        Check(reloadedSection.EditingGitDiffFocusShortcut == "Ctrl+Alt+F8", "再起動後の設定復元");
        reloadedSection.BeginCapture("GitDiffFocus");
        reloadedSection.CancelCapture();
        Check(reloadedSection.EditingGitDiffFocusShortcut == "Ctrl+Alt+F8", "キー入力のキャンセル");
        reloadedSection.BeginCapture("GitDiffFocus");
        reloadedSection.CompleteCapture(string.Empty);
        reloadedSection.Save();
        reloaded.SaveSettingsCommand.Execute(null);
        Check(config.LoadSettings().GitDiffFocusShortcut == string.Empty, "ショートカットの解除");

        reloaded.OpenSettingsCommand.Execute(null);
        var settings = new SettingsWindow(reloaded);
        settings.Show();
        ((TabControl)settings.FindName("MainTabControl")).SelectedIndex = 1;
        settings.UpdateLayout();
        SavePreview(settings, "shortcut-settings.png");
        settings.Close();
    }

    private static async Task TestRepositoryTabsAsync(string workspace)
    {
        string firstPath = CreateRepository(workspace, "first-repository", "first.txt");
        string secondPath = CreateRepository(workspace, "second-repository", "second.txt");
        CreateTrackingHistory(secondPath);
        new GitSettingsManager().SaveSettings(new GitSettings
        {
            RepositoryNameMappings = new List<RepositoryNameMapping>
            {
                new RepositoryNameMapping { Path = secondPath, BaseBranch = "main" }
            }
        });
        GitDiffWindow.FocusExisting();
        Check(!Application.Current.Windows.OfType<GitDiffWindow>().Any(), "未表示時のフォーカスで新規作成しない");

        GitDiffWindow.ShowForRepository(firstPath);
        var window = Application.Current.Windows.OfType<GitDiffWindow>().Single();
        var tabs = (TabControl)window.FindName("RepositoryTabs");
        var firstTab = (TabItem)tabs.SelectedItem;
        var firstView = (GitDiffRepositoryView)firstTab.Content;
        await WaitForAsync(() => firstView.ViewModel.Files.Count == 1 && firstView.ViewModel.Lines.Count > 0 && !firstView.ViewModel.IsBusy);
        await WaitForAsync(() => ((ListBox)firstView.FindName("FileList")).IsKeyboardFocusWithin);
        Check(((ListBox)firstView.FindName("FileList")).IsKeyboardFocusWithin, "初回表示後は変更ファイルにフォーカス");
        Check(window.Owner == null && window.ShowInTaskbar && !window.Topmost, "独立ウィンドウとして表示");
        var firstSelection = firstView.ViewModel.SelectedFile;
        Check(firstView.ViewModel.BaseBranchDisplay == "未設定", "ベースブランチ未設定の表示");

        GitDiffWindow.ShowForRepository(secondPath);
        Check(Application.Current.Windows.OfType<GitDiffWindow>().Count() == 1 && tabs.Items.Count == 2, "別リポジトリは同じウィンドウに追加");
        var secondTab = (TabItem)tabs.SelectedItem;
        var secondView = (GitDiffRepositoryView)secondTab.Content;
        await WaitForAsync(() => secondView.ViewModel.Files.Count == 1 && secondView.ViewModel.Lines.Count > 0 && !secondView.ViewModel.IsBusy);
        Check(secondView.ViewModel.SelectedFile.FileName == "second.txt", "選択タブのリポジトリを表示");
        Check(secondView.ViewModel.AheadBy == 2 && secondView.ViewModel.BehindBy == 1 &&
              secondView.ViewModel.TrackingStatusText == "behind 1  ·  ahead 2", "リモートとの差分数をヘッダーへ反映");
        Check(secondView.ViewModel.CurrentBranchName == "feature/diff-header-and-tab-order" &&
              ((Button)secondView.FindName("CopyCurrentBranchButton")).IsEnabled, "現在のブランチ名をコピーするボタン");
        Check(secondView.ViewModel.BaseBranch == "main" && ((TextBlock)secondView.FindName("BaseBranchText")).Text == "ベース: main",
            "設定済みベースブランチをヘッダーに表示");
        await Application.Current.Dispatcher.InvokeAsync(() => { }, System.Windows.Threading.DispatcherPriority.ContextIdle);
        window.Activate();
        TestHeaderNavigation(window, secondView);
        await TestDiffFontSizeAsync(window, secondView);

        var reorderableTabs = (SettingsTabControl)tabs;
        Check(reorderableTabs.MoveSelectedTab(-1) && ReferenceEquals(tabs.Items[0], secondTab) &&
              ReferenceEquals(tabs.SelectedItem, secondTab), "選択タブを左へ移動して選択を保持");
        Check(!reorderableTabs.MoveSelectedTab(-1), "先頭タブを左へ移動しても折り返さない");
        Check(reorderableTabs.MoveSelectedTab(1) && ReferenceEquals(tabs.Items[1], secondTab), "選択タブを右へ移動");
        Check(!reorderableTabs.MoveSelectedTab(1), "末尾タブを右へ移動しても折り返さない");
        DropTab(secondTab, firstTab, after: false);
        Check(ReferenceEquals(tabs.Items[0], secondTab) && ReferenceEquals(tabs.SelectedItem, secondTab), "ドロップでタブを左側へ移動");
        DropTab(secondTab, firstTab, after: true);
        Check(ReferenceEquals(tabs.Items[1], secondTab) && ReferenceEquals(tabs.SelectedItem, secondTab), "ドロップでタブを右側へ移動");

        GitDiffWindow.ShowForRepository(firstPath.ToUpperInvariant() + Path.DirectorySeparatorChar);
        await Application.Current.Dispatcher.InvokeAsync(() => { }, System.Windows.Threading.DispatcherPriority.ContextIdle);
        Check(tabs.Items.Count == 2 && ReferenceEquals(tabs.SelectedItem, firstTab), "既存リポジトリのタブを再利用");
        Check(ReferenceEquals(firstView.ViewModel.SelectedFile, firstSelection), "タブ切り替えで選択状態を保持");
        GitDiffWindow.ShowForRepository(secondPath);

        var operation = firstView.ViewModel.StageFileAsync(firstSelection);
        Check(firstView.ViewModel.IsOperating, "非選択タブでステージ処理を開始");
        ClickClose(firstTab);
        window.Close();
        Check(tabs.Items.Count == 2 && window.IsVisible, "処理中のタブとウィンドウの閉鎖を抑止");
        await operation;
        Check(firstView.ViewModel.HasStagedChanges && !secondView.ViewModel.HasStagedChanges, "タブ間でGit操作の対象を分離");

        window.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(window), 0, Key.Escape)
        {
            RoutedEvent = Keyboard.PreviewKeyDownEvent
        });
        Check(window.IsVisible, "Escで閉じない");
        window.WindowState = WindowState.Minimized;
        GitDiffWindow.FocusExisting();
        Check(window.WindowState == WindowState.Normal && ReferenceEquals(tabs.SelectedItem, secondTab), "フォーカスで復元し選択タブを保持");
        window.UpdateLayout();
        SavePreview(window, "repository-tabs.png");
        window.Width = window.MinWidth;
        window.UpdateLayout();
        SavePreview(window, "repository-tabs-minimum.png");

        using (var repository = new Repository(secondPath)) Commands.Checkout(repository, repository.Head.Tip);
        await secondView.RefreshAsync();
        Check(!secondView.ViewModel.CanCopyCurrentBranch && !((Button)secondView.FindName("CopyCurrentBranchButton")).IsEnabled,
            "detached HEADではブランチ名のコピーを無効化");

        ClickClose(firstTab);
        Check(tabs.Items.Count == 1 && ReferenceEquals(tabs.SelectedItem, secondTab), "非選択タブを閉じる");
        ClickClose(secondTab);
        Check(!Application.Current.Windows.OfType<GitDiffWindow>().Any(), "最後のタブを閉じるとウィンドウも閉じる");
        GitDiffWindow.ShowForRepository(firstPath);
        var reopened = Application.Current.Windows.OfType<GitDiffWindow>().Single();
        Check(!ReferenceEquals(reopened, window), "閉じた後の新規表示");
        reopened.Close();
    }

    private static async Task TestRefreshOnActivationAsync(string workspace)
    {
        string firstPath = CreateRepository(workspace, "activation-first", "first.txt");
        string secondPath = CreateRepository(workspace, "activation-second", "second.txt");
        GitDiffWindow.ShowForRepository(firstPath);
        var window = Application.Current.Windows.OfType<GitDiffWindow>().Single();
        var tabs = (TabControl)window.FindName("RepositoryTabs");
        var firstTab = (TabItem)tabs.SelectedItem;
        var firstView = (GitDiffRepositoryView)firstTab.Content;
        await WaitForAsync(() => !firstView.ViewModel.IsBusy && firstView.ViewModel.Lines.Count > 0);
        GitDiffWindow.ShowForRepository(secondPath);
        var secondTab = (TabItem)tabs.SelectedItem;
        var secondView = (GitDiffRepositoryView)secondTab.Content;
        await WaitForAsync(() => !secondView.ViewModel.IsBusy && secondView.ViewModel.Lines.Count > 0);

        var other = new Window { Width = 200, Height = 100, ShowInTaskbar = false, Title = "フォーカス切り替えテスト" };
        other.Show();
        other.Activate();
        await WaitForAsync(() => !window.IsActive);
        File.WriteAllText(Path.Combine(firstPath, "first.txt"), "updated first\n");
        File.WriteAllText(Path.Combine(secondPath, "second.txt"), "updated second\n");
        File.WriteAllText(Path.Combine(secondPath, "additional.txt"), "new file\n");
        window.Activate();
        await WaitForAsync(() => !secondView.ViewModel.IsBusy && secondView.ViewModel.Files.Count == 2 &&
            secondView.ViewModel.Lines.Any(line => line.Text == "updated second"));
        Check(secondView.ViewModel.SelectedFile?.FileName == "second.txt", "再フォーカスで選択ファイルを保ち差分と一覧を更新");

        int busyRefreshes = 0;
        secondView.ViewModel.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(GitDiffViewModel.IsRefreshing) && secondView.ViewModel.IsRefreshing) busyRefreshes++;
        };
        var operation = secondView.ViewModel.StageFileAsync(secondView.ViewModel.SelectedFile);
        Check(secondView.ViewModel.IsOperating, "Git操作中に自動更新を要求");
        secondView.RequestRefreshOnActivation();
        await operation;
        await WaitForAsync(() => busyRefreshes >= 2 && !secondView.ViewModel.IsBusy);
        Check(busyRefreshes >= 2, "Git操作後に保留した自動更新を実行");

        tabs.SelectedItem = firstTab;
        await WaitForAsync(() => !firstView.ViewModel.IsBusy && firstView.ViewModel.Lines.Any(line => line.Text == "updated first"));
        Check(firstView.ViewModel.SelectedFile?.FileName == "first.txt", "非表示だったタブも選択時に更新");

        using (var repository = new Repository(firstPath)) Commands.Stage(repository, "first.txt");
        await firstView.RefreshAsync();
        await WaitForAsync(() => firstView.ViewModel.HasStagedChanges && !firstView.ViewModel.IsBusy);
        int refreshes = 0, activations = 0;
        firstView.ViewModel.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(GitDiffViewModel.IsRefreshing) && firstView.ViewModel.IsRefreshing) refreshes++;
        };
        window.Activated += (_, _) => activations++;
        var commit = (Button)firstView.FindName("CommitStagedButton");
        _ = Application.Current.Dispatcher.BeginInvoke(() =>
        {
            var dialog = Application.Current.Windows.OfType<CommitMessageDialog>().Single();
            dialog.Close();
        }, System.Windows.Threading.DispatcherPriority.Background);
        commit.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        await Application.Current.Dispatcher.InvokeAsync(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
        Check(activations > 0 && refreshes == 0 && firstView.ViewModel.SelectedFile != null,
            "確認ダイアログを閉じた際は自動更新しない");

        File.WriteAllText(Path.Combine(firstPath, "first.txt"), "discard this change\n");
        await firstView.RefreshAsync();
        firstView.ViewModel.SelectedFile = firstView.ViewModel.Files.Single(file => !file.IsStaged);
        await WaitForAsync(() => firstView.ViewModel.CanDiscardFile);
        refreshes = 0;
        activations = 0;
        var confirmationTimer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(30) };
        confirmationTimer.Tick += (_, _) =>
        {
            var owner = new System.Windows.Interop.WindowInteropHelper(window).Handle;
            var dialog = IntPtr.Zero;
            while ((dialog = FindWindowEx(IntPtr.Zero, dialog, null, "変更の破棄")) != IntPtr.Zero)
            {
                if (GetWindow(dialog, 4) != owner) continue;
                confirmationTimer.Stop();
                PostMessage(dialog, 0x0111, new IntPtr(6), IntPtr.Zero);
                break;
            }
        };
        var discard = FindVisuals<Button>(firstView).Single(button => button.ToolTip?.ToString() == "このファイルの未ステージ変更を元に戻す");
        confirmationTimer.Start();
        discard.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        await WaitForAsync(() => !firstView.ViewModel.IsBusy && firstView.ViewModel.OperationStatus.Contains("未ステージ変更を破棄しました"));
        Check(activations > 0 && refreshes == 1 && File.ReadAllText(Path.Combine(firstPath, "first.txt")).Replace("\r\n", "\n") == "updated first\n",
            "破棄の確認後も対象を維持し正しく実行");
        File.WriteAllText(Path.Combine(firstPath, "second.txt"), "untracked\n");
        await firstView.RefreshAsync();
        firstView.ViewModel.SelectedFile = firstView.ViewModel.Files.Single(file => file.IsStaged);
        var fileList = (ListBox)firstView.FindName("FileList");
        fileList.UpdateLayout();
        var otherFile = firstView.ViewModel.Files.Single(file => file.Path == "second.txt");
        var otherItem = (ListBoxItem)fileList.ItemContainerGenerator.ContainerFromItem(otherFile);
        var rowDiscard = FindVisuals<Button>(otherItem).Single(button => button.Name == "FileDiscardButton");
        Check(rowDiscard.IsEnabled, "未ステージ行の破棄ボタンを表示");
        confirmationTimer.Start();
        rowDiscard.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        await WaitForAsync(() => !firstView.ViewModel.IsBusy && !File.Exists(Path.Combine(firstPath, "second.txt")));
        Check(firstView.ViewModel.SelectedFile?.IsStaged == true && File.Exists(Path.Combine(firstPath, "first.txt")),
            "未選択行の破棄は指定ファイルだけに適用");
        other.Close();
        window.Close();
    }

    private static async Task TestIntralineViewAsync(string workspace)
    {
        string path = CreateRepository(workspace, "intraline-view", "sample.cs");
        File.WriteAllText(Path.Combine(path, "sample.cs"), "class Example {\n    string text = \"old value\";\n}\n");
        using (var repository = new Repository(path))
        {
            Commands.Stage(repository, "sample.cs");
            var signature = new Signature("Test", "test@example.invalid", DateTimeOffset.UtcNow);
            repository.Commit("initial", signature, signature);
        }
        File.WriteAllText(Path.Combine(path, "sample.cs"), "class Example {\n    string text = \"new value\";\n}\n");
        GitDiffWindow.ShowForRepository(path);
        var window = Application.Current.Windows.OfType<GitDiffWindow>().Single();
        var tabs = (TabControl)window.FindName("RepositoryTabs");
        var view = (GitDiffRepositoryView)((TabItem)tabs.SelectedItem).Content;
        var diff = (GitDiffTextView)view.FindName("DiffLines");
        var background = FindVisuals<FrameworkElement>(diff).Single(element => element.GetType().Name == "DiffBackground");
        var changes = background.GetType().GetProperty("Changes");
        await WaitForAsync(() => changes.GetValue(background) is IReadOnlyList<GitDiffChangeSpan>[] spans && spans.Count(line => line.Count > 0) == 2);
        await Application.Current.Dispatcher.InvokeAsync(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
        window.UpdateLayout();
        SavePreview(window, "intraline-highlight.png");
        Check(FindVisuals<Button>(diff).Any(button => button.Name == "OpenDiffLineButton" && button.Tag is 2),
            "差分の行番号の左に行移動ボタンを表示");
        window.Close();
    }

    private static async Task TestFileNavigationAsync(string workspace)
    {
        string path = CreateRepository(workspace, "file-navigation-links", "sample.cs");
        File.WriteAllLines(Path.Combine(path, "sample.cs"), Enumerable.Range(1, 100).Select(line => $"line {line}"));
        var opened = new List<(string Repository, string File, int? Line)>();
        using var view = new GitDiffRepositoryView(path, 1200, (repository, file, line) =>
        {
            opened.Add((repository, file, line));
            return Task.CompletedTask;
        });
        var window = new Window { Content = view, Width = 1000, Height = 600 };
        window.Show();
        await WaitForAsync(() => view.ViewModel.Lines.Count > 0 && !view.ViewModel.IsBusy);
        await Application.Current.Dispatcher.InvokeAsync(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
        window.UpdateLayout();
        var fileButton = FindVisuals<Button>(view).Single(button => button.Name == "FileOpenButton");
        fileButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Check(opened.Count == 0, "ファイル名のシングルクリックでは VS Code を開かない");
        fileButton.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Left) { RoutedEvent = Control.MouseDoubleClickEvent });
        Check(opened.Count == 1 && opened[0] == (path, "sample.cs", (int?)null), "ファイル名のダブルクリックで行指定なしに開く");
        var header = (Button)view.FindName("SelectedFilePathBox");
        header.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Check(opened.Count == 1, "見出しのファイル名もシングルクリックでは開かない");
        header.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Left) { RoutedEvent = Control.MouseDoubleClickEvent });
        Check(opened.Count == 2 && opened[1].Line == null, "見出しもダブルクリックで行指定なしに開く");
        var diff = (GitDiffTextView)view.FindName("DiffLines");
        var lineButton = FindVisuals<Button>(diff).Single(button => button.Name == "OpenDiffLineButton" && button.Tag is 2);
        lineButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Check(opened.Count == 3 && opened[2] == (path, "sample.cs", (int?)2), "行番号の左のボタンで該当行に移動");
        var text = FindVisuals<TextBox>(diff).Single();
        text.ScrollToEnd();
        await Application.Current.Dispatcher.InvokeAsync(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
        await WaitForAsync(() => FindVisuals<Button>(diff).Any(button => button.Name == "OpenDiffLineButton" && button.Tag is 100));
        lineButton = FindVisuals<Button>(diff).Single(button => button.Name == "OpenDiffLineButton" && button.Tag is 100);
        lineButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Check(opened.Count == 4 && opened[3].Line == 100, "スクロール後も表示行と移動先の行が一致");
        window.Close();
    }

    private static async Task TestSyntaxViewAsync(string workspace)
    {
        string path = CreateRepository(workspace, "syntax-view", "sample.cs");
        File.WriteAllText(Path.Combine(path, "sample.cs"), "public class Example\n{\n\tstring text = \"日本語\"; // comment\n\tint number = 42;\n\tstring longText = \"" + new string('x', 1000) + "\";\n}\n");
        GitDiffWindow.ShowForRepository(path);
        var window = Application.Current.Windows.OfType<GitDiffWindow>().Single();
        var tabs = (TabControl)window.FindName("RepositoryTabs");
        var view = (GitDiffRepositoryView)((TabItem)tabs.SelectedItem).Content;
        var diff = (GitDiffTextView)view.FindName("DiffLines");
        await WaitForAsync(() => view.ViewModel.Lines.Count > 1 && !view.ViewModel.IsBusy);
        var background = FindVisuals<FrameworkElement>(diff).Single(element => element.GetType().Name == "DiffBackground");
        var syntaxProperty = background.GetType().GetProperty("Syntax");
        await WaitForAsync(() => syntaxProperty.GetValue(background) is IReadOnlyList<GitDiffSyntaxSpan>[] spans &&
            spans.Any(line => line.Any(span => span.Color.Equals("#569CD6", StringComparison.OrdinalIgnoreCase))));
        var text = FindVisuals<TextBox>(diff).Single();
        window.Activate();
        text.Focus();
        text.Select(text.Text.IndexOf("string", StringComparison.Ordinal), 6);
        await Application.Current.Dispatcher.InvokeAsync(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
        window.UpdateLayout();
        int stringIndex = text.Text.IndexOf("string", StringComparison.Ordinal);
        int rowIndex = text.GetLineIndexFromCharacterIndex(stringIndex);
        int rowStart = text.GetCharacterIndexFromLineIndex(rowIndex);
        var probe = new FormattedText("\tstring", System.Globalization.CultureInfo.CurrentUICulture, FlowDirection.LeftToRight,
            new Typeface(text.FontFamily, text.FontStyle, text.FontWeight, text.FontStretch), text.FontSize, Brushes.White,
            VisualTreeHelper.GetDpi(text).PixelsPerDip);
        Check(Math.Abs(text.GetRectFromCharacterIndex(stringIndex).Left - text.GetRectFromCharacterIndex(rowStart).Left -
            probe.BuildHighlightGeometry(new Point(0, 0), 1, 1).Bounds.Left) < 1, "タブ文字の描画と選択位置が一致");
        SavePreview(window, "syntax-highlight.png");
        Check(diff.GetSelectedLineIndices().Count == 1 && text.SelectedText == "string", "色分け後もテキスト選択と行選択を維持");
        var originalLines = view.ViewModel.Lines;
        int originalStart = text.SelectionStart;
        view.RequestRefreshOnActivation();
        await WaitForAsync(() => !view.ViewModel.IsBusy);
        Check(ReferenceEquals(originalLines, view.ViewModel.Lines) && text.IsKeyboardFocused &&
            text.SelectionStart == originalStart && text.SelectionLength == 6, "Unchanged refresh preserves display, focus and selection");
        File.AppendAllText(Path.Combine(path, "sample.cs"), "// appended\n");
        view.RequestRefreshOnActivation();
        await WaitForAsync(() => !view.ViewModel.IsBusy);
        Check(!ReferenceEquals(originalLines, view.ViewModel.Lines) && text.IsKeyboardFocused &&
            text.SelectionStart == originalStart && text.SelectionLength == 6, "Changed refresh preserves focus and selection");
        window.UpdateLayout();
        text.ScrollToHorizontalOffset(200);
        await Application.Current.Dispatcher.InvokeAsync(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
        window.UpdateLayout();
        Check(text.HorizontalOffset > 0 && !text.GetRectFromCharacterIndex(rowStart).IsEmpty, "横スクロール中も色付きの行を描画できる");
        var wheelHandler = typeof(GitDiffTextView).GetMethod("OnWindowMessage", BindingFlags.NonPublic | BindingFlags.Instance);
        var position = diff.PointToScreen(new Point(diff.ActualWidth / 2, diff.ActualHeight / 2));
        var coordinates = new IntPtr(((long)(ushort)(short)position.Y << 16) | (ushort)(short)position.X);
        double offset = text.HorizontalOffset;
        object[] wheelArgs = { IntPtr.Zero, 0x020E, new IntPtr(120 << 16), coordinates, false };
        wheelHandler.Invoke(diff, wheelArgs);
        await Application.Current.Dispatcher.InvokeAsync(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
        Check((bool)wheelArgs[4] && text.HorizontalOffset < offset, "横ホイールの正の入力で逆方向へスクロール");
        offset = text.HorizontalOffset;
        wheelArgs = new object[] { IntPtr.Zero, 0x020E, new IntPtr(-120 << 16), coordinates, false };
        wheelHandler.Invoke(diff, wheelArgs);
        await Application.Current.Dispatcher.InvokeAsync(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
        Check((bool)wheelArgs[4] && text.HorizontalOffset > offset, "横ホイールの負の入力でも逆方向へスクロール");
        SavePreview(window, "syntax-horizontal.png");
        window.Close();
    }

    private static async Task TestDiffFontSizeAsync(Window window, GitDiffRepositoryView view)
    {
        var diff = (GitDiffTextView)view.FindName("DiffLines");
        var text = FindVisuals<TextBox>(diff).Single();
        var decrease = (Button)view.FindName("DecreaseDiffFontButton");
        var increase = (Button)view.FindName("IncreaseDiffFontButton");
        var size = (TextBlock)view.FindName("DiffFontSizeText");
        text.Select(0, 3);
        window.UpdateLayout();
        double initialHeight = text.GetRectFromCharacterIndex(0).Height;
        increase.Focus();
        for (int i = 0; i < 6; i++) increase.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        await Application.Current.Dispatcher.InvokeAsync(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
        window.UpdateLayout();
        Check(text.FontSize == 18 && size.Text == "18" && text.SelectionLength == 3 &&
            text.GetRectFromCharacterIndex(0).Height > initialHeight, "文字サイズの拡大と選択範囲の維持");
        SavePreview(window, "diff-font-size.png");
        PressArrow(window, increase, Key.Left);
        Check(decrease.IsKeyboardFocused, "左キーで文字縮小ボタンへ移動");
        PressArrow(window, decrease, Key.Right);
        Check(increase.IsKeyboardFocused, "右キーで文字拡大ボタンへ移動");
        for (int i = 0; i < 40; i++) increase.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Check(text.FontSize == 32 && size.Text == "32" && text.Margin.Left > 120, "文字サイズの上限と行番号欄の拡張");
        for (int i = 0; i < 40; i++) decrease.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Check(text.FontSize == 8 && size.Text == "8", "文字サイズの下限");
        for (int i = 0; i < 4; i++) increase.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Check(text.FontSize == 12 && size.Text == "12", "文字サイズを元に戻す");
    }

    private static async Task TestFileActionNavigationAsync(string workspace)
    {
        string path = CreateRepository(workspace, "file-navigation", "tracked.txt");
        using (var repository = new Repository(path))
        {
            Commands.Stage(repository, "tracked.txt");
            var signature = new Signature("Test", "test@example.invalid", DateTimeOffset.UtcNow);
            repository.Commit("initial", signature, signature);
            File.WriteAllText(Path.Combine(path, "tracked.txt"), "staged\n");
            Commands.Stage(repository, "tracked.txt");
        }
        File.WriteAllText(Path.Combine(path, "unstaged.txt"), "new\n");
        GitDiffWindow.ShowForRepository(path);
        var window = Application.Current.Windows.OfType<GitDiffWindow>().Single();
        var tabs = (TabControl)window.FindName("RepositoryTabs");
        var view = (GitDiffRepositoryView)((TabItem)tabs.SelectedItem).Content;
        var list = (ListBox)view.FindName("FileList");
        await WaitForAsync(() => view.ViewModel.Files.Count == 2 && !view.ViewModel.IsBusy && list.IsKeyboardFocusWithin);
        window.UpdateLayout();
        var item = (ListBoxItem)list.ItemContainerGenerator.ContainerFromItem(list.SelectedItem);
        var rowButtons = FindVisuals<Button>(item).Where(button => button.Name is "FileCopyButton" or "FileStageButton").ToArray();
        Check(rowButtons.Length == 2, "ファイル行の操作ボタンを取得");
        PressArrow(window, item, Key.Right);
        Check(rowButtons[0].IsKeyboardFocused, "ファイルから右キーでコピーへ移動");
        PressArrow(window, rowButtons[0], Key.Right);
        Check(rowButtons[1].IsKeyboardFocused, "コピーから右キーでステージへ移動");
        var discardButton = FindVisuals<Button>(item).Single(button => button.Name == "FileDiscardButton");
        PressArrow(window, rowButtons[1], Key.Right);
        Check(discardButton.IsKeyboardFocused, "ステージの右から破棄へ移動");
        PressArrow(window, discardButton, Key.Left);
        PressArrow(window, rowButtons[1], Key.Left);
        Check(rowButtons[0].IsKeyboardFocused, "ステージから左キーでコピーへ移動");
        PressArrow(window, rowButtons[0], Key.Left);
        Check(item.IsKeyboardFocused, "コピーから左キーでファイルへ戻る");
        PressArrow(window, item, Key.Right);
        PressArrow(window, rowButtons[0], Key.Down);
        var stageAll = FindSectionActionButton(view, isStaged: false);
        var unstageAll = FindSectionActionButton(view, isStaged: true);
        Check(unstageAll.IsKeyboardFocused, "未ステージの末尾からステージ済みの一括解除へ移動");
        PressArrow(window, unstageAll, Key.Down);
        var lastItem = (ListBoxItem)list.ItemContainerGenerator.ContainerFromItem(list.SelectedItem);
        var lastButtons = FindVisuals<Button>(lastItem).Where(button => button.Name is "FileCopyButton" or "FileStageButton").ToArray();
        Check(lastItem.IsKeyboardFocused && !ReferenceEquals(lastItem, item), "一括解除から下キーでステージ済みファイルへ移動");
        PressArrow(window, lastItem, Key.Right);
        var commit = (Button)view.FindName("CommitStagedButton");
        PressArrow(window, lastButtons[0], Key.Down);
        Check(commit.IsKeyboardFocused, "最後のファイルからコミットへ移動");
        PressArrow(window, commit, Key.Up);
        Check(lastItem.IsKeyboardFocused, "コミットから最後のファイルへ戻る");
        PressArrow(window, lastItem, Key.Up);
        Check(unstageAll.IsKeyboardFocused, "ステージ済みの先頭から一括解除へ移動");
        PressArrow(window, unstageAll, Key.Up);
        Check(item.IsKeyboardFocused, "一括解除から前の未ステージファイルへ戻る");
        PressArrow(window, item, Key.Up);
        Check(stageAll.IsKeyboardFocused, "未ステージの先頭からすべてステージへ移動");
        PressArrow(window, stageAll, Key.Down);
        Check(item.IsKeyboardFocused, "すべてステージから下キーで未ステージファイルへ移動");
        stageAll.Focus();
        int inputEvents = 0;
        window.AddHandler(Keyboard.PreviewKeyDownEvent, new KeyEventHandler((_, _) => inputEvents++), true);
        window.AddHandler(Mouse.PreviewMouseDownEvent, new MouseButtonEventHandler((_, _) => inputEvents++), true);
        int inputBeforeOperation = inputEvents;
        stageAll.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        await WaitForAsync(() => !view.ViewModel.IsBusy && !view.ViewModel.HasUnstagedChanges);
        await Application.Current.Dispatcher.InvokeAsync(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
        if (inputEvents == inputBeforeOperation) Check(commit.IsKeyboardFocused, "すべてステージ後はコミットにフォーカス");
        else Console.WriteLine("スキップ: 処理中に入力があったため完了後のフォーカスを維持");
        commit.Focus();
        PressArrow(window, commit, Key.Up);
        Check(list.IsKeyboardFocusWithin, "コミットから上キーでファイルへ移動");
        var selectedItem = (ListBoxItem)list.ItemContainerGenerator.ContainerFromItem(list.SelectedItem);
        var unstage = FindVisuals<Button>(selectedItem).Single(button => button.Name == "FileStageButton");
        unstage.Focus();
        inputBeforeOperation = inputEvents;
        unstage.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        await WaitForAsync(() => !view.ViewModel.IsBusy && view.ViewModel.HasUnstagedChanges);
        await Application.Current.Dispatcher.InvokeAsync(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
        if (inputEvents == inputBeforeOperation) Check(list.IsKeyboardFocusWithin && Keyboard.FocusedElement is ListBoxItem, "ファイルのステージ解除後も一覧で操作を継続");
        else Console.WriteLine("スキップ: 処理中に入力があったため完了後のフォーカスを維持");
        unstageAll = FindSectionActionButton(view, isStaged: true);
        unstageAll.Focus();
        unstageAll.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        await WaitForAsync(() => !view.ViewModel.IsBusy && !view.ViewModel.HasStagedChanges);
        Check(view.ViewModel.Files.Count == 2 && File.ReadAllText(Path.Combine(path, "tracked.txt")) == "staged\n" &&
            File.ReadAllText(Path.Combine(path, "unstaged.txt")) == "new\n", "見出しのすべて解除で作業ファイルを維持して未ステージへ移動");
        window.Close();
    }

    private static async Task TestPlacementAndInitialFocusAsync(string workspace)
    {
        string path = CreateRepository(workspace, "clean-repository", "clean.txt");
        using (var repository = new Repository(path))
        {
            Commands.Stage(repository, "clean.txt");
            var signature = new Signature("Test", "test@example.invalid", DateTimeOffset.UtcNow);
            repository.Commit("clean", signature, signature);
        }
        GitDiffWindow.ShowForRepository(path);
        var window = Application.Current.Windows.OfType<GitDiffWindow>().Single();
        var tabs = (TabControl)window.FindName("RepositoryTabs");
        var view = (GitDiffRepositoryView)((TabItem)tabs.SelectedItem).Content;
        await WaitForAsync(() => !view.ViewModel.IsBusy && view.ViewModel.Message == "未コミットの変更はありません。");
        await WaitForAsync(() => ((Button)view.FindName("RefreshButton")).IsKeyboardFocused);
        Check(((Button)view.FindName("RefreshButton")).IsKeyboardFocused, "差分がない初回表示後は更新ボタンにフォーカス");
        window.WindowState = WindowState.Normal;
        window.Left = SystemParameters.VirtualScreenLeft + 100;
        window.Top = SystemParameters.VirtualScreenTop + 100;
        window.Width = 900;
        window.Height = 520;
        await Application.Current.Dispatcher.InvokeAsync(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
        double left = window.Left, top = window.Top, width = window.ActualWidth, height = window.ActualHeight;
        window.Close();

        GitDiffWindow.ShowForRepository(path);
        var reopened = Application.Current.Windows.OfType<GitDiffWindow>().Single();
        Check(Math.Abs(reopened.Left - left) < 1 && Math.Abs(reopened.Top - top) < 1 &&
            Math.Abs(reopened.Width - width) < 1 && Math.Abs(reopened.Height - height) < 1, "閉じた位置とサイズを復元");
        reopened.WindowState = WindowState.Maximized;
        await Application.Current.Dispatcher.InvokeAsync(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
        reopened.WindowState = WindowState.Minimized;
        reopened.Close();
        GitDiffWindow.ShowForRepository(path);
        var maximized = Application.Current.Windows.OfType<GitDiffWindow>().Single();
        Check(maximized.WindowState == WindowState.Maximized, "最大化後に最小化して閉じても最大化状態を復元");
        maximized.Close();

        string placementPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "git_diff_window_placement.json");
        File.WriteAllText(placementPath, "{\"Left\":-100000,\"Top\":-100000,\"Width\":900,\"Height\":520,\"Maximized\":false}");
        GitDiffWindow.ShowForRepository(path);
        var offscreen = Application.Current.Windows.OfType<GitDiffWindow>().Single();
        Check(offscreen.WindowStartupLocation == WindowStartupLocation.CenterScreen && offscreen.Width == 900 && offscreen.Height == 520,
            "画面外の保存位置は使わずサイズを復元");
        offscreen.Close();
    }

    private static void TestHeaderNavigation(Window window, GitDiffRepositoryView view)
    {
        var push = (Button)view.FindName("PushButton");
        var pull = (Button)view.FindName("PullButton");
        var rename = (Button)view.FindName("RenameBranchButton");
        var copy = (Button)view.FindName("CopyCurrentBranchButton");
        var branch = (TextBlock)view.FindName("CurrentBranchText");
        var branchHeader = (Grid)view.FindName("BranchHeader");
        branch.Width = 20;
        window.UpdateLayout();
        double pullX = pull.TranslatePoint(new Point(pull.ActualWidth / 2, 0), view).X;
        double renameX = rename.TranslatePoint(new Point(rename.ActualWidth / 2, 0), view).X;
        branchHeader.Margin = new Thickness(Math.Max(0, pullX - renameX), 4, 0, 0);
        window.UpdateLayout();
        rename.Focus();
        PressArrow(window, rename, Key.Up);
        Check(pull.IsKeyboardFocused, "リネームの上にあるプルへ移動");
        PressArrow(window, pull, Key.Left);
        Check(push.IsKeyboardFocused, "左キーで隣のプッシュへ移動");
        rename.Focus();
        PressArrow(window, rename, Key.Right);
        Check(copy.IsKeyboardFocused, "右キーで隣のコピーボタンへ移動");
        PressArrow(window, copy, Key.Down);
        var files = (ListBox)view.FindName("FileList");
        Check(files.IsKeyboardFocusWithin, "下段ヘッダーボタンから下キーでファイル一覧へ移動");
        var item = (ListBoxItem)files.ItemContainerGenerator.ContainerFromItem(files.SelectedItem);
        var row = (Border)item.Template.FindName("Row", item);
        Check(((SolidColorBrush)row.BorderBrush).Color == Color.FromRgb(0, 122, 204), "ファイルにフォーカス中は枠を強調");
        var stageAll = FindSectionActionButton(view, isStaged: false);
        stageAll.Focus();
        Check(item.IsSelected && ((SolidColorBrush)row.BorderBrush).Color == Colors.Transparent,
            "ボタンへフォーカスするとファイルの選択を保ちつつ枠の強調を解除");
        var rowButton = FindVisuals<Button>(item).First();
        rowButton.Focus();
        Check(item.IsSelected && ((SolidColorBrush)row.BorderBrush).Color == Colors.Transparent,
            "ファイル行内のボタンでも枠の強調を解除");
        item.Focus();
        PressArrow(window, (UIElement)Keyboard.FocusedElement, Key.Up);
        Check(stageAll.IsKeyboardFocused, "先頭ファイルから上キーで見出しのすべてステージへ移動");
        PressArrow(window, stageAll, Key.Up);
        Check(copy.IsKeyboardFocused, "先頭ファイルから上キーでヘッダーへ移動");
        window.UpdateLayout();
        Check(ReferenceEquals(branch.Parent, copy.Parent), "ブランチ名の行にリネームとコピーボタンを配置");
        branch.ClearValue(FrameworkElement.WidthProperty);
        branchHeader.Margin = new Thickness(0, 4, 0, 0);
    }

    private static Button FindSectionActionButton(GitDiffRepositoryView view, bool isStaged) =>
        FindVisuals<Button>(view).Single(button => button.Name == "SectionActionButton" &&
            button.DataContext is CollectionViewGroup group && group.Items.OfType<GitDiffFile>().FirstOrDefault()?.IsStaged == isStaged);

    private static void PressArrow(Window window, UIElement element, Key key)
    {
        element.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(window), 0, key)
        {
            RoutedEvent = Keyboard.PreviewKeyDownEvent
        });
    }

    private static async Task TestBranchOperationsAsync(string workspace)
    {
        string remotePath = Path.Combine(workspace, "local-remote.git");
        Repository.Init(remotePath, isBare: true);
        string localPath = CreateRepository(workspace, "branch-operations", "untracked.txt");
        var signature = new Signature("Test", "test@example.invalid", DateTimeOffset.UtcNow);
        string originalName;
        using (var repository = new Repository(localPath))
        {
            repository.Commit("initial", signature, signature, new CommitOptions { AllowEmptyCommit = true });
            repository.Network.Remotes.Add("origin", remotePath);
            originalName = repository.Head.FriendlyName;
        }
        using var viewModel = new GitDiffViewModel(localPath);
        await viewModel.RefreshAsync();
        await viewModel.PushCurrentBranchAsync();
        using (var repository = new Repository(localPath))
            Check(repository.Head.TrackedBranch?.FriendlyName == "origin/" + originalName, "初回プッシュで追跡先を設定");

        string peerPath = Path.Combine(workspace, "branch-peer");
        Repository.Clone(remotePath, peerPath, new CloneOptions { BranchName = originalName });
        string remoteCommit;
        using (var peer = new Repository(peerPath))
            remoteCommit = peer.Commit("remote update", signature, signature, new CommitOptions { AllowEmptyCommit = true }).Sha;
        await new GitBranchService(peerPath).PushCurrentBranchAsync();
        var pull = viewModel.PullCurrentBranchAsync();
        Check(viewModel.IsOperating && !viewModel.CanOperateCurrentBranch, "プル中は他のブランチ操作を無効化");
        await pull;
        using (var repository = new Repository(localPath)) Check(repository.Head.Tip.Sha == remoteCommit, "プルでローカルリモートの更新を取得");
        Check(viewModel.AheadBy == 0 && viewModel.BehindBy == 0, "プル後に追跡差分を更新");

        string renamed = "feature/renamed-from-diff";
        await viewModel.RenameCurrentBranchAsync(originalName, renamed);
        Check(viewModel.CurrentBranchName == renamed && viewModel.OperationStatus == "ブランチ名を変更しました。", "リネーム後に現在のブランチ表示を更新");
        await viewModel.RenameCurrentBranchAsync(renamed, "invalid name");
        Check(viewModel.CurrentBranchName == renamed && viewModel.OperationStatus.StartsWith("操作に失敗しました。") && !viewModel.IsBusy,
            "不正なリネームの失敗から復帰");

        string localCommit;
        using (var repository = new Repository(localPath))
            localCommit = repository.Commit("local update", signature, signature, new CommitOptions { AllowEmptyCommit = true }).Sha;
        await viewModel.PushCurrentBranchAsync();
        using (var remote = new Repository(remotePath))
            Check(remote.Branches[originalName].Tip.Sha == localCommit, "リネーム後も設定済みの追跡先へプッシュ");
    }

    private static void TestBranchInfo(string workspace)
    {
        string path = CreateRepository(workspace, "tracking-info", "untracked.txt");
        var service = new GitDiffService(path);
        var unborn = service.ReadBranchInfo(CancellationToken.None);
        Check(!string.IsNullOrEmpty(unborn.BranchName) && unborn.AheadBy == null && unborn.BehindBy == null,
            "初回コミット前は追跡先なしとして扱う");
        CreateTrackingHistory(path);
        var diverged = service.ReadBranchInfo(CancellationToken.None);
        Check(diverged.TrackedBranch == "origin/main" && diverged.AheadBy == 2 && diverged.BehindBy == 1,
            "分岐した履歴のaheadとbehindを正しく集計");
        using (var repository = new Repository(path)) Commands.Checkout(repository, repository.Head.Tip);
        var detached = service.ReadBranchInfo(CancellationToken.None);
        Check(detached.CurrentBranch.StartsWith("detached HEAD") && detached.BranchName == string.Empty &&
              detached.AheadBy == null && detached.BehindBy == null, "detached HEADで誤ったブランチ名や0件を表示しない");
    }

    private static void CreateTrackingHistory(string path)
    {
        using var repository = new Repository(path);
        var signature = new Signature("Test", "test@example.invalid", DateTimeOffset.UtcNow);
        var options = new CommitOptions { AllowEmptyCommit = true };
        var common = repository.Commit("common", signature, signature, options);
        string branchName = "feature/diff-header-and-tab-order";
        repository.CreateBranch(branchName, common);
        Commands.Checkout(repository, branchName);
        repository.Commit("local one", signature, signature, options);
        repository.Commit("local two", signature, signature, options);
        repository.CreateBranch("remote-side", common);
        Commands.Checkout(repository, "remote-side");
        var remoteTip = repository.Commit("remote one", signature, signature, options);
        repository.Network.Remotes.Add("origin", Path.Combine(path, "unused-remote.git"));
        repository.Refs.Add("refs/remotes/origin/main", remoteTip.Id);
        Commands.Checkout(repository, branchName);
        repository.Branches.Update(repository.Head, branch => branch.Remote = "origin", branch => branch.UpstreamBranch = "refs/heads/main");
    }

    private static void DropTab(TabItem source, TabItem target, bool after)
    {
        target.UpdateLayout();
        var header = FindVisuals<Border>(target).First(element => element.AllowDrop && ReferenceEquals(element.Tag, target));
        var data = new DataObject("HotKeyCommandApp.GitDiffRepositoryTab", source);
        var args = (DragEventArgs)Activator.CreateInstance(typeof(DragEventArgs), BindingFlags.Instance | BindingFlags.NonPublic,
            null, new object[] { data, DragDropKeyStates.LeftMouseButton, DragDropEffects.Move, header,
                new Point(header.ActualWidth * (after ? 0.9 : 0.1), header.ActualHeight / 2) }, null);
        args.RoutedEvent = DragDrop.DropEvent;
        header.RaiseEvent(args);
        Check(args.Handled && args.Effects == DragDropEffects.Move, "タブヘッダーでドロップを受け付ける");
    }

    private static IEnumerable<T> FindVisuals<T>(DependencyObject element) where T : DependencyObject
    {
        if (element is T match) yield return match;
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(element); i++)
            foreach (var child in FindVisuals<T>(VisualTreeHelper.GetChild(element, i))) yield return child;
    }

    private static string CreateRepository(string workspace, string name, string fileName)
    {
        string path = Path.Combine(workspace, name);
        Repository.Init(path);
        File.WriteAllText(Path.Combine(path, fileName), "sample diff\n");
        return path;
    }

    private static void ClickClose(TabItem tab)
    {
        tab.UpdateLayout();
        var button = FindButtons(tab).Single(candidate => candidate.ToolTip?.ToString() == "タブを閉じる (Ctrl+W)");
        button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    }

    private static IEnumerable<Button> FindButtons(DependencyObject element)
    {
        if (element is Button button) yield return button;
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(element); i++)
            foreach (var child in FindButtons(VisualTreeHelper.GetChild(element, i))) yield return child;
    }

    private static void SavePreview(Window window, string fileName)
    {
        var bitmap = new RenderTargetBitmap((int)window.ActualWidth, (int)window.ActualHeight, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(window);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var output = File.Create(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, fileName));
        encoder.Save(output);
    }

    private static async Task WaitForAsync(Func<bool> condition)
    {
        var elapsed = Stopwatch.StartNew();
        while (!condition())
        {
            if (elapsed.Elapsed > TimeSpan.FromSeconds(15)) throw new TimeoutException("差分の読み込みが完了しませんでした。");
            await Task.Delay(25);
        }
    }

    private static void Check(bool condition, string description)
    {
        if (!condition) throw new InvalidOperationException(description);
        Console.WriteLine("OK: " + description);
    }
}
