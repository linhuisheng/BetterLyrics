using BetterLyrics.WinUI3.Helper;
using BetterLyrics.WinUI3.Services.GSMTCService;
using BetterLyrics.WinUI3.Services.LyricsSearchService;
using CommunityToolkit.Mvvm.DependencyInjection;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace BetterLyrics.HeadlessHost
{
    internal static class Program
    {
        private static readonly ManualResetEventSlim ShutdownEvent = new(false);

        private static async Task<int> Main(string[] args)
        {
            Console.CancelKeyPress += OnCancelKeyPress;

            HeadlessInitializer.EnsureDispatcherQueue();
            ServiceBootstrapper.EnsureInitialized();
            await HeadlessInitializer.InitializeDatabasesAsync();
            await HeadlessInitializer.InitializeBackgroundServicesAsync();

            var gsmtcService = Ioc.Default.GetRequiredService<IGSMTCService>();
            var lyricsSearchService = Ioc.Default.GetRequiredService<ILyricsSearchService>();

            var bridgeServer = new BridgeServer(gsmtcService, lyricsSearchService);
            _ = bridgeServer.RunAsync(CancellationToken.None);

            Console.WriteLine("BetterLyrics Headless Host is running. Press Ctrl+C to exit.");
            ShutdownEvent.Wait();

            return 0;
        }

        private static void OnCancelKeyPress(object? sender, ConsoleCancelEventArgs e)
        {
            e.Cancel = true;
            ShutdownEvent.Set();
        }
    }
}
