using System;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace HotKeyCommandApp.Services
{
    public class GlobalKeyHookService : IDisposable
    {
        private NativeMethods.LowLevelKeyboardProc _proc;
        private IntPtr _hookId = IntPtr.Zero;

        public event Action? EscPressed;

        public GlobalKeyHookService()
        {
            _proc = HookCallback;
            _hookId = SetHook(_proc);
        }

        private IntPtr SetHook(NativeMethods.LowLevelKeyboardProc proc)
        {
            using (Process curProcess = Process.GetCurrentProcess())
            using (ProcessModule curModule = curProcess.MainModule!)
            {
                return NativeMethods.SetWindowsHookEx(NativeMethods.WH_KEYBOARD_LL, proc,
                    NativeMethods.GetModuleHandle(curModule.ModuleName), 0);
            }
        }

        private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode >= 0 && wParam == (IntPtr)NativeMethods.WM_KEYDOWN)
            {
                int vkCode = Marshal.ReadInt32(lParam);
                if (vkCode == 0x1B) // VK_ESCAPE
                {
                    EscPressed?.Invoke();
                }
            }
            return NativeMethods.CallNextHookEx(_hookId, nCode, wParam, lParam);
        }

        public void Dispose()
        {
            NativeMethods.UnhookWindowsHookEx(_hookId);
        }
    }
}
