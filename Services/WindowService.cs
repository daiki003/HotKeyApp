using System;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Interop;
using System.Windows;
using System.Collections.Generic;
using System.Linq;

namespace HotKeyCommandApp.Services
{
    public class WindowService
    {
        private static readonly Dictionary<string, ImageSource> _iconCache = new();

        private static IntPtr GetClassLongPtr(IntPtr hWnd, int nIndex)
        {
            if (IntPtr.Size == 4) return (IntPtr)NativeMethods.GetClassLong32(hWnd, nIndex);
            else return NativeMethods.GetClassLongPtr64(hWnd, nIndex);
        }

        private static IntPtr GetWindowLongPtr(IntPtr hWnd, int nIndex)
        {
            if (IntPtr.Size == 4) return (IntPtr)NativeMethods.GetWindowLong32(hWnd, nIndex);
            else return NativeMethods.GetWindowLongPtr64(hWnd, nIndex);
        }

        public bool ActivateVscodeWindow(string target)
        {
            if (string.IsNullOrWhiteSpace(target)) return false;

            string nameToMatch = System.IO.Path.GetFileName(target);
            IntPtr targetHWnd = IntPtr.Zero;

            EnumerateRealWindows((hWnd, title) =>
            {
                // VS Codeのタイトルには通常フォルダ名やファイル名、そして "Visual Studio Code" が含まれる
                if (title.Contains("Visual Studio Code", StringComparison.OrdinalIgnoreCase) &&
                    title.Contains(nameToMatch, StringComparison.OrdinalIgnoreCase))
                {
                    targetHWnd = hWnd;
                }
            });

            if (targetHWnd != IntPtr.Zero)
            {
                FocusWindow(targetHWnd);
                return true;
            }

            return false;
        }

        public bool ActivateWindowByProcessPath(string executablePath)
        {
            if (string.IsNullOrWhiteSpace(executablePath)) return false;

            // 比較用に正規化
            string targetPath = System.IO.Path.GetFullPath(executablePath).ToLowerInvariant();
            IntPtr targetHWnd = IntPtr.Zero;

            EnumerateRealWindows((hWnd, title) =>
            {
                if (targetHWnd != IntPtr.Zero) return;

                uint pid;
                NativeMethods.GetWindowThreadProcessId(hWnd, out pid);
                string processPath = GetProcessPath(pid);

                if (!string.IsNullOrEmpty(processPath) && processPath.ToLowerInvariant() == targetPath)
                {
                    targetHWnd = hWnd;
                }
            });

            if (targetHWnd != IntPtr.Zero)
            {
                FocusWindow(targetHWnd);
                return true;
            }

            return false;
        }

        public bool ActivateWindowByProcessName(string processName)
        {
            if (string.IsNullOrWhiteSpace(processName)) return false;

            string targetName = processName.ToLowerInvariant();
            IntPtr targetHWnd = IntPtr.Zero;

            EnumerateRealWindows((hWnd, title) =>
            {
                if (targetHWnd != IntPtr.Zero) return;

                uint pid;
                NativeMethods.GetWindowThreadProcessId(hWnd, out pid);
                string processPath = GetProcessPath(pid);

                if (!string.IsNullOrEmpty(processPath))
                {
                    string nameOnly = System.IO.Path.GetFileNameWithoutExtension(processPath).ToLowerInvariant();
                    if (nameOnly == targetName)
                    {
                        targetHWnd = hWnd;
                    }
                }
            });

            if (targetHWnd != IntPtr.Zero)
            {
                FocusWindow(targetHWnd);
                return true;
            }

            return false;
        }

        private string GetProcessPath(uint pid)
        {
            IntPtr hProcess = NativeMethods.OpenProcess(NativeMethods.PROCESS_QUERY_LIMITED_INFORMATION, false, pid);
            if (hProcess != IntPtr.Zero)
            {
                try
                {
                    int size = 1024;
                    StringBuilder sb = new StringBuilder(size);
                    if (NativeMethods.QueryFullProcessImageName(hProcess, 0, sb, ref size))
                    {
                        return sb.ToString();
                    }
                }
                finally
                {
                    NativeMethods.CloseHandle(hProcess);
                }
            }
            return string.Empty;
        }

        public void FocusWindow(IntPtr hWnd)
        {
            if (NativeMethods.IsIconic(hWnd))
            {
                NativeMethods.ShowWindow(hWnd, NativeMethods.SW_RESTORE);
            }
            else
            {
                NativeMethods.ShowWindow(hWnd, NativeMethods.SW_SHOW);
            }

            // Force Z-order change
            NativeMethods.SetWindowPos(hWnd, NativeMethods.HWND_TOP, 0, 0, 0, 0, NativeMethods.SWP_NOSIZE | NativeMethods.SWP_NOMOVE | NativeMethods.SWP_SHOWWINDOW);

            // Give it the focus
            if (!NativeMethods.SetForegroundWindow(hWnd))
            {
                // If it fails, maybe try BringWindowToTop again
                NativeMethods.BringWindowToTop(hWnd);
            }
        }

