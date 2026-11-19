using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Text;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        if (args.Length == 1) return RunChildProcess(args[0]);

        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        app.Resources.MergedDictionaries.Add(new ResourceDictionary
        {
            Source = new Uri("/HotKeyCommandApp;component/Views/Controls/SettingsSharedResources.xaml", UriKind.Relative)
        });
        string workspace = Path.Combine(Path.GetTempPath(), "HotKeyRefactorTests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(workspace);
        app.Startup += async (_, _) =>
        {
            int exitCode = 0;
            try
            {
                await GitCommandTests.RunAsync(workspace);
                await PaletteTests.RunAsync(workspace);
                await SingleInstanceTests.RunAsync();
                await PairEditorTests.RunAsync();
                await PaletteWindowTests.RunAsync();
                Console.WriteLine("すべてのリファクタリング回帰テストに成功しました。");
            }
            catch (Exception exception)
            {
                Console.Error.WriteLine(exception);
                exitCode = 1;
            }
            finally
            {
                foreach (Window window in app.Windows.Cast<Window>().ToArray()) window.Close();
                string expectedPrefix = Path.Combine(Path.GetFullPath(Path.GetTempPath()), "HotKeyRefactorTests-");
                if (Path.GetFullPath(workspace).StartsWith(expectedPrefix, StringComparison.OrdinalIgnoreCase))
                {
                    foreach (string file in Directory.EnumerateFiles(workspace, "*", SearchOption.AllDirectories))
                        File.SetAttributes(file, FileAttributes.Normal);
                    Directory.Delete(workspace, recursive: true);
                }
                app.Shutdown(exitCode);
            }
        };
        return app.Run();
    }

    private static int RunChildProcess(string mode)
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        if (mode == "--wait")
        {
            Console.WriteLine(Environment.ProcessId);
            Console.Out.Flush();
            Thread.Sleep(30000);
            return 0;
        }
        if (mode is not ("--utf8" or "--sjis")) return 2;
        Encoding encoding = mode == "--sjis" ? Encoding.GetEncoding(932) : new UTF8Encoding(false);
        using var stdout = Console.OpenStandardOutput();
        using var stderr = Console.OpenStandardError();
        stdout.Write(encoding.GetBytes("日本語\r\n\r\nlast"));
        stderr.Write(encoding.GetBytes("エラー\r\n"));
        return 7;
    }
}

internal static class TestSupport
{
    public static void Check(bool condition, string name)
    {
        if (!condition) throw new InvalidOperationException("失敗: " + name);
        Console.WriteLine("成功: " + name);
    }

    public static async Task WaitUntilAsync(Func<bool> condition)
    {
        var watch = Stopwatch.StartNew();
        while (!condition())
        {
            if (watch.Elapsed > TimeSpan.FromSeconds(10)) throw new TimeoutException("状態の更新が完了しませんでした。");
            await Task.Delay(20);
        }
    }

    public static object? Invoke(object target, string method, params object[] args) =>
        (target.GetType().GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new MissingMethodException(method)).Invoke(target, args);

    public static void SavePreview(FrameworkElement element, string fileName)
    {
        var bitmap = new RenderTargetBitmap((int)Math.Ceiling(element.ActualWidth),
            (int)Math.Ceiling(element.ActualHeight), 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(element);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var output = File.Create(Path.Combine(AppContext.BaseDirectory, fileName));
        encoder.Save(output);
    }
}
