using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace HotKeyCommandApp.Services
{
    public class HotKeyService : IDisposable
    {
        [DllImport("user32.dll")]
        private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

        [DllImport("user32.dll")]
        private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

        private const uint MOD_ALT = 0x0001;
        private const uint MOD_WIN = 0x0008;
        private const int DEFAULT_HOTKEY_ID = 9000;

        private IntPtr _hWnd;
        public event Action<int>? HotKeyPressed;

        private readonly System.Collections.Generic.Dictionary<int, string> _registeredHotKeys = new();

        public void Register(Window window, string hotkeyString)
        {
            Register(window, DEFAULT_HOTKEY_ID, hotkeyString);
        }

        public void Register(Window window, int id, string hotkeyString)
        {
            var helper = new WindowInteropHelper(window);
            _hWnd = helper.Handle;

            var source = HwndSource.FromHwnd(_hWnd);
            source.RemoveHook(HwndHook); // Ensure only one hook
            source.AddHook(HwndHook);

            UpdateHotKey(id, hotkeyString);
        }

        public void UpdateHotKey(string hotkeyString)
        {
            UpdateHotKey(DEFAULT_HOTKEY_ID, hotkeyString);
        }

        public void UpdateHotKey(int id, string hotkeyString)
        {
            if (_hWnd == IntPtr.Zero) return;

            UnregisterHotKey(_hWnd, id);
            _registeredHotKeys.Remove(id);

            if (string.IsNullOrWhiteSpace(hotkeyString)) return;

            uint modifiers = 0;
            uint vk = 0;

            var parts = hotkeyString.Split('+');
            foreach (var part in parts)
            {
                string p = part.Trim().ToUpper();
                if (p == "WIN" || p == "WINDOWS") modifiers |= 0x0008;
                else if (p == "ALT") modifiers |= 0x0001;
                else if (p == "CTRL" || p == "CONTROL") modifiers |= 0x0002;
                else if (p == "SHIFT") modifiers |= 0x0004;
                else
                {
                    // parse key
                    if (Enum.TryParse<System.Windows.Input.Key>(part.Trim(), true, out var key))
                    {
                        vk = (uint)System.Windows.Input.KeyInterop.VirtualKeyFromKey(key);
                    }
                }
            }

            if (vk == 0 && id == DEFAULT_HOTKEY_ID)
            {
                modifiers = MOD_WIN | MOD_ALT;
                vk = 0x5A; // 'Z'
            }

            if (vk != 0)
            {
                if (RegisterHotKey(_hWnd, id, modifiers, vk))
                {
                    _registeredHotKeys[id] = hotkeyString;
                }
            }
        }

        public void Unregister(int id)
        {
            if (_hWnd != IntPtr.Zero)
            {
                UnregisterHotKey(_hWnd, id);
                _registeredHotKeys.Remove(id);
            }
        }

        public void UnregisterAll()
        {
            if (_hWnd != IntPtr.Zero)
            {
                foreach (var id in new System.Collections.Generic.List<int>(_registeredHotKeys.Keys))
                {
                    UnregisterHotKey(_hWnd, id);
                }
                _registeredHotKeys.Clear();
            }
        }

        public void UnregisterCommandHotkeys()
        {
            if (_hWnd != IntPtr.Zero)
            {
                var ids = new System.Collections.Generic.List<int>(_registeredHotKeys.Keys);
                foreach (var id in ids)
                {
                    if (id != DEFAULT_HOTKEY_ID)
                    {
                        UnregisterHotKey(_hWnd, id);
                        _registeredHotKeys.Remove(id);
                    }
                }
            }
        }

        public bool IsEnabled { get; set; } = true;

        private IntPtr HwndHook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            const int WM_HOTKEY = 0x0312;
            if (IsEnabled && msg == WM_HOTKEY)
            {
                int id = wParam.ToInt32();
                // 常にデフォルトIDは受け入れるか、辞書に存在する場合に処理する
                if (id == DEFAULT_HOTKEY_ID || _registeredHotKeys.ContainsKey(id))
                {
                    HotKeyPressed?.Invoke(id);
                    handled = true;
                }
            }
            return IntPtr.Zero;
        }

        public void Dispose()
        {
            UnregisterAll();
        }
    }
}