        public int ActivateWindowByTitle(string title)
        {
            var windows = new List<IntPtr>();
            EnumerateRealWindows((hWnd, windowTitle) =>
            {
                if (windowTitle.Contains(title, StringComparison.OrdinalIgnoreCase))
                {
                    windows.Add(hWnd);
                }
            });

            if (windows.Any())
            {
                IntPtr currentForeground = NativeMethods.GetForegroundWindow();
                uint currentPid;
                NativeMethods.GetWindowThreadProcessId(currentForeground, out currentPid);
                uint myPid = (uint)System.Diagnostics.Process.GetCurrentProcess().Id;

                IntPtr targetHWnd = IntPtr.Zero;

                if (currentPid == myPid)
                {
                    // If the app itself is foreground (palette open), find the window it was "covering"
                    // Get the "actual" window behind the palette by solving child windows to roots
                    IntPtr prevWindow = currentForeground;
                    while ((prevWindow = NativeMethods.GetWindow(prevWindow, NativeMethods.GW_HWNDNEXT)) != IntPtr.Zero)
                    {
                        uint pid;
                        NativeMethods.GetWindowThreadProcessId(prevWindow, out pid);
                        if (pid == myPid) continue;
                        if (!NativeMethods.IsWindowVisible(prevWindow)) continue;

                        // Resolve to root window to match the enumerated list
                        IntPtr rootWindow = NativeMethods.GetAncestor(prevWindow, NativeMethods.GA_ROOT);
                        int prevIndex = windows.IndexOf(rootWindow);
                        if (prevIndex >= 0)
                        {
                            targetHWnd = GetTargetHWnd(windows, prevIndex);
                            break;
                        }
                    }

                    if (targetHWnd == IntPtr.Zero)
                    {
                        targetHWnd = windows.LastOrDefault();
                    }
                }
                else
                {
                    // If focusing from another app, resolve that app's window to its root
                    IntPtr rootForeground = NativeMethods.GetAncestor(currentForeground, NativeMethods.GA_ROOT);
                    int currentIndex = windows.IndexOf(rootForeground);
                    targetHWnd = GetTargetHWnd(windows, currentIndex);
                }


                if (targetHWnd != IntPtr.Zero)
                {
                    FocusWindow(targetHWnd);
                }
            }
            return windows.Count;
        }
        
        private IntPtr GetTargetHWnd(List<IntPtr> windows, int prevIndex)
        {
            if (prevIndex >= 0 && windows.Count > 1)
            {
                int targetIndex = (prevIndex - 1 + windows.Count) % windows.Count;
                return windows[targetIndex];
            }
            else
            {
                return windows.Last();
            }
        }

        public List<string> GetAllVisibleWindowTitles()
        {
            var titles = new List<string>();
            EnumerateRealWindows((hWnd, title) =>
            {
                if (!titles.Contains(title))
                {
                    titles.Add(title);
                }
            });
            return titles;
        }

        public class WindowInfo
        {
            public IntPtr Handle { get; set; }
            public string Title { get; set; } = string.Empty;
            public ImageSource? Icon { get; set; }
            public int Width { get; set; }
            public int Height { get; set; }
        }

        public List<WindowInfo> GetVisibleWindowsByTitle(string filter)
        {
            var results = new List<WindowInfo>();
            var filterParts = (filter ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries);

            EnumerateRealWindows((hWnd, title) =>
            {
                bool isMatch = true;
                if (filterParts.Length > 0)
                {
                    foreach (var part in filterParts)
                    {
                        if (!title.Contains(part, StringComparison.OrdinalIgnoreCase))
                        {
                            isMatch = false;
                            break;
                        }
                    }
                }

                if (isMatch)
                {
                    NativeMethods.RECT rect;
                    NativeMethods.GetWindowRect(hWnd, out rect);
                    results.Add(new WindowInfo
                    {
                        Handle = hWnd,
                        Title = title,
                        Icon = GetWindowIconSource(hWnd, title),
                        Width = rect.Width,
                        Height = rect.Height
                    });
                }
            });
            return results;
        }

        public ImageSource? GetWindowIconSource(string title)
        {
            if (string.IsNullOrEmpty(title)) return null;

            IntPtr hWnd = IntPtr.Zero;
            EnumerateRealWindows((h, t) =>
            {
                if (t.Contains(title, StringComparison.OrdinalIgnoreCase))
                {
                    hWnd = h;
                }
            });

            if (hWnd == IntPtr.Zero) return null;
            return GetWindowIconSource(hWnd, title);
        }

        private ImageSource? GetWindowIconSource(IntPtr hWnd, string title)
        {
            // Check cache first
            lock (_iconCache)
            {
                if (_iconCache.TryGetValue(title, out var cached)) return cached;
            }

            IntPtr hIcon = GetWindowIcon(hWnd);
            if (hIcon != IntPtr.Zero)
            {
                try
                {
                    var source = Imaging.CreateBitmapSourceFromHIcon(
                        hIcon,
                        Int32Rect.Empty,
                        BitmapSizeOptions.FromEmptyOptions());
                    source.Freeze();

                    // Update cache
                    lock (_iconCache)
                    {
                        _iconCache[title] = source;
                    }

                    return source;
                }
                catch
                {
                    return null;
                }
            }
            return null;
        }

