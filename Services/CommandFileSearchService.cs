using System.IO;

namespace HotKeyCommandApp.Services
{
    public sealed record CommandFileSearchResult(IReadOnlyList<string> Paths, bool IsDriveList = false);

    public sealed class CommandFileSearchService
    {
        public async Task<CommandFileSearchResult> SearchAsync(string query, CancellationToken ct)
        {
            var rootsToSearch = new List<string>();
            string filter;

            if (Path.IsPathRooted(query))
            {
                string root;
                if (query.EndsWith(Path.DirectorySeparatorChar) || query.EndsWith(Path.AltDirectorySeparatorChar))
                {
                    root = query;
                    filter = string.Empty;
                }
                else
                {
                    root = Path.GetDirectoryName(query) ?? (query.Length >= 2 ? query.Substring(0, 3) : "C:\\");
                    filter = Path.GetFileName(query) ?? string.Empty;
                }
                if (Directory.Exists(root)) rootsToSearch.Add(root);
            }
            else
            {
                // 相対検索では従来と同じユーザーフォルダを起点にする
                string userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                if (Directory.Exists(userProfile)) rootsToSearch.Add(userProfile);

                rootsToSearch.Add(AppDomain.CurrentDomain.BaseDirectory);
                rootsToSearch.Add(Environment.GetFolderPath(Environment.SpecialFolder.Desktop));
                rootsToSearch.Add(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments));

                // 実行ディレクトリが異なる場合も検索対象に含める
                string currentDir = Directory.GetCurrentDirectory();
                if (!rootsToSearch.Contains(currentDir)) rootsToSearch.Add(currentDir);

                filter = query;
            }

            if (rootsToSearch.Count == 0)
            {
                // 起点がない場合はドライブ一覧へ戻す
                var drives = DriveInfo.GetDrives().Where(d => d.IsReady).ToList();
                return new CommandFileSearchResult(drives.Select(d => d.Name).ToList(), IsDriveList: true);
            }

            var results = await Task.Run(() =>
            {
                var found = new List<string>();
                var queue = new Queue<string>();
                foreach (var root in rootsToSearch)
                {
                    if (Directory.Exists(root)) queue.Enqueue(root);
                }
                int dirCount = 0;

                var blacklist = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                {
                    "Windows", "$Recycle.Bin", "System Volume Information", "ProgramData"
                };

                while (queue.Count > 0 && found.Count < 30 && dirCount < 1000)
                {
                    if (ct.IsCancellationRequested) return found;
                    var currentDir = queue.Dequeue();
                    dirCount++;

                    try
                    {
                        foreach (var entry in Directory.EnumerateFileSystemEntries(currentDir))
                        {
                            if (ct.IsCancellationRequested) return found;
                            string name = Path.GetFileName(entry);

                            // システム用の名前は検索結果と探索対象から除く
                            if (blacklist.Contains(name)) continue;

                            if (name.Contains(filter, StringComparison.OrdinalIgnoreCase))
                            {
                                found.Add(entry);
                                if (found.Count >= 30) return found;
                            }

                            if (Directory.Exists(entry))
                            {
                                // 深い階層も件数・探索数の上限内で検索する
                                queue.Enqueue(entry);
                            }
                        }
                    }
                    catch { /* アクセスできないディレクトリは読み飛ばす */ }
                }
                return found;
            }, ct);

            return new CommandFileSearchResult(results);
        }

        public List<string> SearchFolder(string path, string query)
        {
            return Directory.EnumerateFileSystemEntries(path)
                .Where(entry => Path.GetFileName(entry).Contains(query, StringComparison.OrdinalIgnoreCase))
                .Take(30)
                .ToList();
        }
    }
}
