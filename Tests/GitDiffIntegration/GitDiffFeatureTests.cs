using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using HotKeyCommandApp.Models;
using HotKeyCommandApp.Services;
using HotKeyCommandApp.ViewModels;
using HotKeyCommandApp.Views.Controls;
using LibGit2Sharp;

internal static partial class Program
{
    private static readonly Signature FeatureAuthor = new("Diff Test", "diff@example.invalid", DateTimeOffset.Now);

    private static string InitFeatureRepository(string workspace, string name)
    {
        string path = Path.Combine(workspace, name);
        Repository.Init(path);
        using var repository = new Repository(path);
        repository.Config.Set("user.name", FeatureAuthor.Name);
        repository.Config.Set("user.email", FeatureAuthor.Email);
        repository.Config.Set("core.autocrlf", false);
        return path;
    }

    private static Commit CommitFile(Repository repository, string name, string text, string message)
    {
        File.WriteAllText(Path.Combine(repository.Info.WorkingDirectory, name), text);
        Commands.Stage(repository, name);
        return repository.Commit(message, FeatureAuthor, FeatureAuthor);
    }

    private static async Task TestDiffFeaturesAsync(string workspace)
    {
        string path = InitFeatureRepository(workspace, "diff-features");
        string rootSha, featureSha;
        using (var repository = new Repository(path))
        {
            rootSha = CommitFile(repository, "shared.txt", "initial\n", "initial").Sha;
            CommitFile(repository, "unchanged.txt", "unchanged\n", "unchanged file");
            repository.CreateBranch("base", repository.Head.Tip);
            featureSha = CommitFile(repository, "shared.txt", "committed NEEDLE\n", "feature change").Sha;
            CommitFile(repository, "removed.txt", "deleted NEEDLE\n", "file to remove");
            File.Delete(Path.Combine(path, "removed.txt"));
            Commands.Stage(repository, "removed.txt");
            File.WriteAllText(Path.Combine(path, "shared.txt"), "staged NEEDLE\n");
            Commands.Stage(repository, "shared.txt");
            File.WriteAllText(Path.Combine(path, "shared.txt"), "working needle needle\n");
            Directory.CreateDirectory(Path.Combine(path, "z"));
            File.WriteAllText(Path.Combine(path, "z/a.txt"), "untracked NEEDLE\n");
            File.WriteAllText(Path.Combine(path, "b.txt"), "another NEEDLE\n");
        }
        var service = new GitDiffService(path);
        Check(service.ReadRecentCommits(CancellationToken.None).Count == 4, "直近コミットの取得");
        var rootFiles = service.ReadComparisonFiles(GitDiffMode.Commit, rootSha, CancellationToken.None);
        Check(service.ReadDiff(rootFiles.Single(), CancellationToken.None).Lines.Any(line => line.Kind == GitDiffLineKind.Added && line.Text == "initial"), "初回コミットは空のツリーと比較");
        var committed = service.ReadComparisonFiles(GitDiffMode.Commit, featureSha, CancellationToken.None).Single();
        var patch = service.ReadDiff(committed, CancellationToken.None);
        Check(committed.IsReadOnly && patch.Lines.Any(line => line.Text == "committed NEEDLE") && !patch.RawPatch.Contains("working"), "指定コミットの差分は作業ツリーの変更を含まない");
        var all = service.ReadComparisonFiles(GitDiffMode.BaseBranch, "base", CancellationToken.None);
        Check(all.Select(file => file.Path).ToHashSet().SetEquals(new[] { "shared.txt", "z/a.txt", "b.txt" }), "ベース比較はコミット済み・未コミット・未追跡を含み相殺された変更を除く");
        patch = service.ReadDiff(all.Single(file => file.Path == "shared.txt"), CancellationToken.None);
        Check(patch.Lines.Any(line => line.Text == "initial" && line.Kind == GitDiffLineKind.Deleted) &&
              patch.Lines.Any(line => line.Text == "working needle needle" && line.Kind == GitDiffLineKind.Added), "ベースから作業ツリーまでの累積差分");
        Check(service.ReadDiff(all.Single(file => file.Path == "z/a.txt"), CancellationToken.None).RawPatch.Contains("untracked NEEDLE"), "ベース比較の未追跡ファイルの内容");
        bool rejected = false;
        try { service.ReadComparisonFiles(GitDiffMode.BaseBranch, "missing-base", CancellationToken.None); }
        catch (InvalidOperationException) { rejected = true; }
        Check(rejected, "存在しないベースブランチを明示的に報告");

        var settings = new GitSettingsManager();
        settings.SaveSettings(new GitSettings { RepositoryNameMappings = new() { new RepositoryNameMapping { Path = path, BaseBranch = "base" } } });
        using (var view = new GitDiffRepositoryView(path, 1200))
        {
            var window = new Window { Content = view, Width = 1280, Height = 920 };
            try
            {
                window.Show();
                var vm = view.ViewModel;
                GitDiffSearchResult displayedMatch = null;
                vm.SearchMatchRequested += match => displayedMatch = match;
                await WaitForAsync(() => !vm.IsBusy && vm.Lines.Count > 0);
                var modeTabs = (TabControl)view.FindName("DiffModeTabs");
                var recentCommits = (Expander)view.FindName("RecentCommitsExpander");
                Check(modeTabs.Items.Cast<TabItem>().Select(tab => tab.Header?.ToString())
                    .SequenceEqual(new[] { "未コミットの差分", "コミットの差分", "ベースとの差分" }), "差分モードを3つのタブで表示");
                var update = (Button)view.FindName("UpdateFromBaseButton");
                var pull = (Button)view.FindName("PullButton");
                Check(ReferenceEquals(update.Parent, pull.Parent) &&
                      update.TranslatePoint(new Point(), view).X > pull.TranslatePoint(new Point(), view).X &&
                      Math.Abs(update.TranslatePoint(new Point(), view).Y - pull.TranslatePoint(new Point(), view).Y) < 1,
                    "ベース取り込みボタンをプルの右隣に配置");
                Check(recentCommits.Visibility == Visibility.Collapsed, "未コミット差分では直近コミット欄を非表示");
                PressArrow(window, view, Key.PageDown);
                await WaitForAsync(() => !vm.IsBusy);
                Check(vm.Mode == GitDiffMode.Commit && modeTabs.SelectedIndex == 1 && recentCommits.IsVisible,
                    "PageDownでコミット差分へ移動して直近コミット欄を表示");
                PressArrow(window, (TextBox)view.FindName("DiffSearchBox"), Key.PageDown);
                await WaitForAsync(() => !vm.IsBusy);
                Check(vm.Mode == GitDiffMode.BaseBranch && modeTabs.SelectedIndex == 2 && recentCommits.Visibility == Visibility.Collapsed,
                    "検索欄からもPageDownでベース差分へ移動し直近コミット欄を非表示");
                PressArrow(window, view, Key.PageDown);
                Check(vm.Mode == GitDiffMode.BaseBranch, "末尾モードではPageDownで折り返さない");
                PressArrow(window, view, Key.PageUp);
                await WaitForAsync(() => !vm.IsBusy);
                PressArrow(window, (TabItem)modeTabs.SelectedItem, Key.PageUp);
                await WaitForAsync(() => !vm.IsBusy && vm.Lines.Count > 0);
                Check(vm.Mode == GitDiffMode.Uncommitted && modeTabs.SelectedIndex == 0, "PageUpで未コミット差分まで戻れる");
                PressArrow(window, view, Key.PageUp);
                Check(vm.Mode == GitDiffMode.Uncommitted, "先頭モードではPageUpで折り返さない");
                modeTabs.SelectedIndex = 1;
                await WaitForAsync(() => !vm.IsBusy);
                Check(vm.Mode == GitDiffMode.Commit && recentCommits.IsVisible, "タブ選択で表示モードとコミット欄を切り替え");
                var commitList = (ListBox)view.FindName("RecentCommitList");
                recentCommits.IsExpanded = true;
                await WaitForAsync(() => commitList.IsKeyboardFocusWithin && commitList.SelectedIndex == 0);
                Check(commitList.IsEnabled, "直近コミットを開くと一覧にフォーカス");
                var revisionBox = (TextBox)view.FindName("CommitRevisionBox");
                PressArrow(window, (UIElement)Keyboard.FocusedElement, Key.Up);
                Check(revisionBox.IsKeyboardFocused, "先頭コミットから上キーでコミット検索欄へ移動");
                PressArrow(window, revisionBox, Key.Up);
                var expanderHeader = (System.Windows.Controls.Primitives.ToggleButton)recentCommits.Template.FindName("HeaderSite", recentCommits);
                Check(expanderHeader.IsKeyboardFocused, "コミット検索欄から上キーで開閉ボタンへ移動");
                var headerBorder = (Border)expanderHeader.Template.FindName("HeaderBorder", expanderHeader);
                Check(((System.Windows.Media.SolidColorBrush)headerBorder.BorderBrush).Color == System.Windows.Media.Color.FromRgb(117, 190, 255),
                    "直近コミット開閉ボタンのフォーカスを強調");
                await WaitForAsync(() => !vm.IsBusy && vm.Files.Count > 0);
                PressArrow(window, expanderHeader, Key.Up);
                var fileList = (ListBox)view.FindName("FileList");
                Check(fileList.IsKeyboardFocusWithin && fileList.SelectedIndex == fileList.Items.Count - 1,
                    "直近コミット開閉ボタンから上キーで最後の差分ファイルへ移動");
                PressArrow(window, (UIElement)Keyboard.FocusedElement, Key.Down);
                Check(expanderHeader.IsKeyboardFocused, "最後の差分ファイルから下キーで直近コミット開閉ボタンへ移動");
                revisionBox.Focus();
                PressArrow(window, revisionBox, Key.Down);
                Check(commitList.IsKeyboardFocusWithin && commitList.SelectedIndex == 0, "コミット検索欄から下キーで先頭コミットへ移動");
                PressArrow(window, (UIElement)Keyboard.FocusedElement, Key.Down);
                Check(commitList.SelectedIndex == 1 && commitList.IsEnabled && commitList.IsKeyboardFocusWithin,
                    "直近コミットを下キーで選択し読み込み中もフォーカスを維持");
                await WaitForAsync(() => !vm.IsBusy);
                modeTabs.SelectedIndex = 0;
                await WaitForAsync(() => !vm.IsBusy && vm.Lines.Count > 0);
                vm.SelectedSortIndex = (int)GitDiffSortOrder.FileName;
                Check(vm.Files.First().Path == "z/a.txt", "ファイル名順の並べ替え");
                vm.SelectedFile = vm.Files.First();
                var selectedBeforeMove = vm.SelectedFile;
                vm.MoveFile(vm.Files.First(), vm.Files.First(file => file.Path == "b.txt"));
                Check(vm.SelectedFile == selectedBeforeMove, "並べ替え後も選択中のファイルを維持");
                var manual = vm.Files.Select(file => file.Key).ToArray();
                await vm.RefreshAsync();
                Check(vm.Files.Select(file => file.Key).SequenceEqual(manual), "手動で並べ替えた順序を更新後も維持");
                vm.SelectedFile = vm.Files.First(file => file.Path == "shared.txt" && !file.IsStaged);
                await WaitForAsync(() => vm.Lines.Any(line => line.Text.Contains("working")));
                vm.SearchText = "needle";
                await WaitForAsync(() => !vm.IsSearching && vm.SearchResults.Count == 3);
                Check(vm.SearchResults.Count == 3 && vm.SearchResults.All(result => result.File == vm.SelectedFile), "表示中ファイルの検索と同一行の複数ヒット");
                await WaitForAsync(() => displayedMatch == vm.SelectedSearchResult);
                var firstMatch = vm.SelectedSearchResult;
                var linesBeforeNext = vm.Lines;
                PressArrow(window, (TextBox)view.FindName("DiffSearchBox"), Key.F3);
                Check(vm.SelectedSearchResult != null && vm.SelectedSearchResult != firstMatch, "F3で次の検索結果へ移動");
                Check(ReferenceEquals(vm.Lines, linesBeforeNext) && displayedMatch == vm.SelectedSearchResult, "同一ファイル内のF3移動では差分を再描画しない");
                vm.SearchAllFiles = true;
                await vm.SearchAsync();
                Check(vm.SearchResults.Select(result => result.File.Path).Distinct().Count() == 4, "ステージ済み・未ステージ・削除・未追跡を全差分検索");
                var result = vm.SearchResults.First(match => match.File.Path == "z/a.txt");
                vm.SelectedSearchResult = result;
                await WaitForAsync(() => displayedMatch == result);
                var diffView = (GitDiffTextView)view.FindName("DiffLines");
                Check(FindVisuals<TextBox>(diffView).Single().SelectedText == "NEEDLE", "検索結果を選ぶと該当ファイルの一致文字を選択して強調");
                vm.SelectedModeIndex = (int)GitDiffMode.BaseBranch;
                await WaitForAsync(() => !vm.IsBusy && vm.Files.All(file => file.IsReadOnly));
                Check(!vm.CanStageLines && !vm.CanDiscardFile && !vm.CanCommitStagedChanges && vm.SearchResults.Count == 0, "比較モードでは作業変更操作を無効化し検索結果を破棄");
                await vm.StageAllAsync();
                using (var repository = new Repository(path))
                    Check(repository.Index["z/a.txt"] == null, "比較モードの一括ステージは作業ツリーへ影響しない");
                vm.SearchText = "needle";
                await vm.SearchAsync();
                Check(vm.SearchResults.Select(match => match.File.Path).Distinct().Count() == 3, "全差分検索は選択中モードの累積差分を対象にする");
                await WaitForAsync(() => displayedMatch == vm.SelectedSearchResult);
                recentCommits.IsExpanded = true;
                window.UpdateLayout();
                SavePreview(window, "diff-features-base.png");
                vm.ShowCommit(featureSha[..7]);
                await WaitForAsync(() => !vm.IsBusy && vm.Mode == GitDiffMode.Commit && vm.Lines.Any(line => line.Text == "committed NEEDLE"));
                Check(vm.Files.Single().TargetRevision == featureSha, "SHA指定でコミット差分へ切り替え");
                ((ListBox)view.FindName("RecentCommitList")).SelectedItem = vm.RecentCommits.First(commit => commit.Sha == rootSha);
                await WaitForAsync(() => !vm.IsBusy && vm.SelectedCommit.Sha == rootSha && vm.Lines.Any(line => line.Text == "initial"));
                Check(vm.SelectedCommit.Sha == rootSha, "折りたたみ欄からコミットを選択して表示");
                File.WriteAllText(Path.Combine(path, "many-matches.txt"), string.Join("\n", Enumerable.Repeat("needle", 5100)));
                vm.SelectedModeIndex = (int)GitDiffMode.Uncommitted;
                await WaitForAsync(() => !vm.IsBusy && vm.Files.Any(file => file.Path == "many-matches.txt"));
                vm.SelectedFile = vm.Files.First(file => file.Path == "many-matches.txt");
                vm.SearchAllFiles = false;
                vm.SearchText = "needle";
                int collectionChanges = 0;
                vm.SearchResults.CollectionChanged += (_, _) => collectionChanges++;
                await vm.SearchAsync();
                Check(vm.SearchResults.Count == 5000 && collectionChanges == 1, "大量の検索結果を一度の通知で表示");
                var searchBox = (TextBox)view.FindName("DiffSearchBox");
                searchBox.Text = "needl";
                Check(vm.SearchText == "needl" && vm.SearchResults.Count == 0 && !vm.IsSearching, "検索語を削除すると大量の結果を破棄");
                window.UpdateLayout();
                SavePreview(window, "diff-features-commit.png");
            }
            finally { window.Close(); }
        }
        await TestBaseUpdateAsync(workspace);
        await TestComparisonEdgesAsync(workspace);
    }

