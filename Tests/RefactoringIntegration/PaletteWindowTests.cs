using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Threading;
using HotKeyCommandApp.Models;
using HotKeyCommandApp.Services;
using HotKeyCommandApp.Views;
using static TestSupport;

internal static class PaletteWindowTests
{
    [DllImport("user32.dll")]
    private static extern IntPtr SendMessage(IntPtr window, int message, IntPtr parameter, IntPtr data);

    public static async Task RunAsync()
    {
        var config = new ConfigService();
        config.SaveSettings(new AppSettings { GlobalHotkey = "Ctrl+Alt+Shift+F24" });
        config.SaveCommands(new List<CommandEntry> { new() { Name = "test", Category = CommandCategory.Hierarchy } });

        var resources = Application.Current.Resources;
        object? previousStyle = resources.Contains(typeof(ListBoxItem)) ? resources[typeof(ListBoxItem)] : null;
        resources[typeof(ListBoxItem)] = new Style(typeof(ListBoxItem));
        var window = new PaletteWindow();
        try
        {
            window.Show();
            await Dispatcher.Yield(DispatcherPriority.ContextIdle);
            var handle = new WindowInteropHelper(window).Handle;
            Check(handle != IntPtr.Zero && window.IsVisible, "パレットの起動と表示");

            window.Hide();
            NotifyHotkey(handle);
            await Dispatcher.Yield(DispatcherPriority.ContextIdle);
            Check(window.IsVisible && window.WindowState == WindowState.Normal, "ホットキー通知で非表示のパレットを再表示");

            window.WindowState = WindowState.Minimized;
            await Dispatcher.Yield(DispatcherPriority.ContextIdle);
            Check(window.WindowState == WindowState.Minimized, "パレットの最小化を再現");
            NotifyHotkey(handle);
            await Dispatcher.Yield(DispatcherPriority.ContextIdle);
            Check(window.IsVisible && window.WindowState == WindowState.Normal, "ホットキー通知で最小化されたパレットを復元");

            window.WindowState = WindowState.Minimized;
            window.Hide();
            NotifyHotkey(handle);
            await Dispatcher.Yield(DispatcherPriority.ContextIdle);
            Check(window.IsVisible && window.WindowState == WindowState.Normal, "非表示かつ最小化されたパレットを復元");

            window.Close();
            await Dispatcher.Yield(DispatcherPriority.ContextIdle);
            Check(window.IsLoaded && !window.IsVisible, "閉じる操作でも常駐用ウィンドウを破棄しない");
            NotifyHotkey(handle);
            await Dispatcher.Yield(DispatcherPriority.ContextIdle);
            Check(window.IsVisible, "閉じる操作の後もホットキーで再表示");

            window.Hide();
            window.ShowPalette();
            await Dispatcher.Yield(DispatcherPriority.ContextIdle);
            Check(window.IsVisible && window.WindowState == WindowState.Normal, "外部からの表示要求で非表示から復帰");

            window.WindowState = WindowState.Minimized;
            window.ShowPalette();
            await Dispatcher.Yield(DispatcherPriority.ContextIdle);
            Check(window.IsVisible && window.WindowState == WindowState.Normal, "外部からの表示要求で最小化から復帰");
        }
        finally
        {
            window.Close();
            if (previousStyle == null) resources.Remove(typeof(ListBoxItem));
            else resources[typeof(ListBoxItem)] = previousStyle;
        }
    }

    private static void NotifyHotkey(IntPtr handle)
    {
        // 実キー入力を送らず、同プロセス内でホットキー通知からの処理を検証する。
        SendMessage(handle, 0x0312, new IntPtr(9000), IntPtr.Zero);
    }
}
