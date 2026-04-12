using System.Windows;
using HotKeyCommandApp.Services;
using System.Configuration;
using System.Data;

namespace HotKeyCommandApp
{
    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            this.DispatcherUnhandledException += App_DispatcherUnhandledException;
            AppDomain.CurrentDomain.UnhandledException += CurrentDomain_UnhandledException;

            this.ShutdownMode = ShutdownMode.OnExplicitShutdown;
            base.OnStartup(e);

            // Register for startup
            var startupService = new StartupService();
            startupService.Register();

            // The MainWindow will be shown automatically because of StartupUri in App.xaml
        }

        private void App_DispatcherUnhandledException(object sender, System.Windows.Threading.DispatcherUnhandledExceptionEventArgs e)
        {
            LogException(e.Exception, "DispatcherUnhandledException");
            // 処理済みとして扱うことでアプリの強制終了を試行的に防ぐ（継続不可能な場合は結局落ちるがログは残る）
            e.Handled = true; 
        }

        private void CurrentDomain_UnhandledException(object sender, UnhandledExceptionEventArgs e)
        {
            if (e.ExceptionObject is Exception ex)
            {
                LogException(ex, "AppDomainUnhandledException");
            }
        }

        private void LogException(Exception ex, string type)
        {
            try
            {
                string logPath = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "crash_report.txt");
                string message = $"[{DateTime.Now}] {type}:\n{ex.ToString()}\n\n";
                System.IO.File.AppendAllText(logPath, message);
                
                MessageBox.Show($"問題が発生しました。詳細は {logPath} を確認してください。\n\nエラー内容: {ex.Message}", "エラー", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            catch
            {
                // ログの書き込み自体に失敗した場合はどうしようもない
            }
        }
    }
}

