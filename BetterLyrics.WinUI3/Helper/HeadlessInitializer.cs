// 2025/6/23 by Zhe Fang

using BetterLyrics.WinUI3.Models.DbContext;
using BetterLyrics.WinUI3.Services.FileSystemService;
using BetterLyrics.WinUI3.Services.PluginService;
using BetterLyrics.WinUI3.Services.SettingsService;
using BetterLyrics.WinUI3.Services.SongSearchMapService;
using CommunityToolkit.Mvvm.DependencyInjection;
using Microsoft.EntityFrameworkCore;
using Microsoft.UI.Dispatching;
using System.Threading;
using System.Threading.Tasks;

namespace BetterLyrics.WinUI3.Helper
{
    public static class HeadlessInitializer
    {
        private static int _dispatcherInitialized;

        public static void EnsureDispatcherQueue()
        {
            if (Interlocked.Exchange(ref _dispatcherInitialized, 1) == 1)
            {
                return;
            }

            DispatcherQueueController.CreateOnCurrentThread();
        }

        public static async Task InitializeDatabasesAsync()
        {
            var playHistoryFactory = Ioc.Default.GetRequiredService<IDbContextFactory<PlayHistoryDbContext>>();
            var songSearchMapFactory = Ioc.Default.GetRequiredService<IDbContextFactory<SongSearchMapDbContext>>();
            var filesIndexFactory = Ioc.Default.GetRequiredService<IDbContextFactory<FilesIndexDbContext>>();
            var lyricsCacheFactory = Ioc.Default.GetRequiredService<IDbContextFactory<LyricsCacheDbContext>>();

            using (var playHistoryDb = await playHistoryFactory.CreateDbContextAsync())
            {
                await playHistoryDb.Database.EnsureCreatedAsync();
            }

            using (var songSearchMapDb = await songSearchMapFactory.CreateDbContextAsync())
            {
                await songSearchMapDb.Database.EnsureCreatedAsync();
            }

            using (var filesIndexDb = await filesIndexFactory.CreateDbContextAsync())
            {
                await filesIndexDb.Database.EnsureCreatedAsync();
            }

            using (var lyricsCacheDb = await lyricsCacheFactory.CreateDbContextAsync())
            {
                await lyricsCacheDb.Database.EnsureCreatedAsync();
            }
        }

        public static async Task InitializeBackgroundServicesAsync()
        {
            var settingsService = Ioc.Default.GetRequiredService<ISettingsService>();

            var songSearchMapService = Ioc.Default.GetRequiredService<ISongSearchMapService>();
            var obsoleteSongSearchMap = settingsService.AppSettings.MappedSongSearchQueries;
            if (obsoleteSongSearchMap.Count > 0)
            {
                foreach (var item in obsoleteSongSearchMap)
                {
                    await songSearchMapService.SaveMappingAsync(item);
                }
                obsoleteSongSearchMap.Clear();
            }

            var fileSystemService = Ioc.Default.GetRequiredService<IFileSystemService>();
            foreach (var item in settingsService.AppSettings.LocalMediaFolders)
            {
                if (item.LastSyncTime == null)
                {
                    _ = Task.Run(async () => await fileSystemService.ScanMediaFolderAsync(item, CancellationToken.None));
                }
            }
            fileSystemService.StartAllFolderTimers();

            var pluginService = Ioc.Default.GetRequiredService<IPluginService>();
            await pluginService.LoadPluginsAsync();
        }
    }
}
