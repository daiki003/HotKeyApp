using HotKeyCommandApp.Services;
using static TestSupport;

internal static class SingleInstanceTests
{
    public static async Task RunAsync()
    {
        string name = "HotKeyAppTest-" + Guid.NewGuid().ToString("N");
        var requested = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using (var primary = new SingleInstanceService(name))
        {
            Check(primary.IsPrimary, "最初の起動だけが常駐を開始");
            primary.ListenForShowRequests(() => requested.TrySetResult());
            await Task.Run(() =>
            {
                using var secondary = new SingleInstanceService(name);
                Check(!secondary.IsPrimary, "重複起動は常駐とホットキー登録を増やさない");
                secondary.RequestShow();
            });
            await requested.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Check(requested.Task.IsCompletedSuccessfully, "重複起動から既存プロセスへ表示を要求");
        }

        using var restarted = new SingleInstanceService(name);
        Check(restarted.IsPrimary, "終了後は同じ識別子で起動し直せる");
    }
}