    private static async Task TestComparisonEdgesAsync(string workspace)
    {
        string path = InitFeatureRepository(workspace, "comparison-edges");
        using (var repository = new Repository(path))
        {
            CommitFile(repository, "recreated.txt", "original\n", "initial");
            CommitFile(repository, "old-name.txt", "renamed content\n", "rename source");
            CommitFile(repository, "deleted.txt", "deleted content\n", "delete source");
            repository.CreateBranch("base", repository.Head.Tip);
            File.Delete(Path.Combine(path, "recreated.txt"));
            File.Delete(Path.Combine(path, "deleted.txt"));
            File.Move(Path.Combine(path, "old-name.txt"), Path.Combine(path, "new-name.txt"));
            Commands.Stage(repository, "*");
            repository.Commit("delete and rename", FeatureAuthor, FeatureAuthor);
            File.WriteAllText(Path.Combine(path, "recreated.txt"), "recreated content\n");
            File.WriteAllBytes(Path.Combine(path, "binary.bin"), new byte[] { 0, 1, 0, 3 });
        }
        var service = new GitDiffService(path);
        var files = service.ReadComparisonFiles(GitDiffMode.BaseBranch, "base", CancellationToken.None);
        var recreated = service.ReadDiff(files.Single(file => file.Path == "recreated.txt"), CancellationToken.None);
        Check(recreated.RawPatch.Contains("+recreated content") && recreated.RawPatch.Contains("-original"), "ベースに存在しブランチで削除後に未追跡として再作成されたファイルを比較");
        Check(files.Single(file => file.Path == "new-name.txt").OldPath == "old-name.txt", "ベース比較で名前変更を保持");
        Check(!service.ReadDiff(files.Single(file => file.Path == "new-name.txt"), CancellationToken.None).Message.Contains("表示できる差分はありません"), "ベース比較で名前変更の差分を表示");
        Check(service.ReadDiff(files.Single(file => file.Path == "deleted.txt"), CancellationToken.None).Deleted == 1, "ベース比較で削除ファイルを表示");
        Check(service.ReadDiff(files.Single(file => file.Path == "binary.bin"), CancellationToken.None).Message.Contains("バイナリ"), "ベース比較でバイナリを識別");

        string largePath = InitFeatureRepository(workspace, "search-large-diff");
        using (var repository = new Repository(largePath)) repository.Commit("empty", FeatureAuthor, FeatureAuthor, new CommitOptions { AllowEmptyCommit = true });
        File.WriteAllText(Path.Combine(largePath, "large.txt"), string.Concat(Enumerable.Repeat("ordinary\n", 20010)) + "tail needle\n");
        using var vm = new GitDiffViewModel(largePath);
        await vm.RefreshAsync();
        await WaitForAsync(() => vm.Lines.Count > 0);
        Check(vm.Lines.Count == 20000, "大きい差分は通常表示で行数を制限");
        vm.SearchText = "tail needle";
        await vm.SearchAsync();
        await WaitForAsync(() => vm.Lines.Count > 20000);
        Check(vm.SearchResults.Single().LineIndex > 20000 && vm.Lines.Last().Text == "tail needle", "全差分検索は表示上限より後ろも検索して移動できる");
    }

