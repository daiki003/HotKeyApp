using System;
using System.Windows;
using System.Windows.Interop;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Media;
using System.Windows.Input;
using System.Windows.Controls;
using System.Reflection;

namespace HotKeyCommandApp.Services
{
    /// <summary>
    /// ウィンドウの低レベル操作を共通化するヘルパークラス。
    /// </summary>
    public static class WindowHelper
    {
        /// <summary>
        /// 指定したウィンドウにおいて、AltキーやF10キーによるシステムメニュー（移動、サイズ変更など）の表示を無効化します。
        /// </summary>
        /// <param name="window">対象のウィンドウ</param>
        public static void DisableSystemMenu(Window window)
        {
            if (window == null) return;

            // ウィンドウハンドルが作成されているか確認
            if (PresentationSource.FromVisual(window) != null)
            {
                AddHook(window);
            }
            else
            {
                // まだの場合は初期化完了イベントでフックを登録
                window.SourceInitialized += (s, e) => AddHook(window);
            }
        }

        /// <summary>
        /// 指定したウィンドウを、マルチモニター環境において最も右側にあるモニターの中央に配置します。
        /// </summary>
        public static void CenterOnRightmostMonitor(Window window)
        {
            if (window == null) return;

            var monitors = new List<NativeMethods.MONITORINFO>();
            NativeMethods.EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, (IntPtr hMonitor, IntPtr hdcMonitor, ref NativeMethods.RECT lprcMonitor, IntPtr dwData) =>
            {
                var mi = new NativeMethods.MONITORINFO();
                mi.cbSize = Marshal.SizeOf(typeof(NativeMethods.MONITORINFO));
                if (NativeMethods.GetMonitorInfo(hMonitor, ref mi))
                {
                    monitors.Add(mi);
                }
                return true;
            }, IntPtr.Zero);

            if (monitors.Count == 0) return;

            // 最も右側にあるモニターを特定（Left座標が最大のもの）
            var rightmost = monitors.OrderByDescending(m => m.rcMonitor.Left).First();

            // DPI スケーリングの取得
            var dpi = VisualTreeHelper.GetDpi(window);
            double dpiScaleX = dpi.DpiScaleX;
            double dpiScaleY = dpi.DpiScaleY;

            // モニターの座標（ピクセル）
            double mLeft = rightmost.rcMonitor.Left;
            double mTop = rightmost.rcMonitor.Top;
            double mWidth = rightmost.rcMonitor.Width;
            double mHeight = rightmost.rcMonitor.Height;

            // WPF座標 (DIP) への変換
            double dipLeft = mLeft / dpiScaleX;
            double dipTop = mTop / dpiScaleY;
            double dipWidth = mWidth / dpiScaleX;
            double dipHeight = mHeight / dpiScaleY;

            // ウィンドウサイズ（DIP）を取得するために一度 Measure/Arrange が必要かもしれないが、
            // すでに SizeToContent でサイズが決まっていることを想定
            // レイアウトを強制的に更新（これで ActualWidth/Height が確定する）
            window.UpdateLayout();

            double wWidth = window.ActualWidth;
            double wHeight = window.ActualHeight;

            // WPF座標が0の場合のフォールバック (Win32 API から直接取得)
            if (wWidth <= 0 || wHeight <= 0)
            {
                var helper = new WindowInteropHelper(window);
                if (NativeMethods.GetWindowRect(helper.Handle, out NativeMethods.RECT rect))
                {
                    wWidth = rect.Width / dpiScaleX;
                    wHeight = rect.Height / dpiScaleY;
                }
            }

            // 中央座標の計算
            window.Left = dipLeft + (dipWidth - wWidth) / 2;
            window.Top = dipTop + (dipHeight - wHeight) / 2;
        }

        /// <summary>
        /// 指定されたサイズを元に、最右モニターの中央に配置するための座標（DIP）を計算します。
        /// </summary>
        public static Point GetRightmostMonitorCenter(double dipWidth, double dipHeight)
        {
            var monitors = new List<NativeMethods.MONITORINFO>();
            NativeMethods.EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, (IntPtr hMonitor, IntPtr hdcMonitor, ref NativeMethods.RECT lprcMonitor, IntPtr dwData) =>
            {
                var mi = new NativeMethods.MONITORINFO();
                mi.cbSize = Marshal.SizeOf(typeof(NativeMethods.MONITORINFO));
                if (NativeMethods.GetMonitorInfo(hMonitor, ref mi))
                {
                    monitors.Add(mi);
                }
                return true;
            }, IntPtr.Zero);

            if (monitors.Count == 0) return new Point(0, 0);

            // 最も右側にあるモニターを特定
            var rightmost = monitors.OrderByDescending(m => m.rcMonitor.Left).First();

            // システム全体のDPIを使用（ウィンドウ生成前の計算用）
            // 注意: マルチモニターでDPIが異なる場合は本来微調整が必要だが、
            // 生成前の概算としてはこれで十分。
            double dpiScaleX = 1.0;
            double dpiScaleY = 1.0;

            using (var source = new HwndSource(new HwndSourceParameters()))
            {
                if (source.CompositionTarget != null)
                {
                    dpiScaleX = source.CompositionTarget.TransformToDevice.M11;
                    dpiScaleY = source.CompositionTarget.TransformToDevice.M22;
                }
            }

            // モニターの座標（ピクセル）
            double mLeft = rightmost.rcMonitor.Left;
            double mTop = rightmost.rcMonitor.Top;
            double mWidth = rightmost.rcMonitor.Width;
            double mHeight = rightmost.rcMonitor.Height;

