using System;
using System.IO;

namespace HotKeyCommandApp.Services
{
    public static class Logger
    {
        private static readonly object _sync = new();
        private static readonly string LogDirectory = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "logs");
        private static readonly string LogPath = Path.Combine(LogDirectory, "app_debug.log");

        public static void Log(string message)
        {
            try
            {
                string timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff");
                lock (_sync)
                {
                    Directory.CreateDirectory(LogDirectory);
                    File.AppendAllText(LogPath, $"[{timestamp}] {message}{Environment.NewLine}");
                }
            }
            catch { }
        }
    }
}
