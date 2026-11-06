using Microsoft.Extensions.DependencyInjection;
using System.ComponentModel;
using System.Globalization;
using System.Windows;
using System.Windows.Markup;
using System.Windows.Threading;

using FlowMemo.Core;
using FlowMemo.Core.Configurations;
using FlowMemo.Core.Enums;
using FlowMemo.Core.Repositories;
using FlowMemo.WpfUi.Helpers;
using FlowMemo.WpfUi.Messages;
using FlowMemo.WpfUi.Models;
using FlowMemo.WpfUi.Resources;
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

    private Mutex? _mutex;
    private bool _ownsMutex;

    private AppMetadataService? _appMetadataService;
    private SettingsService? _settingsService;
    private NotifyIconService? _notifyIconService;
    private WindowService? _windowService;

    private EventWaitHandle? _eventWaitHandle;

    private ApplicationSettingsModel? _applicationSettings;

    protected override async void OnStartup(StartupEventArgs e)
    {
        // _mutex is required to keep it in memory, using _ = new Mutex() will not work as garbage collector will dispose of it eventually.
        _mutex = new Mutex(true, UniqueMutexName, out bool createdNew);
        _ownsMutex = createdNew;
        _eventWaitHandle = new(false, EventResetMode.AutoReset, UniqueEventName);

        if (!createdNew)
        {
            // 이미 실행 중이면 기존 인스턴스에 알리고 끝낸다.
            _eventWaitHandle.Set();
            Shutdown();
            return;
        }

        RegisterGlobalExceptionHandlers();
        ApplyUiLanguage();

        base.OnStartup(e);

        ServiceCollection services = new();
        ConfigureServices(services);
        Services = services.BuildServiceProvider();

        DatabaseConfiguration databaseConfiguration = Services.GetRequiredService<DatabaseConfiguration>();
        ErrorReporter.Initialize(databaseConfiguration.DataPath);

        try
        {
            await DatabaseInitialiser.Initialise(databaseConfiguration.ConnectionString);

            _settingsService = Services.GetRequiredService<SettingsService>();
            await _settingsService.Load();

            _appMetadataService = Services.GetRequiredService<AppMetadataService>();
            await _appMetadataService.Load();
        }
        catch (DatabaseVersionException ex)
        {
            ErrorReporter.Log(ex, "Database version");
            ShowFatalError(string.Format(Strings.Error_DatabaseNewer, databaseConfiguration.DataPath));
            return;
        }
        catch (Exception ex)
        {
            ErrorReporter.Log(ex, "Database open");
            ShowFatalError(string.Format(Strings.Error_DatabaseOpen, ex.Message, databaseConfiguration.DataPath));
            return;
        }

        _applicationSettings = _settingsService.ApplicationSettings;
        _applicationSettings.PropertyChanged += OnApplicationSettingsChanged;

        _windowService = Services.GetRequiredService<WindowService>();
        _notifyIconService = Services.GetRequiredService<NotifyIconService>();

        MessengerService messengerService = Services.GetRequiredService<MessengerService>();
        messengerService.Subscribe<ApplicationActionMessage>(OnApplicationActionMessage);

        // Spawn a thread which will be waiting for our event
        EventWaitHandle eventWaitHandle = _eventWaitHandle;
        Thread thread = new(
            () => {
                try
                {
                    while (eventWaitHandle.WaitOne())
                        Current?.Dispatcher.BeginInvoke(
                            () => messengerService.Publish(new ApplicationActionMessage(ApplicationAction.NewInstance))
                        );
                }
                catch (ObjectDisposedException)
                {
                    // 종료 중
                }
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
        services.AddSingleton<NotifyIconService>();
        services.AddSingleton<ThemeService>();

        services.AddTransient<SettingsWindow>();
        services.AddTransient<SettingsViewModel>();

        services.AddTransient<ManagementWindow>();
        services.AddTransient<ManagementViewModel>();
    }

    protected override void OnSessionEnding(SessionEndingCancelEventArgs e)
    {
        // 로그오프/종료 때는 창이 정상적으로 닫히지 않을 수 있으니 먼저 저장해 둔다.
        SaveEverything();
        base.OnSessionEnding(e);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        // async void로 두면 첫 await 뒤의 저장이 끝나기 전에 프로세스가 끝날 수 있었다.
        SaveEverything();

        _notifyIconService?.Dispose();

        if (_ownsMutex)
            _mutex?.ReleaseMutex();
        _mutex?.Dispose();
        _eventWaitHandle?.Dispose();

        base.OnExit(e);
    }

    private void SaveEverything()
    {
        try
        {
            _windowService?.SaveAllOpenNotes();
            _appMetadataService?.Save().GetAwaiter().GetResult();
            _settingsService?.Save().GetAwaiter().GetResult();
        }
        catch (Exception ex)
        {
            ErrorReporter.Log(ex, "SaveEverything");
        }
    }

    private void RegisterGlobalExceptionHandlers()
    {
        DispatcherUnhandledException += OnDispatcherUnhandledException;
        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            ErrorReporter.Log(args.Exception, "UnobservedTaskException");
            args.SetObserved();
        };
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
        {
            if (args.ExceptionObject is Exception ex)
                ErrorReporter.Log(ex, "AppDomain.UnhandledException");
        };
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        // 메모 앱은 꺼지는 것보다 계속 떠 있으면서 알려 주는 편이 데이터 손실이 적다.
        ErrorReporter.Report(e.Exception, "DispatcherUnhandledException");
        e.Handled = true;
    }

    private static void ApplyUiLanguage()
    {
        // WPF는 Windows 언어와 상관없이 기본 Language가 en-US다.
        // 한국어로 맞춰야 글꼴 대체(한글/한자)와 숫자·날짜 서식이 한국어 기준이 된다.
        FrameworkElement.LanguageProperty.OverrideMetadata(
            typeof(FrameworkElement),
            new FrameworkPropertyMetadata(XmlLanguage.GetLanguage(CultureInfo.CurrentCulture.IetfLanguageTag))
        );
    }

    private void ShowFatalError(string message)
    {
        MessageBox.Show(message, Strings.Error_Title, MessageBoxButton.OK, MessageBoxImage.Error);
        Shutdown(1);
    }

    private void OnApplicationActionMessage(ApplicationActionMessage message)
    {
        if (message.Action == ApplicationAction.Close)
            Shutdown();
    }

    private void OnApplicationSettingsChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ApplicationSettingsModel.ShowNotifyIcon) && _applicationSettings is not null)
            ShutdownMode = (_applicationSettings.ShowNotifyIcon) ? ShutdownMode.OnExplicitShutdown : ShutdownMode.OnLastWindowClose;
    }
}