    private static async Task TestBaseUpdateAsync(string workspace)
    {
        string path = InitFeatureRepository(workspace, "base-update-success");
        string baseTip, oldStash;
        using (var repository = new Repository(path))
        {
            CommitFile(repository, "shared.txt", "initial\n", "initial");
            var feature = repository.Head;
            var baseBranch = repository.CreateBranch("base", feature.Tip);
            Commands.Checkout(repository, baseBranch);
            baseTip = CommitFile(repository, "base.txt", "base update\n", "base update").Sha;
            Commands.Checkout(repository, feature);
            File.WriteAllText(Path.Combine(path, "old-stash.txt"), "existing stash\n");
            oldStash = repository.Stashes.Add(FeatureAuthor, "existing", StashModifiers.IncludeUntracked).WorkTree.Sha;
            File.WriteAllText(Path.Combine(path, "shared.txt"), "staged\n");
            Commands.Stage(repository, "shared.txt");
            File.WriteAllText(Path.Combine(path, "shared.txt"), "unstaged\n");
            File.WriteAllText(Path.Combine(path, "new.txt"), "untracked\n");
        }
        await new GitBaseUpdateService(path).UpdateAsync("base");
        using (var repository = new Repository(path))
        {
            Check(repository.Head.Tip.Sha == baseTip && File.Exists(Path.Combine(path, "base.txt")), "退避後にベースブランチをマージ");
            Check(File.ReadAllText(Path.Combine(path, "shared.txt")) == "unstaged\n" &&
                  ((Blob)repository.Lookup(repository.Index["shared.txt"].Id)).GetContentText() == "staged\n" &&
                  File.ReadAllText(Path.Combine(path, "new.txt")) == "untracked\n", "ステージ状態・未ステージ・未追跡を復元");
            Check(repository.Stashes.Count() == 1 && repository.Stashes.First().WorkTree.Sha == oldStash, "復元成功時は今回のスタッシュだけ削除");
        }
        await new GitBaseUpdateService(path).UpdateAsync("base");
        using (var repository = new Repository(path)) Check(repository.Stashes.Count() == 1, "取り込むコミットがない場合も退避・復元で既存スタッシュを維持");

        string conflict = InitFeatureRepository(workspace, "base-update-restore-conflict");
        using (var repository = new Repository(conflict))
        {
            CommitFile(repository, "shared.txt", "initial\n", "initial");
            var feature = repository.Head;
            var baseBranch = repository.CreateBranch("base", feature.Tip);
            Commands.Checkout(repository, baseBranch);
            CommitFile(repository, "shared.txt", "base changes\n", "base change");
            Commands.Checkout(repository, feature);
            File.WriteAllText(Path.Combine(conflict, "shared.txt"), "local changes\n");
        }
        string error = string.Empty;
        try { await new GitBaseUpdateService(conflict).UpdateAsync("base"); }
        catch (InvalidOperationException ex) { error = ex.Message; }
        using (var repository = new Repository(conflict))
            Check(repository.Stashes.Count() == 1 && error.Contains("git stash apply --index"), "復元競合ではスタッシュと復旧案内を残す");

        string mergeConflict = InitFeatureRepository(workspace, "base-update-merge-conflict");
        using (var repository = new Repository(mergeConflict))
        {
            CommitFile(repository, "shared.txt", "initial\n", "initial");
            var feature = repository.Head;
            var baseBranch = repository.CreateBranch("base", feature.Tip);
            Commands.Checkout(repository, baseBranch);
            CommitFile(repository, "shared.txt", "base changes\n", "base change");
            Commands.Checkout(repository, feature);
            CommitFile(repository, "shared.txt", "feature changes\n", "feature change");
            File.WriteAllText(Path.Combine(mergeConflict, "local.txt"), "pending\n");
        }
        error = string.Empty;
        try { await new GitBaseUpdateService(mergeConflict).UpdateAsync("base"); }
        catch (InvalidOperationException ex) { error = ex.Message; }
        using (var repository = new Repository(mergeConflict))
            Check(repository.Stashes.Count() == 1 && repository.Index.Conflicts.Any() &&
                  !File.Exists(Path.Combine(mergeConflict, "local.txt")) && error.Contains("スタッシュ"), "マージ競合では復元を中止し退避した変更を保持");
        await TestBaseFetchAsync(workspace);
    }

