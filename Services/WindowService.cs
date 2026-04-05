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

        [DllImport("user32.dll")]
        private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

        private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern int GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);

        [DllImport("user32.dll")]
        private static extern bool IsWindowVisible(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern bool SetForegroundWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        private static extern bool IsIconic(IntPtr hWnd);

        private const int SW_RESTORE = 9;

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
                if (IsIconic(targetHWnd))
                {
                    ShowWindow(targetHWnd, SW_RESTORE);
                }

                SetForegroundWindow(targetHWnd);
                return true;
            }

            return false;
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
                    // If the app itself is foreground, find the first matching window in Z-order
                    // but if that top match was the one active just before the app, cycle to next.

                    // Find the "previously active" window by looking for the first "real" window 
                    // that is NOT our process in Z-order.
                    IntPtr prevWindow = currentForeground;
                    while ((prevWindow = GetWindow(prevWindow, GW_HWNDNEXT)) != IntPtr.Zero)
                    {
                        uint pid;
                        GetWindowThreadProcessId(prevWindow, out pid);
                        if (pid == myPid) continue;
                        if (!IsWindowVisible(prevWindow)) continue;

                        // Check if it's a "real" window by checking for a non-empty title
                        StringBuilder sb = new StringBuilder(256);
                        GetWindowText(prevWindow, sb, sb.Capacity);
                        string titleText = sb.ToString();

                        if (string.IsNullOrWhiteSpace(titleText)) continue;
                        if (titleText == "Program Manager") continue;

                        break;
                    }

                    int prevIndex = windows.IndexOf(prevWindow);
                    targetHWnd = GetTargetHWnd(windows, prevIndex);
                }
                else
                {
                    int currentIndex = windows.IndexOf(currentForeground);
                    targetHWnd = GetTargetHWnd(windows, currentIndex);
                }


                if (targetHWnd != IntPtr.Zero)
                {
                    if (IsIconic(targetHWnd))
                    {
                        ShowWindow(targetHWnd, SW_RESTORE);
                    }

                    SetForegroundWindow(targetHWnd);
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

                    StringBuilder sb = new StringBuilder(256);
                    GetWindowText(hWnd, sb, sb.Capacity);
                    string title = sb.ToString();

                    if (!string.IsNullOrWhiteSpace(title) && title != "Program Manager")
                    {
                        action(hWnd, title);
                    }
                }
                return true;
            }, IntPtr.Zero);
        }
    }
}
