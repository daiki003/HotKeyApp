using System;
using System.Windows;
using System.Windows.Interop;

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