    private static async Task TestBaseFetchAsync(string workspace)
    {
        string remotePath = Path.Combine(workspace, "base-update-remote.git");
        Repository.Init(remotePath, true);
        string path = InitFeatureRepository(workspace, "base-update-fetch");
        string baseName;
        using (var repository = new Repository(path))
        {
            CommitFile(repository, "initial.txt", "initial\n", "initial");
            baseName = repository.Head.FriendlyName;
            repository.Network.Remotes.Add("origin", remotePath);
        }
        await new GitBranchService(path).PushCurrentBranchAsync();
        string peerPath = Path.Combine(workspace, "base-update-peer");
        Repository.Clone(remotePath, peerPath, new CloneOptions { BranchName = baseName });
        string remoteSha;
        using (var peer = new Repository(peerPath)) remoteSha = CommitFile(peer, "remote.txt", "remote\n", "remote update").Sha;
        await new GitBranchService(peerPath).PushCurrentBranchAsync();
        using (var repository = new Repository(path)) Commands.Checkout(repository, repository.CreateBranch("feature", repository.Head.Tip));
        await new GitBaseUpdateService(path).UpdateAsync(baseName);
        using (var repository = new Repository(path))
            Check(repository.Head.Tip.Sha == remoteSha && !repository.Stashes.Any(), "ベースの追跡先をfetchして最新のコミットを取り込み、差分なしの場合はスタッシュを作らない");
    }
}
