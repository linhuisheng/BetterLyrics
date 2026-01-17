// 2025/6/23 by Zhe Fang

using BetterLyrics.WinUI3.Models.DbContext;
using BetterLyrics.WinUI3.Services.AlbumArtSearchService;
using BetterLyrics.WinUI3.Services.DiscordService;
using BetterLyrics.WinUI3.Services.FileSystemService;
using BetterLyrics.WinUI3.Services.GSMTCService;
using BetterLyrics.WinUI3.Services.LastFMService;
using BetterLyrics.WinUI3.Services.LocalizationService;
using BetterLyrics.WinUI3.Services.LyricsCacheService;
using BetterLyrics.WinUI3.Services.LyricsSearchService;
using BetterLyrics.WinUI3.Services.PlayHistoryService;
using BetterLyrics.WinUI3.Services.PluginService;
using BetterLyrics.WinUI3.Services.SettingsService;
using BetterLyrics.WinUI3.Services.SMTCService;
using BetterLyrics.WinUI3.Services.SongSearchMapService;
using BetterLyrics.WinUI3.Services.TranslationService;
using BetterLyrics.WinUI3.Services.TransliterationService;
using BetterLyrics.WinUI3.ViewModels;
using CommunityToolkit.Mvvm.DependencyInjection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Serilog;
using System.Text;

namespace BetterLyrics.WinUI3.Helper
{
    public static class ServiceBootstrapper
    {
        private static bool _initialized;

        public static void EnsureInitialized()
        {
            if (_initialized)
            {
                return;
            }

            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            PathHelper.EnsureDirectories();
            ConfigureServices();
            _initialized = true;
        }

        public static void ConfigureServices()
        {
            Log.Logger = new LoggerConfiguration()
                .MinimumLevel.Is(Serilog.Events.LogEventLevel.Verbose)
                .MinimumLevel.Override("Microsoft.EntityFrameworkCore", Serilog.Events.LogEventLevel.Error)
                .WriteTo.File(PathHelper.LogFilePattern, rollingInterval: RollingInterval.Day)
                .CreateLogger();

            Ioc.Default.ConfigureServices(
                new ServiceCollection()
                    // 数据库工厂
                    .AddDbContextFactory<PlayHistoryDbContext>(options => options.UseSqlite($"Data Source={PathHelper.PlayHistoryPath}"))
                    .AddDbContextFactory<FilesIndexDbContext>(options => options.UseSqlite($"Data Source={PathHelper.FilesIndexPath}"))
                    .AddDbContextFactory<LyricsCacheDbContext>(options => options.UseSqlite($"Data Source={PathHelper.LyricsCachePath}"))
                    .AddDbContextFactory<SongSearchMapDbContext>(options => options.UseSqlite($"Data Source={PathHelper.SongSearchMapPath}"))

                    // 日志
                    .AddLogging(loggingBuilder =>
                    {
                        loggingBuilder.ClearProviders();
                        loggingBuilder.AddSerilog();
                    })

                    // Services
                    .AddSingleton<ISettingsService, SettingsService>()
                    .AddSingleton<ISMTCService, SMTCService>()
                    .AddSingleton<IGSMTCService, GSMTCService>()
                    .AddSingleton<IAlbumArtSearchService, AlbumArtSearchService>()
                    .AddSingleton<ILyricsSearchService, LyricsSearchService>()
                    .AddSingleton<ITranslationService, TranslationService>()
                    .AddSingleton<ITransliterationService, TransliterationService>()
                    .AddSingleton<ILastFMService, LastFMService>()
                    .AddSingleton<IDiscordService, DiscordService>()
                    .AddSingleton<ILocalizationService, LocalizationService>()
                    .AddSingleton<IFileSystemService, FileSystemService>()
                    .AddSingleton<IPlayHistoryService, PlayHistoryService>()
                    .AddSingleton<ILyricsCacheService, LyricsCacheService>()
                    .AddSingleton<ISongSearchMapService, SongSearchMapService>()
                    .AddSingleton<IPluginService, PluginService>()

                    // ViewModels
                    .AddSingleton<AppSettingsControlViewModel>()
                    .AddSingleton<PlaybackSettingsControlViewModel>()
                    .AddSingleton<MediaSettingsControlViewModel>()
                    .AddSingleton<LyricsSearchControlViewModel>()
                    .AddSingleton<LyricsWindowSettingsControlViewModel>()
                    .AddSingleton<LyricsWindowSwitchControlViewModel>()
                    .AddSingleton<LyricsWindowSwitchWindowViewModel>()
                    .AddSingleton<SettingsWindowViewModel>()
                    .AddSingleton<SystemTrayViewModel>()
                    .AddSingleton<SettingsPageViewModel>()
                    .AddSingleton<MusicGalleryPageViewModel>()
                    .AddSingleton<AboutControlViewModel>()
                    .AddSingleton<MusicGalleryWindowViewModel>()
                    .AddSingleton<StatsDashboardControlViewModel>()
                    .AddSingleton<PlayQueueViewModel>()
                    .AddSingleton<PluginManagerControlViewModel>()

                    .AddTransient<NowPlayingWindowViewModel>()
                    .AddTransient<NowPlayingPageViewModel>()
                    .AddTransient<NowPlayingBarViewModel>()

                    .BuildServiceProvider()
            );
        }
    }
}
