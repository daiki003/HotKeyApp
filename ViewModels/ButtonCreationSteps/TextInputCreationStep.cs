using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Threading.Tasks;
using HotKeyCommandApp.Models;
using HotKeyCommandApp.Services;
using R3;

namespace HotKeyCommandApp.ViewModels.ButtonCreationSteps
{
    public class TextInputCreationStep : CreationStepBase
    {
        private readonly PresetStepDefinition _def;
        private readonly WindowService _windowService;
        private readonly CommandCategory _category;

        public override string Title => _def.Title;
        public override string Prompt => _def.Prompt;
        public override bool ShowBrowseButton => _def.ShowBrowseButton;
        public override BrowseFileType BrowseFileType => _def.BrowseFileType == "Folder" ? BrowseFileType.Folder : BrowseFileType.File;
        public string? ListSource => _def.ListSource;

        public TextInputCreationStep(PresetStepDefinition def, WindowService windowService, CommandCategory category)
        {
            _def = def;
            _windowService = windowService;
            _category = category;

            // リストの表示条件をリアクティブに管理
            IsListBoxVisible = Observable.FromEvent<NotifyCollectionChangedEventHandler, NotifyCollectionChangedEventArgs>(
                h => (sender, e) => h(e),
                h => DisplayCommands.CollectionChanged += h,
                h => DisplayCommands.CollectionChanged -= h)
                .Select(_ => _def.ShowIncrementalSearch && DisplayCommands.Count > 0)
                .ToBindableReactiveProperty(_def.ShowIncrementalSearch && DisplayCommands.Count > 0)
                .AddTo(ref _disposables);

            if (_def.ShowIncrementalSearch)
            {
                // 入力直後に現在のリストをフィルタリングする（即時フィードバック用）
                InputText.Subscribe(query => FilterCurrentResults(query)).AddTo(ref _disposables);

                InputText
                    .Debounce(TimeSpan.FromMilliseconds(200))
                    .Subscribe(async text => await PerformSearchAsync(text))
                    .AddTo(ref _disposables);
            }
        }

        private void FilterCurrentResults(string query)
        {
            if (!_def.ShowIncrementalSearch) return;

            App.Current.Dispatcher.Invoke(() => 
            {
                if (string.IsNullOrEmpty(query))
                {
                    // windows ソースの場合はクリアせず、全件表示のままにする（PerformSearchAsync が非同期で更新する）
                    if (_category != CommandCategory.WindowSwitcher && _def.ListSource != "windows")
                    {
                        DisplayCommands.Clear();
                        SelectedItem.Value = null;
                    }
                    return;
                }

                // 現在のリストから、入力文字列を含まないものを除外する
                var toRemove = DisplayCommands.Where(c => !c.Name.Contains(query, StringComparison.OrdinalIgnoreCase)).ToList();
                foreach (var item in toRemove)
                {
                    // 選択中のアイテム（フォーカスがあるもの）は残す
                    if (SelectedItem.Value == item) continue;
                    
                    DisplayCommands.Remove(item);
                }
            });
        }

        public override bool ShouldInterceptCommit()
        {
            if (!_def.ShowIncrementalSearch) return false;
            return IsListBoxVisible.Value && SelectedItem.Value != null && SelectedItem.Value.Value != InputText.Value;
        }

        public override void InterceptCommit()
        {
            if (SelectedItem.Value != null)
            {
                InputText.Value = SelectedItem.Value.Value;
                SelectedItem.Value = null;
                TriggerRequestControlFocus("InputTextBox");
            }
        }

        public override void Initialize(IDictionary<string, CreationStepResultBase> results)
        {
            if (results.TryGetValue(_def.DataKey, out var result) && result is TextInputResult textResult)
            {
                InputText.Value = textResult.Text;
            }
            else
            {
                InputText.Value = "";
            }

            // 初期化時にリストを構築（特に windows ソースの場合）
            _ = PerformSearchAsync(InputText.Value);
        }

        public override CreationStepResultBase? OnCommitted()
        {
            var text = InputText.Value?.Trim() ?? "";
            if (_def.DataKey == "name" && string.IsNullOrWhiteSpace(text))
                return null;

            return new TextInputResult
            {
                DataKey = _def.DataKey,
                Text = text
            };
        }

        private CancellationTokenSource? _searchCts;

