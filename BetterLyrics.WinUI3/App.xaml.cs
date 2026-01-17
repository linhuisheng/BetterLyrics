using BetterLyrics.WinUI3.Helper;
using BetterLyrics.WinUI3.Hooks;
using BetterLyrics.WinUI3.Services.PluginService;
using BetterLyrics.WinUI3.Services.SettingsService;
using BetterLyrics.WinUI3.Services.SongSearchMapService;
using BetterLyrics.WinUI3.ViewModels;
using BetterLyrics.WinUI3.Views;
using CommunityToolkit.Mvvm.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Xaml;
using Microsoft.Windows.AppLifecycle;
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Windows.ApplicationModel.Activation;
using Windows.Storage;

namespace BetterLyrics.WinUI3
{
    public partial class App : Application
    {
        private Window? m_window;
        private readonly ILogger<App> _logger;
        public static new App Current => (App)Application.Current;

        private readonly string _appKey = Windows.ApplicationModel.Package.Current.Id.FamilyName;

        public App()
        {
            // Must be done before InitializeComponent
            if (!TryHandleSingleInstance())
            {
                // 如果移交成功直接退出当前进程
                Environment.Exit(0);
                return;
            }

            this.InitializeComponent();

            ServiceBootstrapper.EnsureInitialized();

            _logger = Ioc.Default.GetRequiredService<ILogger<App>>();

            // 注册全局异常捕获
            UnhandledException += App_UnhandledException;
            AppDomain.CurrentDomain.UnhandledException += CurrentDomain_UnhandledException;
            AppDomain.CurrentDomain.FirstChanceException += CurrentDomain_FirstChanceException;
            TaskScheduler.UnobservedTaskException += TaskScheduler_UnobservedTaskException;
        }

        protected override async void OnLaunched(Microsoft.UI.Xaml.LaunchActivatedEventArgs args)
        {
            await InitAppServicesAsync();

            AppActivationArguments appArgs = AppInstance.GetCurrent().GetActivatedEventArgs();
            if (appArgs.Kind == ExtendedActivationKind.File)
            {
                await HandleFileActivationAsync(appArgs);
            }
            else
            {
                HandleNormalLaunch();
            }
        }

        private async Task HandleFileActivationAsync(AppActivationArguments args)
        {
            if (args.Data is IFileActivatedEventArgs fileArgs)
            {
                var item = fileArgs.Files.FirstOrDefault();
                if (item is StorageFile file)
                {
                    _logger.LogInformation("App activated via file: {Path}", file.Path);

                    WindowHook.OpenOrShowWindow<SettingsWindow>();

                    var pluginManagerControlViewModel = Ioc.Default.GetRequiredService<PluginManagerControlViewModel>();
                    await pluginManagerControlViewModel.InstallPluginByFileAsync(file);
                }
            }
        }

        private void HandleNormalLaunch()
        {
            var settingsService = Ioc.Default.GetRequiredService<ISettingsService>();

            // 初始化系统托盘
            m_window = WindowHook.OpenOrShowWindow<SystemTrayWindow>();

            // 自动打开歌词窗口逻辑
            if (settingsService.AppSettings.GeneralSettings.AutoStartLyricsWindow)
            {
                var defaultStatus = settingsService.AppSettings.WindowBoundsRecords.Where(x => x.IsDefault);
                if (defaultStatus != null)
                {
                    foreach (var item in defaultStatus)
                    {
                        WindowHook.OpenOrShowWindow<NowPlayingWindow>(item);
                        if (!settingsService.AppSettings.GeneralSettings.MultiNowPlayingWindowMode) break;
                    }
                }
            }

            // 自动打开音乐库逻辑
            if (settingsService.AppSettings.MusicGallerySettings.AutoOpen)
            {
                WindowHook.OpenOrShowWindow<MusicGalleryWindow>();
            }
        }

        private async Task InitAppServicesAsync()
        {
            await HeadlessInitializer.InitializeDatabasesAsync();

            var settingsService = Ioc.Default.GetRequiredService<ISettingsService>();

            // 迁移逻辑
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

            // 启动后台扫描
            var fileSystemService = Ioc.Default.GetRequiredService<IFileSystemService>();
            foreach (var item in settingsService.AppSettings.LocalMediaFolders)
            {
                if (item.LastSyncTime == null)
                {
                    _ = Task.Run(async () => await fileSystemService.ScanMediaFolderAsync(item, CancellationToken.None));
                }
            }
            fileSystemService.StartAllFolderTimers();

            // 加载插件
            var pluginService = Ioc.Default.GetRequiredService<IPluginService>();
            pluginService.LoadPluginsAsync();
        }

        private bool TryHandleSingleInstance()
        {
            var mainInstance = AppInstance.FindOrRegisterForKey(_appKey);
            if (mainInstance.IsCurrent)
            {
                mainInstance.Activated += OnMainInstanceActivated;
                return true;
            }
            else
            {
                var args = AppInstance.GetCurrent().GetActivatedEventArgs();
                try
                {
                    // 将激活参数（包括文件信息）发送给主实例
                    mainInstance.RedirectActivationToAsync(args).AsTask().Wait();
                }
                catch (Exception) { }
                return false;
            }
        }

        private void OnMainInstanceActivated(object? sender, AppActivationArguments e)
        {
            m_window?.DispatcherQueue.TryEnqueue(async () =>
            {
                if (e.Kind == ExtendedActivationKind.File)
                {
                    // 复用上面的文件处理逻辑
                    await HandleFileActivationAsync(e);
                }
                else
                {
                    // 普通启动则打开窗口切换器
                    WindowHook.OpenOrShowWindow<LyricsWindowSwitchWindow>();
                }
            });
        }


        private void App_UnhandledException(object sender, Microsoft.UI.Xaml.UnhandledExceptionEventArgs e)
        {
            _logger.LogError(e.Exception, "App_UnhandledException");
            e.Handled = true;
        }

        private void CurrentDomain_FirstChanceException(object? sender, System.Runtime.ExceptionServices.FirstChanceExceptionEventArgs e)
        {
            // FirstChance 异常非常多（比如内部 try-catch 也会触发），通常建议只在 Debug 模式记录，或者过滤特定类型
            // _logger.LogError(e.Exception, "CurrentDomain_FirstChanceException"); 
        }

        private void CurrentDomain_UnhandledException(object sender, System.UnhandledExceptionEventArgs e)
        {
            _logger.LogError(e.ExceptionObject.ToString(), "CurrentDomain_UnhandledException");
        }

        private void TaskScheduler_UnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
        {
            _logger.LogError(e.Exception, "TaskScheduler_UnobservedTaskException");
        }
    }
}
