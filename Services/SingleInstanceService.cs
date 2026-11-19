using System.Threading;

namespace HotKeyCommandApp.Services
{
    public sealed class SingleInstanceService : IDisposable
    {
        private readonly Mutex _mutex;
        private readonly EventWaitHandle _showRequested;
        private RegisteredWaitHandle? _listener;
        private volatile bool _disposed;

        public SingleInstanceService(string name = "HotKeyCommandApp")
        {
            _mutex = new Mutex(false, @"Local\" + name + ".Instance");
            _showRequested = new EventWaitHandle(false, EventResetMode.AutoReset, @"Local\" + name + ".Show");
            try
            {
                IsPrimary = _mutex.WaitOne(0);
            }
            catch (AbandonedMutexException)
            {
                // 前のプロセスが強制終了されていても、今回の起動で引き継ぐ。
                IsPrimary = true;
            }
        }

        public bool IsPrimary { get; }

        public void ListenForShowRequests(Action show)
        {
            if (!IsPrimary) throw new InvalidOperationException("受信は常駐側のプロセスでのみ開始できます。");
            if (_listener != null) throw new InvalidOperationException("表示要求の受信は開始済みです。");
            _listener = ThreadPool.RegisterWaitForSingleObject(_showRequested, (_, _) =>
            {
                if (!_disposed) show();
            }, null, Timeout.Infinite, executeOnlyOnce: false);
        }

        public void RequestShow() => _showRequested.Set();

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _listener?.Unregister(null);
            _showRequested.Dispose();
            if (IsPrimary) _mutex.ReleaseMutex();
            _mutex.Dispose();
        }
    }
}