        public override async Task PerformSearchAsync(string query)
        {
            if (!_def.ShowIncrementalSearch) return;

            // 古い検索をキャンセル
            _searchCts?.Cancel();
            _searchCts = new CancellationTokenSource();
            var ct = _searchCts.Token;

            IsSearching.Value = true;

            try
            {
                // 1. ウィンドウ検索
                if (_category == CommandCategory.WindowSwitcher || _def.ListSource == "windows")
                {
                    var windows = _windowService.GetVisibleWindowsByTitle(query)
                        .OrderByDescending(w => w.Title.StartsWith(query, StringComparison.OrdinalIgnoreCase))
                        .ThenBy(w => w.Title.Length)
                        .Take(30);

                    App.Current.Dispatcher.Invoke(() => 
                    {
                        // windows の場合は毎回リフレッシュする
                        DisplayCommands.Clear();
                        foreach (var win in windows) 
                        {
                            DisplayCommands.Add(new CommandEntry { Name = win.Title, Value = win.Title });
                        }
                    });
                }
                // 2. ファイル/フォルダパス検索
                else if (_category == CommandCategory.Open)
                {
                    await Task.Run(() => SearchFiles(query, ct), ct);
                }
            }
            catch (OperationCanceledException) { }
            catch { }
            finally
            {
                IsSearching.Value = false;
            }
        }

        private void SearchFiles(string query, CancellationToken ct)
        {
            if (string.IsNullOrEmpty(query))
            {
                foreach (var drive in DriveInfo.GetDrives().Where(d => d.IsReady))
                {
                    if (ct.IsCancellationRequested) return;
                    App.Current.Dispatcher.Invoke(() => 
                    {
                        if (!DisplayCommands.Any(c => string.Equals(c.Value, drive.Name, StringComparison.OrdinalIgnoreCase)))
                        {
                            DisplayCommands.Add(new CommandEntry { Name = drive.Name, Value = drive.Name });
                        }
                    });
                }
                return;
            }

            // 入力にパス区切りが含まれる場合は、そのディレクトリ内を優先検索
            if (query.Contains("\\") || query.Contains("/"))
            {
                string dir = Directory.Exists(query) ? query : (Path.GetDirectoryName(query) ?? "");
                if (Directory.Exists(dir))
                {
                    SearchRecursively(dir, query, ct, 30);
                }
            }
            else
            {
                // パス区切りがない場合：ユーザーフォルダと全ドライブを再帰検索
                string userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                SearchRecursively(userProfile, query, ct, 30);
                
                foreach (var drive in DriveInfo.GetDrives().Where(d => d.IsReady))
                {
                    if (ct.IsCancellationRequested) return;
                    SearchRecursively(drive.Name, query, ct, 50);
                }
            }
        }

        private void SearchRecursively(string root, string query, CancellationToken ct, int maxResults)
        {
            var queue = new Queue<string>();
            queue.Enqueue(root);
            string searchPattern = query.Contains("\\") || query.Contains("/") ? (Path.GetFileName(query) ?? "") : query;

            if (!string.IsNullOrEmpty(searchPattern))
            {
                string rootName = Path.GetFileName(root);
                if (!string.IsNullOrEmpty(rootName) && rootName.Contains(searchPattern, StringComparison.OrdinalIgnoreCase))
                {
                    App.Current.Dispatcher.Invoke(() => 
                    {
                        if (!DisplayCommands.Any(c => string.Equals(c.Value, root, StringComparison.OrdinalIgnoreCase)))
                        {
                            DisplayCommands.Add(new CommandEntry { Name = root, Value = root });
                        }
                    });
                }
            }

            while (queue.Count > 0 && !ct.IsCancellationRequested)
            {
                // 結果数が上限に達していたら終了
                int currentCount = 0;
                App.Current.Dispatcher.Invoke(() => currentCount = DisplayCommands.Count);
                if (currentCount >= maxResults) break;

                var dir = queue.Dequeue();
                try
                {
                    // ファイル・フォルダの列挙
                    foreach (var entry in Directory.EnumerateFileSystemEntries(dir))
                    {
                        if (ct.IsCancellationRequested) return;

                        App.Current.Dispatcher.Invoke(() => currentCount = DisplayCommands.Count);
                        if (currentCount >= maxResults) return;

                        string name = Path.GetFileName(entry);
                        if (name.Contains(searchPattern, StringComparison.OrdinalIgnoreCase))
                        {
                            App.Current.Dispatcher.Invoke(() => 
                            {
                                if (!DisplayCommands.Any(c => string.Equals(c.Value, entry, StringComparison.OrdinalIgnoreCase)))
                                {
                                    DisplayCommands.Add(new CommandEntry { Name = entry, Value = entry });
                                }
                            });
                        }

                        // ディレクトリであればキューに積む（幅優先探索）
                        if (Directory.Exists(entry))
                        {
                            if (!IsSkipDir(name))
                            {
                                queue.Enqueue(entry);
                            }
                        }
                    }
                }
                catch (UnauthorizedAccessException) { }
                catch (Exception) { }
            }
        }

        private bool IsSkipDir(string name)
        {
            var skip = new[] { 
                "Windows", "Program Files", "Program Files (x86)", "ProgramData", 
                "AppData", "$Recycle.Bin", "System Volume Information", "node_modules", ".git" 
            };
            return skip.Any(s => string.Equals(s, name, StringComparison.OrdinalIgnoreCase));
        }
    }
}
