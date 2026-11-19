using System.Diagnostics;
using System.IO;
using System.Text;

namespace HotKeyCommandApp.Services
{
    public sealed class GitProcessRunner
    {
        private Process? _currentProcess;
        public bool IsRunning => _currentProcess != null;

        static GitProcessRunner()
        {
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        }

        public void Cancel()
        {
            try
            {
                if (_currentProcess != null && !_currentProcess.HasExited)
                {
                    _currentProcess.Kill(entireProcessTree: true);
                }
            }
            catch
            {
                // プロセス終了直後の競合は無視する
            }
        }

        public async Task<int> RunAsync(ProcessStartInfo psi, CancellationToken cancellationToken, Action<string>? onStdOutLine = null, Action<string>? onStdErrLine = null)
        {
            NativeMethods.AllowSetForegroundWindow(NativeMethods.ASFW_ANY);
            using var process = Process.Start(psi);
            if (process == null)
            {
                return -1;
            }

            _currentProcess = process;

            try
            {
                var stdoutTask = ReadProcessStreamAsync(process.StandardOutput.BaseStream, onStdOutLine, cancellationToken);
                var stderrTask = ReadProcessStreamAsync(process.StandardError.BaseStream, onStdErrLine, cancellationToken);
                var exitTask = process.WaitForExitAsync();
                var cancelTask = Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);

                var completedTask = await Task.WhenAny(exitTask, cancelTask);
                if (completedTask == cancelTask)
                {
                    Cancel();

                    try
                    {
                        await exitTask;
                    }
                    catch
                    {
                        // キャンセルに伴う終了待ちの失敗は無視する
                    }

                    cancellationToken.ThrowIfCancellationRequested();
                }

                await Task.WhenAll(stdoutTask, stderrTask);
                await exitTask;
                return process.ExitCode;
            }
            finally
            {
                if (ReferenceEquals(_currentProcess, process))
                {
                    _currentProcess = null;
                }
            }
        }

        private static async Task ReadProcessStreamAsync(Stream stream, Action<string>? onLine, CancellationToken cancellationToken)
        {
            var buffer = new byte[4096];
            var pending = new List<byte>();
            Encoding? encoding = null;

            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();

                int bytesRead = await stream.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken);
                if (bytesRead == 0)
                {
                    break;
                }

                pending.AddRange(buffer.AsSpan(0, bytesRead).ToArray());
                encoding ??= DetectProcessOutputEncoding(pending);

                EmitDecodedLines(pending, encoding, onLine);
            }

            if (pending.Count > 0)
            {
                encoding ??= DetectProcessOutputEncoding(pending);
                string line = DecodeProcessOutputBytes(pending, encoding);
                if (!string.IsNullOrEmpty(line))
                {
                    onLine?.Invoke(line);
                }
            }
        }

        private static void EmitDecodedLines(List<byte> pending, Encoding encoding, Action<string>? onLine)
        {
            int lineStart = 0;

            for (int i = 0; i < pending.Count; i++)
            {
                if (pending[i] != (byte)'\n')
                {
                    continue;
                }

                int length = i - lineStart;
                if (length > 0 && pending[i - 1] == (byte)'\r')
                {
                    length--;
                }

                byte[] lineBytes = pending.GetRange(lineStart, Math.Max(length, 0)).ToArray();
                string line = DecodeProcessOutputBytes(lineBytes, encoding);
                onLine?.Invoke(line);
                lineStart = i + 1;
            }

            if (lineStart > 0)
            {
                pending.RemoveRange(0, lineStart);
            }
        }

        private static string DecodeProcessOutputBytes(IReadOnlyList<byte> bytes, Encoding encoding)
        {
            if (bytes.Count == 0)
            {
                return string.Empty;
            }

            byte[] buffer = bytes as byte[] ?? bytes.ToArray();
            return encoding.GetString(buffer);
        }

        private static Encoding DetectProcessOutputEncoding(IReadOnlyList<byte> bytes)
        {
            if (bytes.Count >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
            {
                return new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
            }

            return IsValidUtf8(bytes)
                ? new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true)
                : Encoding.GetEncoding(932);
        }

        private static bool IsValidUtf8(IReadOnlyList<byte> bytes)
        {
            int remaining = 0;

            foreach (byte value in bytes)
            {
                if (remaining == 0)
                {
                    if ((value & 0b1000_0000) == 0)
                    {
                        continue;
                    }

                    if ((value & 0b1110_0000) == 0b1100_0000)
                    {
                        remaining = 1;
                        continue;
                    }

                    if ((value & 0b1111_0000) == 0b1110_0000)
                    {
                        remaining = 2;
                        continue;
                    }

                    if ((value & 0b1111_1000) == 0b1111_0000)
                    {
                        remaining = 3;
                        continue;
                    }

                    return false;
                }

                if ((value & 0b1100_0000) != 0b1000_0000)
                {
                    return false;
                }

                remaining--;
            }

            return remaining == 0;
        }

    }
}