        private IntPtr GetSendMessageIcon(IntPtr hWnd, IntPtr iconType)
        {
            IntPtr result;
            if (NativeMethods.SendMessageTimeout(hWnd, NativeMethods.WM_GETICON, iconType, IntPtr.Zero, NativeMethods.SMTO_ABORTIFHUNG, 100, out result) != IntPtr.Zero)
            {
                return result;
            }
            return IntPtr.Zero;
        }

        private IntPtr GetWindowIcon(IntPtr hWnd)
        {
            // 1. Try sending WM_GETICON (with timeout)
            IntPtr hIcon = GetSendMessageIcon(hWnd, NativeMethods.ICON_SMALL2);
            if (hIcon == IntPtr.Zero)
                hIcon = GetSendMessageIcon(hWnd, NativeMethods.ICON_SMALL);
            if (hIcon == IntPtr.Zero)
                hIcon = GetSendMessageIcon(hWnd, NativeMethods.ICON_BIG);

            // 2. Try class long
            if (hIcon == IntPtr.Zero)
                hIcon = GetClassLongPtr(hWnd, NativeMethods.GCLP_HICONSM);
            if (hIcon == IntPtr.Zero)
                hIcon = GetClassLongPtr(hWnd, NativeMethods.GCLP_HICON);

            return hIcon;
        }

        private void EnumerateRealWindows(Action<IntPtr, string> action)
        {
            uint myPid = (uint)System.Diagnostics.Process.GetCurrentProcess().Id;

            NativeMethods.EnumWindowsProc proc = (hWnd, lParam) =>
            {
                if (NativeMethods.IsWindowVisible(hWnd))
                {
                    uint pid;
                    NativeMethods.GetWindowThreadProcessId(hWnd, out pid);
                    if (pid == myPid) return true;

                    // Ensure it's a top-level main window
                    // Main windows usually don't have an owner
                    if (NativeMethods.GetWindow(hWnd, NativeMethods.GW_OWNER) != IntPtr.Zero) return true;

                    // Skip Tool Windows (those that don't appear in Taskbar)
                    long exStyle = (long)GetWindowLongPtr(hWnd, NativeMethods.GWL_EXSTYLE);
                    if ((exStyle & NativeMethods.WS_EX_TOOLWINDOW) != 0) return true;

                    StringBuilder sb = new StringBuilder(256);
                    NativeMethods.GetWindowText(hWnd, sb, sb.Capacity);
                    string title = sb.ToString();

                    if (!string.IsNullOrWhiteSpace(title) && title != "Program Manager")
                    {
                        // Check if the window has a non-zero size
                        NativeMethods.RECT rect;
                        if (NativeMethods.GetWindowRect(hWnd, out rect))
                        {
                            if (rect.Width > 0 && rect.Height > 0)
                        {
                                action(hWnd, title);
                            }
                        }
                    }
                }
                return true;
            };

            NativeMethods.EnumWindows(proc, IntPtr.Zero);
        }
        public string? ShowBrowseDialog(bool isFolder, string filter, string? initialPath = null)
        {
            string? dirToOpen = null;
            string? fileToSelect = null;

            if (!string.IsNullOrWhiteSpace(initialPath))
            {
                try
                {
                    if (System.IO.Directory.Exists(initialPath))
                    {
                        dirToOpen = initialPath;
                    }
                    else if (System.IO.File.Exists(initialPath))
                    {
                        dirToOpen = System.IO.Path.GetDirectoryName(initialPath);
                        fileToSelect = System.IO.Path.GetFileName(initialPath);
                    }
                    else
                    {
                        string? parent = System.IO.Path.GetDirectoryName(initialPath);
                        if (!string.IsNullOrEmpty(parent) && System.IO.Directory.Exists(parent))
                        {
                            dirToOpen = parent;
                            fileToSelect = System.IO.Path.GetFileName(initialPath);
                        }
                    }
                }
                catch { } // 無効なパス文字列などの例外を無視
            }

            if (isFolder)
            {
                var dialog = new Microsoft.Win32.OpenFolderDialog
                {
                    Title = "フォルダを選択してください"
                };
                if (!string.IsNullOrEmpty(dirToOpen))
                {
                    dialog.InitialDirectory = dirToOpen;
                }
                return dialog.ShowDialog() == true ? dialog.FolderName : null;
            }
            else
            {
                var dialog = new Microsoft.Win32.OpenFileDialog
                {
                    Title = "ファイルを選択してください",
                    Filter = filter
                };
                if (!string.IsNullOrEmpty(dirToOpen))
                {
                    dialog.InitialDirectory = dirToOpen;
                }
                if (!string.IsNullOrEmpty(fileToSelect))
                {
                    dialog.FileName = fileToSelect;
                }
                return dialog.ShowDialog() == true ? dialog.FileName : null;
            }
        }
    }
}
