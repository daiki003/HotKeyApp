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
            this.ShutdownMode = ShutdownMode.OnExplicitShutdown;
            base.OnStartup(e);

            // Register for startup
            var startupService = new StartupService();
            startupService.Register();

            // The MainWindow will be shown automatically because of StartupUri in App.xaml
        }
    }
}