            // WPF座標 (DIP) への変換
            double monitorDipLeft = mLeft / dpiScaleX;
            double monitorDipTop = mTop / dpiScaleY;
            double monitorDipWidth = mWidth / dpiScaleX;
            double monitorDipHeight = mHeight / dpiScaleY;

            return new Point(
                monitorDipLeft + (monitorDipWidth - dipWidth) / 2,
                monitorDipTop + (monitorDipHeight - dipHeight) / 2
            );
        }

        /// <summary>
        /// Ctrl+矢印によるウィンドウの移動機能を有効化します。
        /// ダイアログから呼び出す場合、Ownerウィンドウも連動して移動します。
        /// </summary>
        /// <param name="window">対象のウィンドウ</param>
        /// <param name="predicate">移動を許可するかどうかの判定関数（オプション）</param>
        public static void EnableWindowMoveShortcut(Window window, Func<bool>? predicate = null)
        {
            TimeSpan lastRenderingTime = TimeSpan.Zero;

            CompositionTarget.Rendering += (s, e) =>
            {
                if (!window.IsVisible || !window.IsActive) return;
                if (IsTextInputFocused(window)) return;
                if (predicate != null && !predicate()) return;

                var args = (RenderingEventArgs)e;
                if (lastRenderingTime == args.RenderingTime) return;

                if (lastRenderingTime == TimeSpan.Zero)
                {
                    lastRenderingTime = args.RenderingTime;
                    return;
                }

                double deltaTime = (args.RenderingTime - lastRenderingTime).TotalSeconds;
                lastRenderingTime = args.RenderingTime;

                if (deltaTime > 0.1) deltaTime = 0.1;

                if (Keyboard.Modifiers == ModifierKeys.Control)
                {
                    double speed = 1200.0;
                    // ViewModelから速度を取得（存在する場合をリフレクションで判定）
                    object? vm = window.DataContext;
                    if (vm == null && window.Owner != null) vm = window.Owner.DataContext;

                    if (vm != null)
                    {
                        var prop = vm.GetType().GetProperty("MovementSpeed");
                        if (prop != null)
                        {
                            speed = (double)prop.GetValue(vm)!;
                        }
                    }

                    double distance = speed * deltaTime;
                    Window target = window.Owner ?? window;

                    bool moved = false;
                    if (Keyboard.IsKeyDown(Key.Left)) { target.Left -= distance; moved = true; }
                    if (Keyboard.IsKeyDown(Key.Right)) { target.Left += distance; moved = true; }
                    if (Keyboard.IsKeyDown(Key.Up)) { target.Top -= distance; moved = true; }
                    if (Keyboard.IsKeyDown(Key.Down)) { target.Top += distance; moved = true; }

                    if (moved && window.Owner != null)
                    {
                        // ダイアログをOwnerの中央に維持する
                        SyncWindowWithOwner(window);
                    }
                }
            };
        }

        /// <summary>
        /// マウスドラッグによるウィンドウ移動を有効化します。
        /// Ownerが存在する場合、Ownerウィンドウを連動させてドラッグ移動させます。
        /// </summary>
        public static void EnableWindowDragMove(Window window)
        {
            Point? offset = null;
            bool isSyncing = false;

            window.MouseLeftButtonDown += (s, e) =>
            {
                if (e.LeftButton == MouseButtonState.Pressed)
                {
                    if (window.Owner != null)
                    {
                        // ドラッグ開始時のダイアログとOwnerのオフセットを記録
                        offset = new Point(window.Left - window.Owner.Left, window.Top - window.Owner.Top);
                    }
                    
                    try
                    {
                        // ダイアログ自身をドラッグ移動させる
                        window.DragMove();
                    }
                    catch (InvalidOperationException)
                    {
                    }
                }
            };

            window.LocationChanged += (s, e) =>
            {
                // ドラッグ中、ダイアログの位置が変わるたびにOwnerを追従させる
                if (isSyncing || window.Owner == null || offset == null) return;

                isSyncing = true;
                try
                {
                    window.Owner.Left = window.Left - offset.Value.X;
                    window.Owner.Top = window.Top - offset.Value.Y;
                }
                finally
                {
                    isSyncing = false;
                }
            };
        }

        private static bool IsTextInputFocused(Window window)
        {
            var focused = Keyboard.FocusedElement;
            return focused is System.Windows.Controls.Primitives.TextBoxBase || focused is PasswordBox;
        }

        private static void SyncWindowWithOwner(Window window)
        {
            if (window.Owner == null) return;
            window.Left = window.Owner.Left + (window.Owner.ActualWidth - window.ActualWidth) / 2;
            window.Top = window.Owner.Top + (window.Owner.ActualHeight - window.ActualHeight) / 2;
        }

        private static void AddHook(Window window)
        {
            var helper = new WindowInteropHelper(window);
            if (helper.Handle == IntPtr.Zero) return;

            var source = HwndSource.FromHwnd(helper.Handle);
            source.AddHook(HwndHook);
        }

        /// <summary>
        /// Windowsメッセージを処理し、システムメニュー要求（SC_KEYMENU）をインターセプトします。
        /// </summary>
        private static IntPtr HwndHook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (msg == 0x0112) // WM_SYSCOMMAND
            {
                // 0xF100 は SC_KEYMENU。AltキーやF10キーによるメニュー表示信号。
                // 下位4ビットはシステム予約のため、マスクして判定。
                if ((wParam.ToInt32() & 0xFFF0) == 0xF100)
                {
                    handled = true;
                }
            }
            return IntPtr.Zero;
        }
    }
}
