using System.Reflection;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Microsoft.Extensions.DependencyInjection;

using FlowMemo.Core;
using FlowMemo.Core.Configurations;
using FlowMemo.Core.DataTransferObjects;
using FlowMemo.Core.Repositories;
using FlowMemo.WpfUi.Services;
using FlowMemo.WpfUi.ViewModels;
using FlowMemo.WpfUi.Views;

/// <summary>
/// 관리 창·설정 창을 이 프로세스 안에서 만들어 PNG로 그린다 (검사 없음, 눈으로 확인용).
/// 화면이 잠겨 있으면 PrintWindow로는 일반 창이 흰색으로 찍히는데, 이 방법은 잠겨 있어도 된다.
/// 창 테두리·제목 표시줄은 그리지 않는다.
/// </summary>
internal static class Render
{
    private static void Pump() => Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);

    public static void Run(string outDir)
    {
        Console.WriteLine("== 렌더 (관리 창 · 휴지통 · 설정 창)");

        Application app = new() { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        app.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri("pack://application:,,,/PresentationFramework.Fluent;component/Themes/Fluent.xaml") });
        FrameworkElement.LanguageProperty.OverrideMetadata(typeof(FrameworkElement), new FrameworkPropertyMetadata(System.Windows.Markup.XmlLanguage.GetLanguage("ko-KR")));

        string dataDir = Path.Combine(Path.GetTempPath(), "FlowMemo-tests", "render-db");
        if (Directory.Exists(dataDir))
            Directory.Delete(dataDir, recursive: true);
        DatabaseConfiguration config = new(dataDir);
        DatabaseInitialiser.Initialise(config.ConnectionString).GetAwaiter().GetResult();

        ServiceCollection services = new();
        services.AddSingleton(config);
        services.AddSingleton<SettingsRepository>();
        services.AddSingleton<AppMetadataRepository>();
        services.AddSingleton<NoteRepository>();
        services.AddSingleton<AppMetadataService>();
        services.AddSingleton<SettingsService>();
        services.AddSingleton<MessengerService>();
        services.AddSingleton<WindowService>();
        services.AddSingleton<ThemeService>();
        services.AddTransient<SettingsWindow>();
        services.AddTransient<SettingsViewModel>();
        services.AddTransient<ManagementWindow>();
        services.AddTransient<ManagementViewModel>();
        ServiceProvider provider = services.BuildServiceProvider();
        typeof(FlowMemo.WpfUi.App).GetProperty("Services", BindingFlags.Public | BindingFlags.Static)!.SetValue(null, provider);

        provider.GetRequiredService<SettingsService>().Load().GetAwaiter().GetResult();
        provider.GetRequiredService<AppMetadataService>().Load().GetAwaiter().GetResult();

        NoteRepository notes = provider.GetRequiredService<NoteRepository>();
        DateTime now = DateTime.UtcNow;
        NoteDto Note(string content, string colour) => new(0, content, 100, 100, 300, 300, 1, 1, colour, false, false, false, now, now, null);
        notes.Create(Note("회의 메모\r\n- 3시 회의실\r\n- 자료 준비", "Yellow")).Wait();
        notes.Create(Note("장보기\r\n우유, 계란, 두부", "Green")).Wait();
        notes.Create(Note("할 일\r\n보고서 마무리", "Pink")).Wait();
        int trashed1 = notes.Create(Note("지난주 메모 (지운 것)", "Blue")).GetAwaiter().GetResult();
        int trashed2 = notes.Create(Note("예전 아이디어", "Purple")).GetAwaiter().GetResult();
        notes.MoveToTrash(trashed1, now).Wait();
        notes.MoveToTrash(trashed2, now.AddDays(-3)).Wait();

        ManagementWindow management = provider.GetRequiredService<ManagementWindow>();
        management.WindowStartupLocation = WindowStartupLocation.Manual;
        management.Left = -20000;
        management.Top = -20000;
        management.ShowActivated = false;
        management.Show();
        Pump();
        ManagementViewModel vm = (ManagementViewModel)management.DataContext;
        Save(management, outDir, "R1_management.png");

        vm.NotePreviews[0].IsSelected = true;
        Pump();
        Save(management, outDir, "R2_management_selected.png");

        vm.ShowTrashCommand.Execute(null);
        Pump();
        Save(management, outDir, "R3_trash.png");

        vm.NotePreviews[0].IsSelected = true;
        Pump();
        Save(management, outDir, "R4_trash_selected.png");
        management.Close();

        SettingsWindow settings = provider.GetRequiredService<SettingsWindow>();
        settings.WindowStartupLocation = WindowStartupLocation.Manual;
        settings.Left = -20000;
        settings.Top = -20000;
        settings.ShowActivated = false;
        settings.Show();
        Pump();
        Save(settings, outDir, "R5_settings_app.png");
        settings.Close();
    }

    private static void Save(Window window, string outDir, string name)
    {
        window.UpdateLayout();
        Pump();
        FrameworkElement root = (FrameworkElement)window.Content;
        const double scale = 1.25; // 이 PC 배율과 같게
        int w = (int)Math.Ceiling(root.ActualWidth * scale), h = (int)Math.Ceiling(root.ActualHeight * scale);
        RenderTargetBitmap bmp = new(Math.Max(w, 1), Math.Max(h, 1), 96 * scale, 96 * scale, PixelFormats.Pbgra32);

        // 창 배경(Fluent 테마 색, 시스템이 다크 모드면 어두운 색)을 먼저 깔고 내용을 그린다.
        // 창 배경은 창 템플릿이 그리므로 내용(Content)만 그리면 빠진다.
        Brush windowBackground = window.TryFindResource("ApplicationBackgroundBrush") as Brush
            ?? window.TryFindResource("SolidBackgroundFillColorBaseBrush") as Brush
            ?? Brushes.White;
        DrawingVisual background = new();
        using (DrawingContext dc = background.RenderOpen())
            dc.DrawRectangle(windowBackground, null, new Rect(0, 0, root.ActualWidth, root.ActualHeight));
        bmp.Render(background);
        bmp.Render(root);

        PngBitmapEncoder encoder = new();
        encoder.Frames.Add(BitmapFrame.Create(bmp));
        string path = Path.Combine(outDir, name);
        using (FileStream fs = File.Create(path))
            encoder.Save(fs);
        Console.WriteLine($"info  렌더: {path}");
    }
}
