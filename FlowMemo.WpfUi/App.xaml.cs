using Microsoft.Extensions.DependencyInjection;
using System.ComponentModel;
using System.Windows;

using FlowMemo.Core;
using FlowMemo.Core.Configurations;
using FlowMemo.Core.Enums;
using FlowMemo.Core.Repositories;
using FlowMemo.WpfUi.Helpers;
using FlowMemo.WpfUi.Messages;
using FlowMemo.WpfUi.Models;
using FlowMemo.WpfUi.Services;
using FlowMemo.WpfUi.ViewModels;
using FlowMemo.WpfUi.Views;

namespace FlowMemo.WpfUi;

public partial class App : Application
{
#if DEBUG
    public const bool IsDebugMode = true;
#else
    public const bool IsDebugMode = false;
#endif

    private const string UniqueEventName = (IsDebugMode) ? "1a7e0b4d-500c-418d-ba07-cca3f02f8cc7" : "74a761eb-2068-4ecc-830b-da4633d77ada";
    private const string UniqueMutexName = (IsDebugMode) ? "3f0e4f63-f47d-43e6-afe7-5524c931a449" : "79468726-ded4-4926-845d-b4bbf668ef27";

    private Mutex _mutex = null!;

    private AppMetadataService _appMetadataService = null!;
    private SettingsService _settingsService = null!;
    private NotifyIconService _notifyIconService = null!;

    private EventWaitHandle _eventWaitHandle = null!;

    private ApplicationSettingsModel _applicationSettings = null!;

    protected override async void OnStartup(StartupEventArgs e)
    {
        // _mutex is required to keep it in memory, using _ = new Mutex() will not work as garbage collector will dispose of it eventually.
        _mutex = new Mutex(true, UniqueMutexName, out bool createdNew);
        _eventWaitHandle = new(false, EventResetMode.AutoReset, UniqueEventName);

        if (!createdNew)
        {
            _eventWaitHandle.Set();
            Shutdown();
            return;
        }

        base.OnStartup(e);

        ServiceCollection services = new();
        ConfigureServices(services);
        Services = services.BuildServiceProvider();

        DatabaseConfiguration databaseConfiguration = Services.GetRequiredService<DatabaseConfiguration>();
        await DatabaseInitialiser.Initialise(databaseConfiguration.ConnectionString);

        _settingsService = Services.GetRequiredService<SettingsService>();
        await _settingsService.Load();
        _applicationSettings = _settingsService.ApplicationSettings;
        _applicationSettings.PropertyChanged += OnApplicationSettingsChanged;

        _appMetadataService = Services.GetRequiredService<AppMetadataService>();
        await _appMetadataService.Load();
        _ = Services.GetRequiredService<WindowService>();
        _notifyIconService = Services.GetRequiredService<NotifyIconService>();

        MessengerService messengerService = Services.GetRequiredService<MessengerService>();
        messengerService.Subscribe<ApplicationActionMessage>(OnApplicationActionMessage);

        // Spawn a thread which will be waiting for our event
        Thread thread = new(
            () => {
                while (_eventWaitHandle.WaitOne())
                    Current.Dispatcher.BeginInvoke(
                        () => messengerService.Publish(new ApplicationActionMessage(ApplicationAction.NewInstance))
                    );
            }
        )
        {
            // It is important mark it as background otherwise it will prevent app from exiting.
            IsBackground = true
        };

        thread.Start();

        messengerService.Publish(new ApplicationActionMessage(ApplicationAction.Start));

        ShutdownMode = (_applicationSettings.ShowNotifyIcon) ? ShutdownMode.OnExplicitShutdown : ShutdownMode.OnLastWindowClose;

        if (_settingsService.ApplicationSettings.CheckForUpdates)
        {
            DateTimeOffset date = DateTimeOffset.UtcNow;
            if (await VersionHelper.CheckForNewRelease(_appMetadataService.Metadata.LastUpdateCheck, date))
                _appMetadataService.Metadata.LastUpdateCheck = date.ToUnixTimeSeconds();
        }
    }

    public static IServiceProvider Services { get; private set; } = null!;

    private static void ConfigureServices(IServiceCollection services)
    {
        services.AddSingleton<DatabaseConfiguration>();

        services.AddSingleton<SettingsRepository>();
        services.AddSingleton<AppMetadataRepository>();
        services.AddSingleton<NoteRepository>();

        services.AddSingleton<AppMetadataService>();
        services.AddSingleton<SettingsService>();

        services.AddSingleton<MessengerService>();
        services.AddSingleton<WindowService>();
        services.AddTransient<NotifyIconService>();
        services.AddSingleton<ThemeService>();

        services.AddTransient<SettingsWindow>();
        services.AddTransient<SettingsViewModel>();

        services.AddTransient<ManagementWindow>();
        services.AddTransient<ManagementViewModel>();
    }

    protected override async void OnExit(ExitEventArgs e)
    {
        await _appMetadataService.Save();
        await _settingsService.Save();
        _notifyIconService.Dispose();

        _mutex?.ReleaseMutex();
        _mutex?.Dispose();
        _eventWaitHandle?.Dispose();

        base.OnExit(e);
    }

    private void OnApplicationActionMessage(ApplicationActionMessage message)
    {
        if (message.Action == ApplicationAction.Close)
            Shutdown();
    }

    private void OnApplicationSettingsChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ApplicationSettingsModel.ShowNotifyIcon))
            ShutdownMode = (_applicationSettings.ShowNotifyIcon) ? ShutdownMode.OnExplicitShutdown : ShutdownMode.OnLastWindowClose;
    }
}
