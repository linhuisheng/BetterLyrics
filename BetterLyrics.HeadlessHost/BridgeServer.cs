using BetterLyrics.WinUI3.Enums;
using BetterLyrics.WinUI3.Models;
using BetterLyrics.WinUI3.Models.Lyrics;
using BetterLyrics.WinUI3.Services.GSMTCService;
using BetterLyrics.WinUI3.Services.LyricsSearchService;
using System;
using System.Buffers.Binary;
using System.ComponentModel;
using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace BetterLyrics.HeadlessHost
{
    public sealed class BridgeServer
    {
        private const string PipeName = "BetterLyrics.HeadlessBridge";
        private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
        private static readonly TimeSpan PositionThrottle = TimeSpan.FromMilliseconds(500);

        private readonly IGSMTCService _gsmtcService;
        private readonly ILyricsSearchService _lyricsSearchService;
        private readonly SemaphoreSlim _writeLock = new(1, 1);
        private long _lastPositionTicks;

        public BridgeServer(IGSMTCService gsmtcService, ILyricsSearchService lyricsSearchService)
        {
            _gsmtcService = gsmtcService;
            _lyricsSearchService = lyricsSearchService;
        }

        public async Task RunAsync(CancellationToken token)
        {
            while (!token.IsCancellationRequested)
            {
                using var pipe = new NamedPipeServerStream(
                    PipeName,
                    PipeDirection.InOut,
                    1,
                    PipeTransmissionMode.Byte,
                    PipeOptions.Asynchronous);

                await pipe.WaitForConnectionAsync(token);

                using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(token);
                var handler = new PropertyChangedEventHandler((_, args) => OnPropertyChanged(args, pipe, linkedCts.Token));
                _gsmtcService.PropertyChanged += handler;

                await PublishSnapshotAsync(pipe, linkedCts.Token);

                try
                {
                    await ReadLoopAsync(pipe, linkedCts.Token);
                }
                catch (IOException)
                {
                    // Client disconnected.
                }
                finally
                {
                    _gsmtcService.PropertyChanged -= handler;
                }
            }
        }

        private async Task ReadLoopAsync(Stream pipe, CancellationToken token)
        {
            while (!token.IsCancellationRequested && pipe is NamedPipeServerStream server && server.IsConnected)
            {
                var message = await ReadMessageAsync(pipe, token);
                if (message == null)
                {
                    break;
                }

                await HandleMessageAsync(message, pipe, token);
            }
        }

        private async Task HandleMessageAsync(string message, Stream pipe, CancellationToken token)
        {
            using var document = JsonDocument.Parse(message);
            var root = document.RootElement;

            if (!root.TryGetProperty("type", out var typeElement))
            {
                return;
            }

            var type = typeElement.GetString();
            if (type != "search")
            {
                return;
            }

            if (!root.TryGetProperty("id", out var idElement) || !root.TryGetProperty("payload", out var payload))
            {
                return;
            }

            var title = payload.TryGetProperty("title", out var titleElement) ? titleElement.GetString() ?? string.Empty : string.Empty;
            var artist = payload.TryGetProperty("artist", out var artistElement) ? artistElement.GetString() ?? string.Empty : string.Empty;
            var album = payload.TryGetProperty("album", out var albumElement) ? albumElement.GetString() ?? string.Empty : string.Empty;

            LyricsSearchSnapshot responsePayload;
            try
            {
                var songInfo = new SongInfo
                {
                    Title = title,
                    Artist = artist,
                    Album = album,
                };
                var result = await _lyricsSearchService.SearchSmartlyAsync(songInfo, LyricsSearchType.BestMatch, CancellationToken.None);
                responsePayload = result == null
                    ? new LyricsSearchSnapshot { Found = false }
                    : new LyricsSearchSnapshot
                    {
                        Found = result.IsFound,
                        Provider = result.ProviderIfFound?.ToString(),
                        MatchPercentage = result.MatchPercentage,
                        Title = result.Title,
                        Artist = result.Artist,
                        Album = result.Album,
                        DurationSeconds = result.Duration,
                        Raw = result.Raw,
                        Translation = result.Translation,
                        Transliteration = result.Transliteration,
                    };
            }
            catch (Exception ex)
            {
                responsePayload = new LyricsSearchSnapshot
                {
                    Found = false,
                    Error = ex.Message
                };
            }

            var responseJson = JsonSerializer.Serialize(new
            {
                type = "search_response",
                id = idElement.GetString(),
                payload = responsePayload
            }, JsonOptions);

            await WriteMessageAsync(pipe, responseJson, token);
        }

        private void OnPropertyChanged(PropertyChangedEventArgs args, Stream pipe, CancellationToken token)
        {
            if (args.PropertyName == nameof(IGSMTCService.CurrentPosition))
            {
                var nowTicks = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                var previous = Interlocked.Read(ref _lastPositionTicks);
                if (nowTicks - previous < PositionThrottle.TotalMilliseconds)
                {
                    return;
                }
                Interlocked.Exchange(ref _lastPositionTicks, nowTicks);
            }

            if (args.PropertyName is nameof(IGSMTCService.CurrentSongInfo)
                or nameof(IGSMTCService.CurrentLyricsData)
                or nameof(IGSMTCService.CurrentPosition)
                or nameof(IGSMTCService.CurrentIsPlaying))
            {
                _ = PublishSnapshotAsync(pipe, token);
            }
        }

        private async Task PublishSnapshotAsync(Stream pipe, CancellationToken token)
        {
            var snapshot = BuildSnapshot(_gsmtcService);
            var eventJson = JsonSerializer.Serialize(new
            {
                type = "event",
                payload = snapshot
            }, JsonOptions);

            await WriteMessageAsync(pipe, eventJson, token);
        }

        private static PlaybackSnapshot BuildSnapshot(IGSMTCService gsmtcService)
        {
            var songInfo = gsmtcService.CurrentSongInfo;
            var lyricsData = gsmtcService.CurrentLyricsData;
            var lyricsCache = gsmtcService.CurrentLyricsSearchResult;
            var positionSeconds = gsmtcService.CurrentPosition.TotalSeconds;

            var lineSnapshot = GetCurrentLine(lyricsData, positionSeconds);

            return new PlaybackSnapshot
            {
                PlayerId = songInfo.PlayerId,
                Title = songInfo.Title,
                Artist = songInfo.Artist,
                Album = songInfo.Album,
                IsPlaying = gsmtcService.CurrentIsPlaying,
                PositionSeconds = positionSeconds,
                DurationSeconds = songInfo.Duration,
                LyricsText = lyricsData?.WrappedOriginalText,
                LyricsRaw = lyricsCache?.Raw,
                Translation = lyricsCache?.Translation,
                Transliteration = lyricsCache?.Transliteration,
                CurrentLineIndex = lineSnapshot?.Index,
                CurrentLineText = lineSnapshot?.Line?.PrimaryText,
                CurrentLineStartMs = lineSnapshot?.Line?.StartMs,
                CurrentLineEndMs = lineSnapshot?.Line?.EndMs,
            };
        }

        private static (int Index, LyricsLine Line)? GetCurrentLine(LyricsData? lyricsData, double positionSeconds)
        {
            if (lyricsData?.LyricsLines == null || lyricsData.LyricsLines.Count == 0)
            {
                return null;
            }

            var positionMs = positionSeconds * 1000;
            var lines = lyricsData.LyricsLines;
            for (int i = 0; i < lines.Count; i++)
            {
                var line = lines[i];
                if (line.StartMs <= positionMs && positionMs <= line.EndMs)
                {
                    return (i, line);
                }

                if (line.StartMs > positionMs)
                {
                    return i > 0 ? (i - 1, lines[i - 1]) : (0, lines[0]);
                }
            }

            return (lines.Count - 1, lines[^1]);
        }

        private async Task WriteMessageAsync(Stream stream, string message, CancellationToken token)
        {
            var payload = Encoding.UTF8.GetBytes(message);
            Span<byte> lengthPrefix = stackalloc byte[sizeof(int)];
            BinaryPrimitives.WriteInt32LittleEndian(lengthPrefix, payload.Length);

            await _writeLock.WaitAsync(token);
            try
            {
                await stream.WriteAsync(lengthPrefix, token);
                await stream.WriteAsync(payload, token);
                await stream.FlushAsync(token);
            }
            finally
            {
                _writeLock.Release();
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
