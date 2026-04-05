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
        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        static extern IntPtr SendMessage(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr OpenProcess(uint processAccess, bool bInheritHandle, uint processId);

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Auto)]
        private static extern bool QueryFullProcessImageName(IntPtr hProcess, int dwFlags, StringBuilder lpExeName, ref int lpdwSize);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool CloseHandle(IntPtr hObject);

        private const uint PROCESS_QUERY_LIMITED_INFORMATION = 0x1000;

        [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Auto)]
        static extern IntPtr SendMessageTimeout(IntPtr windowHandle, uint Msg, IntPtr wParam, IntPtr lParam, uint flags, uint timeout, out IntPtr result);

        private const uint SMTO_ABORTIFHUNG = 0x0002;
        private const uint WM_GETICON = 0x007F;
        private static readonly IntPtr ICON_SMALL = (IntPtr)0;
        private static readonly IntPtr ICON_BIG = (IntPtr)1;
        private static readonly IntPtr ICON_SMALL2 = (IntPtr)2;

        [DllImport("user32.dll", EntryPoint = "GetClassLong")]
        private static extern uint GetClassLong32(IntPtr hWnd, int nIndex);

        [DllImport("user32.dll", EntryPoint = "GetClassLongPtr")]
        private static extern IntPtr GetClassLongPtr64(IntPtr hWnd, int nIndex);

        private static IntPtr GetClassLongPtr(IntPtr hWnd, int nIndex)
        {
            if (IntPtr.Size == 4) return (IntPtr)GetClassLong32(hWnd, nIndex);
            else return GetClassLongPtr64(hWnd, nIndex);
        }

        private const int GCLP_HICONSM = -34;
        private const int GCLP_HICON = -14;

        private static readonly Dictionary<string, ImageSource> _iconCache = new();

        [DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

        [DllImport("user32.dll")]
        private static extern IntPtr GetWindow(IntPtr hWnd, uint uCmd);

        private const uint GW_HWNDNEXT = 2;
        private const uint GW_OWNER = 4;

        [DllImport("user32.dll")]
        private static extern IntPtr GetAncestor(IntPtr hWnd, uint uCmd);
        private const uint GA_ROOT = 2;

        [DllImport("user32.dll", EntryPoint = "GetWindowLong")]
        private static extern int GetWindowLong32(IntPtr hWnd, int nIndex);

        [DllImport("user32.dll", EntryPoint = "GetWindowLongPtr")]
        private static extern IntPtr GetWindowLongPtr64(IntPtr hWnd, int nIndex);

        private static IntPtr GetWindowLongPtr(IntPtr hWnd, int nIndex)
        {
            if (IntPtr.Size == 4) return (IntPtr)GetWindowLong32(hWnd, nIndex);
            else return GetWindowLongPtr64(hWnd, nIndex);
        }

        private const int GWL_EXSTYLE = -20;
        private const uint WS_EX_TOOLWINDOW = 0x00000080;

        [DllImport("user32.dll")]
        private static extern bool BringWindowToTop(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

        [StructLayout(LayoutKind.Sequential)]
        public struct RECT
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
            public int Width => Right - Left;
            public int Height => Bottom - Top;
        }

        [DllImport("user32.dll")]
        private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

        private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern int GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);

        [DllImport("user32.dll")]
        private static extern bool IsWindowVisible(IntPtr hWnd);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool SetForegroundWindow(IntPtr hWnd);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);

        private static readonly IntPtr HWND_TOP = (IntPtr)0;
        private static readonly IntPtr HWND_TOPMOST = (IntPtr)(-1);
        private const uint SWP_NOSIZE = 0x0001;
        private const uint SWP_NOMOVE = 0x0002;
        private const uint SWP_SHOWWINDOW = 0x0040;

        [DllImport("user32.dll")]
        private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        private static extern bool IsIconic(IntPtr hWnd);

        private const int SW_RESTORE = 9;
        private const int SW_SHOW = 5;

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
                GetWindowThreadProcessId(hWnd, out pid);
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
                GetWindowThreadProcessId(hWnd, out pid);
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
            IntPtr hProcess = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, false, pid);
            if (hProcess != IntPtr.Zero)
            {
                try
                {
                    int size = 1024;
                    StringBuilder sb = new StringBuilder(size);
                    if (QueryFullProcessImageName(hProcess, 0, sb, ref size))
                    {
                        return sb.ToString();
                    }
                }
                finally
                {
                    CloseHandle(hProcess);
                }
            }
            return string.Empty;
        }

        private void FocusWindow(IntPtr hWnd)
        {
            if (IsIconic(hWnd))
            {
                ShowWindow(hWnd, SW_RESTORE);
            }
            else
            {
                ShowWindow(hWnd, SW_SHOW);
            }

            // Force Z-order change
            SetWindowPos(hWnd, HWND_TOP, 0, 0, 0, 0, SWP_NOSIZE | SWP_NOMOVE | SWP_SHOWWINDOW);

            // Give it the focus
            if (!SetForegroundWindow(hWnd))
            {
                // If it fails, maybe try BringWindowToTop again
                BringWindowToTop(hWnd);
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
                IntPtr currentForeground = GetForegroundWindow();
                uint currentPid;
                GetWindowThreadProcessId(currentForeground, out currentPid);
                uint myPid = (uint)System.Diagnostics.Process.GetCurrentProcess().Id;

                IntPtr targetHWnd = IntPtr.Zero;

                if (currentPid == myPid)
                {
                    // If the app itself is foreground (palette open), find the window it was "covering"
                    // Get the "actual" window behind the palette by solving child windows to roots
                    IntPtr prevWindow = currentForeground;
                    while ((prevWindow = GetWindow(prevWindow, GW_HWNDNEXT)) != IntPtr.Zero)
                    {
                        uint pid;
                        GetWindowThreadProcessId(prevWindow, out pid);
                        if (pid == myPid) continue;
                        if (!IsWindowVisible(prevWindow)) continue;

                        // Resolve to root window to match the enumerated list
                        IntPtr rootWindow = GetAncestor(prevWindow, GA_ROOT);
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
                    IntPtr rootForeground = GetAncestor(currentForeground, GA_ROOT);
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

        public ImageSource? GetWindowIconSource(string title)
        {
            if (string.IsNullOrEmpty(title)) return null;

            // Check cache first
            lock (_iconCache)
            {
                if (_iconCache.TryGetValue(title, out var cached)) return cached;
            }

            IntPtr hWnd = IntPtr.Zero;
            EnumerateRealWindows((h, t) =>
            {
                if (t.Contains(title, StringComparison.OrdinalIgnoreCase))
                {
                    hWnd = h;
                }
            });

            if (hWnd == IntPtr.Zero) return null;

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
            if (SendMessageTimeout(hWnd, WM_GETICON, iconType, IntPtr.Zero, SMTO_ABORTIFHUNG, 100, out result) != IntPtr.Zero)
            {
                return result;
            }
            return IntPtr.Zero;
        }

        private IntPtr GetWindowIcon(IntPtr hWnd)
        {
            // 1. Try sending WM_GETICON (with timeout)
            IntPtr hIcon = GetSendMessageIcon(hWnd, ICON_SMALL2);
            if (hIcon == IntPtr.Zero)
                hIcon = GetSendMessageIcon(hWnd, ICON_SMALL);
            if (hIcon == IntPtr.Zero)
                hIcon = GetSendMessageIcon(hWnd, ICON_BIG);

            // 2. Try class long
            if (hIcon == IntPtr.Zero)
                hIcon = GetClassLongPtr(hWnd, GCLP_HICONSM);
            if (hIcon == IntPtr.Zero)
                hIcon = GetClassLongPtr(hWnd, GCLP_HICON);

            return hIcon;
        }

        private void EnumerateRealWindows(Action<IntPtr, string> action)
        {
            uint myPid = (uint)System.Diagnostics.Process.GetCurrentProcess().Id;

            EnumWindows((hWnd, lParam) =>
            {
                if (IsWindowVisible(hWnd))
                {
                    uint pid;
                    GetWindowThreadProcessId(hWnd, out pid);
                    if (pid == myPid) return true;

                    // Ensure it's a top-level main window
                    // Main windows usually don't have an owner
                    if (GetWindow(hWnd, GW_OWNER) != IntPtr.Zero) return true;

                    // Skip Tool Windows (those that don't appear in Taskbar)
                    long exStyle = (long)GetWindowLongPtr(hWnd, GWL_EXSTYLE);
                    if ((exStyle & WS_EX_TOOLWINDOW) != 0) return true;

                    StringBuilder sb = new StringBuilder(256);
                    GetWindowText(hWnd, sb, sb.Capacity);
                    string title = sb.ToString();

                    if (!string.IsNullOrWhiteSpace(title) && title != "Program Manager")
                    {
                        // Check if the window has a non-zero size
                        RECT rect;
                        if (GetWindowRect(hWnd, out rect))
                        {
                            if (rect.Width > 0 && rect.Height > 0)
                            {
                                action(hWnd, title);
                            }
                        }
                    }
                }
                return true;
            }, IntPtr.Zero);
        }
    }
}
