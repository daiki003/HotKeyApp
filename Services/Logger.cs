using System;
using System.IO;

namespace HotKeyCommandApp.Services
{
    public static class Logger
    {
        private static readonly string LogPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "app_debug.log");

        public static void Log(string message)
        {
            try
            {
                string timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff");
                File.AppendAllText(LogPath, $"[{timestamp}] {message}{Environment.NewLine}");
            }
            catch { }
        }
    }
}
