using System;
using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.IO;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace BetterLyrics.Bridge
{
    internal static class BridgeState
    {
        private const string PipeName = "BetterLyrics.HeadlessBridge";
        private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
        private static readonly SemaphoreSlim WriteLock = new(1, 1);
        private static readonly ConcurrentDictionary<string, TaskCompletionSource<string>> PendingRequests = new();

        private static NamedPipeClientStream? _pipe;
        private static Task? _readerTask;
        private static CancellationTokenSource? _readerCts;
        private static bool _eventsEnabled;

        internal static unsafe delegate* unmanaged<IntPtr, int, void> Callback;

        internal static void EnsureConnected()
        {
            if (_pipe is { IsConnected: true })
            {
                return;
            }

            _readerCts?.Cancel();
            _readerCts?.Dispose();
            _pipe?.Dispose();

            _pipe = new NamedPipeClientStream(".", PipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
            _pipe.Connect(2000);

            _readerCts = new CancellationTokenSource();
            _readerTask = Task.Run(() => ReadLoopAsync(_pipe, _readerCts.Token), _readerCts.Token);
        }

        internal static void EnableEvents(bool enabled)
        {
            _eventsEnabled = enabled;
        }

        internal static void Stop()
        {
            _eventsEnabled = false;
            _readerCts?.Cancel();
        }

        internal static BridgeBuffer SearchLyrics(string title, string artist, string album)
        {
            EnsureConnected();

            var requestId = Guid.NewGuid().ToString("N");
            var requestJson = JsonSerializer.Serialize(new
            {
                type = "search",
                id = requestId,
                payload = new
                {
                    title,
                    artist,
                    album
                }
            }, JsonOptions);

            var tcs = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
            PendingRequests[requestId] = tcs;

            WriteMessageAsync(_pipe!, requestJson, CancellationToken.None).GetAwaiter().GetResult();

            var payloadJson = tcs.Task.Wait(TimeSpan.FromSeconds(10))
                ? tcs.Task.Result
                : "{\"found\":false,\"error\":\"timeout\"}";

            PendingRequests.TryRemove(requestId, out _);
            return SerializeToBuffer(payloadJson);
        }

        internal static BridgeBuffer SerializeToBuffer(string json)
        {
            var data = Marshal.StringToCoTaskMemUTF8(json);
            return new BridgeBuffer(data, Encoding.UTF8.GetByteCount(json));
        }

        private static async Task ReadLoopAsync(NamedPipeClientStream pipe, CancellationToken token)
        {
            while (!token.IsCancellationRequested && pipe.IsConnected)
            {
                string? message;
                try
                {
                    message = await ReadMessageAsync(pipe, token);
                }
                catch (IOException)
                {
                    break;
                }

                if (message == null)
                {
                    break;
                }

                HandleMessage(message);
            }
        }

        private static void HandleMessage(string message)
        {
            using var document = JsonDocument.Parse(message);
            var root = document.RootElement;

            if (!root.TryGetProperty("type", out var typeElement))
            {
                return;
            }

            var type = typeElement.GetString();
            switch (type)
            {
                case "event":
                    if (!_eventsEnabled)
                    {
                        return;
                    }

                    if (root.TryGetProperty("payload", out var payload))
                    {
                        PublishEvent(payload.GetRawText());
                    }
                    break;
                case "search_response":
                    if (root.TryGetProperty("id", out var idElement) &&
                        root.TryGetProperty("payload", out var responsePayload))
                    {
                        var id = idElement.GetString();
                        if (id != null && PendingRequests.TryGetValue(id, out var tcs))
                        {
                            tcs.TrySetResult(responsePayload.GetRawText());
                        }
                    }
                    break;
            }
        }

        private static void PublishEvent(string payloadJson)
        {
            unsafe
            {
                if (Callback == null)
                {
                    return;
                }

                var buffer = SerializeToBuffer(payloadJson);
                Callback(buffer.Data, buffer.Length);
            }
        }

        private static async Task WriteMessageAsync(Stream stream, string message, CancellationToken token)
        {
            var payload = Encoding.UTF8.GetBytes(message);
            Span<byte> lengthPrefix = stackalloc byte[sizeof(int)];
            BinaryPrimitives.WriteInt32LittleEndian(lengthPrefix, payload.Length);

            await WriteLock.WaitAsync(token);
            try
            {
                await stream.WriteAsync(lengthPrefix, token);
                await stream.WriteAsync(payload, token);
                await stream.FlushAsync(token);
            }
            finally
            {
                WriteLock.Release();
            }
        }

        private static async Task<string?> ReadMessageAsync(Stream stream, CancellationToken token)
        {
            var lengthBuffer = new byte[sizeof(int)];
            var read = await FillBufferAsync(stream, lengthBuffer, token);
            if (read == 0)
            {
                return null;
            }

            var length = BinaryPrimitives.ReadInt32LittleEndian(lengthBuffer);
            if (length <= 0)
            {
                return null;
            }

            var payloadBuffer = new byte[length];
            read = await FillBufferAsync(stream, payloadBuffer, token);
            if (read == 0)
            {
                return null;
            }

            return Encoding.UTF8.GetString(payloadBuffer);
        }

        private static async Task<int> FillBufferAsync(Stream stream, byte[] buffer, CancellationToken token)
        {
            int offset = 0;
            while (offset < buffer.Length)
            {
                var read = await stream.ReadAsync(buffer.AsMemory(offset, buffer.Length - offset), token);
                if (read == 0)
                {
                    return 0;
                }
                offset += read;
            }

            return offset;
        }
    }
}
