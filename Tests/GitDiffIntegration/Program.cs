using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
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
    [STAThread]
    private static int Main()
    {
        string workspace = Path.Combine(Path.GetTempPath(), "HotKeyGitDiffTests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(workspace);
        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        app.Resources.MergedDictionaries.Add(new ResourceDictionary
        {
            Source = new Uri("/HotKeyCommandApp;component/Views/Controls/SettingsSharedResources.xaml", UriKind.Relative)
        });
        app.Startup += async (_, _) =>
        {
            int result = 0;
            try
            {
                TestShortcutSettings();
                TestSyntaxHighlighting();
                TestSyntaxContext(workspace);
                TestBranchInfo(workspace);
                await TestBranchOperationsAsync(workspace);
                await TestRepositoryTabsAsync(workspace);
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

    private static async Task TestSyntaxViewAsync(string workspace)
    {
        string path = CreateRepository(workspace, "syntax-view", "sample.cs");
        File.WriteAllText(Path.Combine(path, "sample.cs"), "public class Example\n{\n\tstring text = \"日本語\"; // comment\n\tint number = 42;\n\tstring longText = \"" + new string('x', 220) + "\";\n}\n");
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
        text.ScrollToHorizontalOffset(200);
        await Application.Current.Dispatcher.InvokeAsync(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
        window.UpdateLayout();
        Check(text.HorizontalOffset > 0 && !text.GetRectFromCharacterIndex(rowStart).IsEmpty, "横スクロール中も色付きの行を描画できる");
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
        PressArrow(window, rowButtons[1], Key.Left);
        Check(rowButtons[0].IsKeyboardFocused, "ステージから左キーでコピーへ移動");
        PressArrow(window, rowButtons[0], Key.Left);
        Check(item.IsKeyboardFocused, "コピーから左キーでファイルへ戻る");
        PressArrow(window, item, Key.Right);
        PressArrow(window, rowButtons[0], Key.Down);
        var lastItem = (ListBoxItem)list.ItemContainerGenerator.ContainerFromItem(list.SelectedItem);
        var lastButtons = FindVisuals<Button>(lastItem).Where(button => button.Name is "FileCopyButton" or "FileStageButton").ToArray();
        Check(lastButtons[0].IsKeyboardFocused && !ReferenceEquals(lastItem, item), "行内ボタンから上下キーで隣のファイルの同じボタンへ移動");
        var stageAll = (Button)view.FindName("StageAllButton");
        var commit = (Button)view.FindName("CommitStagedButton");
        PressArrow(window, lastButtons[0], Key.Down);
        Check(stageAll.IsKeyboardFocused, "最後のファイルからすべてステージへ移動");
        PressArrow(window, stageAll, Key.Down);
        Check(commit.IsKeyboardFocused, "すべてステージからコミットへ移動");
        PressArrow(window, commit, Key.Up);
        Check(stageAll.IsKeyboardFocused, "コミットからすべてステージへ戻る");
        PressArrow(window, stageAll, Key.Up);
        Check(lastItem.IsKeyboardFocused, "すべてステージから最後のファイルへ戻る");
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
        Check(list.IsKeyboardFocusWithin, "無効なすべてステージを飛ばしてファイルへ移動");
        var selectedItem = (ListBoxItem)list.ItemContainerGenerator.ContainerFromItem(list.SelectedItem);
        var unstage = FindVisuals<Button>(selectedItem).Single(button => button.Name == "FileStageButton");
        unstage.Focus();
        inputBeforeOperation = inputEvents;
        unstage.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        await WaitForAsync(() => !view.ViewModel.IsBusy && view.ViewModel.HasUnstagedChanges);
        await Application.Current.Dispatcher.InvokeAsync(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
        if (inputEvents == inputBeforeOperation) Check(list.IsKeyboardFocusWithin && Keyboard.FocusedElement is ListBoxItem, "ファイルのステージ解除後も一覧で操作を継続");
        else Console.WriteLine("スキップ: 処理中に入力があったため完了後のフォーカスを維持");
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
        var stageAll = (Button)view.FindName("StageAllButton");
        stageAll.Focus();
        Check(item.IsSelected && ((SolidColorBrush)row.BorderBrush).Color == Colors.Transparent,
            "ボタンへフォーカスするとファイルの選択を保ちつつ枠の強調を解除");
        var rowButton = FindVisuals<Button>(item).First();
        rowButton.Focus();
        Check(item.IsSelected && ((SolidColorBrush)row.BorderBrush).Color == Colors.Transparent,
            "ファイル行内のボタンでも枠の強調を解除");
        item.Focus();
        PressArrow(window, (UIElement)Keyboard.FocusedElement, Key.Up);
        Check(copy.IsKeyboardFocused, "先頭ファイルから上キーでヘッダーへ移動");
        window.UpdateLayout();
        Check(ReferenceEquals(branch.Parent, copy.Parent), "ブランチ名の行にリネームとコピーボタンを配置");
        branch.ClearValue(FrameworkElement.WidthProperty);
        branchHeader.Margin = new Thickness(0, 4, 0, 0);
    }

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
