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
                TestBranchInfo(workspace);
                await TestRepositoryTabsAsync(workspace);
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
        GitDiffWindow.FocusExisting();
        Check(!Application.Current.Windows.OfType<GitDiffWindow>().Any(), "未表示時のフォーカスで新規作成しない");

        GitDiffWindow.ShowForRepository(firstPath);
        var window = Application.Current.Windows.OfType<GitDiffWindow>().Single();
        var tabs = (TabControl)window.FindName("RepositoryTabs");
        var firstTab = (TabItem)tabs.SelectedItem;
        var firstView = (GitDiffRepositoryView)firstTab.Content;
        await WaitForAsync(() => firstView.ViewModel.Files.Count == 1 && firstView.ViewModel.Lines.Count > 0 && !firstView.ViewModel.IsBusy);
        Check(window.Owner == null && window.ShowInTaskbar && !window.Topmost, "独立ウィンドウとして表示");
        var firstSelection = firstView.ViewModel.SelectedFile;

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
