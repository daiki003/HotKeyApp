using System;
using Microsoft.Win32;
using System.IO;

namespace HotKeyCommandApp.Services
{
    public class StartupService
    {
        private const string AppName = "HotKeyCommandApp";
        private readonly string _exePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "HotKeyCommandApp.exe");

        public void Register()
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Run", true);
                if (key != null)
                {
                    key.SetValue(AppName, $"\"{_exePath}\"");
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to register startup: {ex.Message}");
            }
        }

        public void Unregister()
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Run", true);
                key?.DeleteValue(AppName, false);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to unregister startup: {ex.Message}");
            }
        }
    }
}
