using System.ComponentModel;
using System.Reflection;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Microsoft.Extensions.DependencyInjection;

using FlowMemo.Core;
using FlowMemo.Core.Configurations;
using FlowMemo.Core.DataTransferObjects;
using FlowMemo.Core.Enums;
using FlowMemo.Core.Repositories;
using FlowMemo.WpfUi.Controls;
using FlowMemo.WpfUi.Helpers;
using FlowMemo.WpfUi.Messages;
using FlowMemo.WpfUi.Models;
using FlowMemo.WpfUi.Resources;
using FlowMemo.WpfUi.Services;
using FlowMemo.WpfUi.Themes;
using FlowMemo.WpfUi.Tools;
using FlowMemo.WpfUi.ViewModels;

internal static class Program
{
    private static int _pass, _fail;

    public static void Check(string name, bool ok, string detail = "")
    {
        if (ok) _pass++; else _fail++;
        Console.WriteLine($"{(ok ? "PASS" : "FAIL")}  {name}{(string.IsNullOrEmpty(detail) ? "" : "  | " + detail)}");
    }

    public static string Esc(string? s) => (s ?? "<null>").Replace("\r", "\\r").Replace("\n", "\\n");

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool SetProcessDpiAwarenessContext(IntPtr value);

    [STAThread]
    private static int Main(string[] args)
    {
        // 하네스가 DPI를 모르면 다른 앱 창의 좌표를 축소된 값으로 받는다 (125%에서 375px -> 300px).
        SetProcessDpiAwarenessContext(new IntPtr(-4)); // PER_MONITOR_AWARE_V2
        Console.OutputEncoding = Encoding.UTF8;
        try
        {
            if (args[0] == "logic") Logic.Run(args[1]);
            else if (args[0] == "data") Data.Run(args[1]);
            else if (args[0] == "render") Render.Run(args[1]);
            else if (args[0] == "migrate-copy") Data.MigrateCopy(args[1], args[2]);
            else if (args[0] == "ui") Ui.Run(args[1], args[2]);
            else if (args[0] == "monitors") Monitors.Print();
            else if (args[0] == "dpiprobe") Ui.DpiProbe(args[1]);
            else if (args[0] == "capturenotes") Ui.CaptureNotes(args[1], args[2], args.Length > 3 ? args[3] : null);
            else throw new ArgumentException($"알 수 없는 모드: {string.Join(' ', args)}");
        }
        catch (Exception ex)
        {
            Console.WriteLine("CRASH " + ex);
            _fail++;
        }
        Console.WriteLine($"RESULT pass={_pass} fail={_fail}");
        return _fail;
    }
}

internal static class Logic
{
    private static void Pump() => Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);

    private static void SetProp(object target, string name, object value)
        => target.GetType().GetProperty(name)!.SetValue(target, value);

    private static void RunTool(ITool tool, string header)
    {
        MenuItem item = tool.MenuItem.Items.OfType<MenuItem>().FirstOrDefault(i => (i.Header as string) == header)
            ?? throw new Exception($"menu '{header}' not found under '{tool.MenuItem.Header}'");
        item.Command!.Execute(item.CommandParameter);
        Pump();
    }

    private static void PressKey(UIElement target, Key key)
    {
        PresentationSource src = PresentationSource.FromVisual(target)!;
        KeyEventArgs e = new(Keyboard.PrimaryDevice, src, 0, key) { RoutedEvent = Keyboard.PreviewKeyDownEvent };
        target.RaiseEvent(e);
        Pump();
    }

    /// <summary>실행 중에 뜨는 메시지 상자를 자동으로 닫고, 떴는지 알려 준다.</summary>
    private static bool RunExpectingDialog(Action action)
    {
        bool seen = false;
        int pid = Environment.ProcessId;
        using CancellationTokenSource cts = new();
        Task closer = Task.Run(async () =>
        {
            for (int i = 0; i < 100 && !cts.IsCancellationRequested; i++)
            {
                IntPtr dialog = Native.VisibleWindows(pid).FirstOrDefault(h => Native.ClassName(h) == "#32770");
                if (dialog != IntPtr.Zero)
                {
                    seen = true;
                    Native.PostMessage(dialog, 0x0010 /* WM_CLOSE */, IntPtr.Zero, IntPtr.Zero);
                    return;
                }
                await Task.Delay(50);
            }
        });
        action();
        cts.Cancel();
        closer.Wait(2000);
        return seen;
    }

    private static void SaveVisual(FrameworkElement element, string path)
    {
        int w = (int)Math.Ceiling(element.ActualWidth), h = (int)Math.Ceiling(element.ActualHeight);
        RenderTargetBitmap bmp = new(Math.Max(w, 1), Math.Max(h, 1), 96, 96, PixelFormats.Pbgra32);
        DrawingVisual bg = new();
        using (DrawingContext dc = bg.RenderOpen())
            dc.DrawRectangle(Brushes.White, null, new Rect(0, 0, w, h));
        bmp.Render(bg);
        bmp.Render(element);
        PngBitmapEncoder enc = new();
        enc.Frames.Add(BitmapFrame.Create(bmp));
        using FileStream fs = File.Create(path);
        enc.Save(fs);
    }

    public static void Run(string outDir)
    {
        Console.WriteLine("== 로직 테스트 (FlowMemo.dll 실제 클래스)");

        Application app = new() { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        try
        {
            app.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri("pack://application:,,,/PresentationFramework.Fluent;component/Themes/Fluent.xaml") });
        }
        catch (Exception ex)
        {
            Console.WriteLine("note: Fluent 테마를 못 불러옴: " + ex.Message);
        }
        FrameworkElement.LanguageProperty.OverrideMetadata(typeof(FrameworkElement), new FrameworkPropertyMetadata(System.Windows.Markup.XmlLanguage.GetLanguage("ko-KR")));

        // NoteTextBoxControl의 도구들이 만들어지도록 최소한의 서비스만 준비한다.
        SettingsService settings = new(null!);
        ToolSettingsModel toolSettings = new();
        foreach (PropertyInfo p in typeof(ToolSettingsModel).GetProperties().Where(p => p.PropertyType == typeof(ToolState)))
            p.SetValue(toolSettings, ToolState.Enabled);
        SetProp(settings, "ToolSettings", toolSettings);
        SetProp(settings, "NoteSettings", new NoteSettingsModel { TransparencyMode = TransparencyMode.WhenPinned });
        SetProp(settings, "EditorSettings", new EditorSettingsModel());
        SetProp(settings, "ApplicationSettings", new ApplicationSettingsModel());
        ServiceCollection services = new();
        services.AddSingleton(settings);
        typeof(FlowMemo.WpfUi.App).GetProperty("Services", BindingFlags.Public | BindingFlags.Static)!.SetValue(null, services.BuildServiceProvider());

        NoteTextBoxControl tb = new() { FontSize = 16, Width = 400, Height = 260 };
        Window win = new()
        {
            Content = tb, SizeToContent = SizeToContent.WidthAndHeight, Left = -20000, Top = -20000,
            ShowInTaskbar = false, ShowActivated = false, WindowStyle = WindowStyle.None
        };
        win.Show();
        Pump();

        ContextMenu menu = tb.ContextMenu!;
        ITool[] tools = (ITool[])menu.GetType().GetField("_tools", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(menu)!;
        ITool Tool<T>() => tools.First(t => t is T);

        void SetText(string text, int selStart = -1, int selLength = 0)
        {
            tb.Text = text;
            Pump();
            if (selStart >= 0) tb.Select(selStart, selLength);
            else tb.Select(tb.Text.Length, 0);
        }

        tb.NewLineAtEnd = false;

        // 해시: 선택 영역만
        SetText("abc\r\nxyz", 0, 3);
        RunTool(Tool<HashTool>(), "SHA256");
        Check("해시 도구는 선택 영역만 해시", tb.Text == "BA7816BF8F01CFEA414140DE5DAE2223B00361A396177A9CB410FF61F20015AD\r\nxyz", Program.Esc(tb.Text));

        // 색상: HEX -> RGB
        SetText("add face decade #FFF #FFAA00 ff00aa 1a2b3c");
        tb.Select(0, 0);
        RunTool(Tool<ColourTool>(), Strings.Tool_Colour_HexToRgb);
        Check("HEX→RGB: 영어 단어는 그대로, 색상 값만 변환",
            tb.Text == "add face decade 255, 255, 255 255, 170, 0 255, 0, 170 26, 43, 60", tb.Text);

        // 색상: RGB -> HEX
        SetText("rgb(255, 0, 170) / 2024, 11, 06");
        tb.Select(0, 0);
        RunTool(Tool<ColourTool>(), Strings.Tool_Colour_RgbToHex);
        Check("RGB→HEX: 날짜 같은 숫자 묶음은 그대로", tb.Text == "#FF00AA / 2024, 11, 06", tb.Text);

        // 목록 기호 지우기
        SetText("1. 하나\r\n  - 둘\r\n안녕 하세요\r\n* 셋");
        tb.Select(0, 0);
        RunTool(Tool<ListTool>(), Strings.Tool_List_Remove);
        Check("목록 기호만 지우고 일반 줄은 그대로", tb.Text == "하나\r\n  둘\r\n안녕 하세요\r\n셋", Program.Esc(tb.Text));

        // 번호 매기기 + 정렬 (한글)
        SetText("다\r\n가\r\n나");
        tb.Select(0, 0);
        RunTool(Tool<SortTool>(), Strings.Tool_Sort_Ascending);
        Check("한글 줄 정렬", tb.Text == "가\r\n나\r\n다", Program.Esc(tb.Text));

        // JSON
        SetText("{\"이름\":\"홍길동\",\"n\":1}");
        tb.Select(0, 0);
        RunTool(Tool<JsonTool>(), Strings.Tool_Json_Prettify);
        Check("JSON 정렬 뒤에도 한글 유지", tb.Text.Contains("홍길동") && !tb.Text.Contains("\\u") && tb.Text.Contains("\n"), Program.Esc(tb.Text));

        // Base64
        SetText("7ZWc6riA");
        tb.Select(0, 0);
        RunTool(Tool<Base64Tool>(), Strings.Tool_Base64_Decode);
        Check("Base64 디코딩 (한글)", tb.Text == "한글", tb.Text);

        SetText("이건 base64가 아님!");
        tb.Select(0, 0);
        bool dialog = RunExpectingDialog(() => RunTool(Tool<Base64Tool>(), Strings.Tool_Base64_Decode));
        Check("잘못된 Base64 디코딩: 앱이 죽지 않고 안내만, 텍스트는 그대로", dialog && tb.Text == "이건 base64가 아님!", $"dialog={dialog} text={tb.Text}");

        // URL
        SetText("한글 경로/파일 이름.txt");
        tb.Select(0, 0);
        RunTool(Tool<UrlTool>(), Strings.Tool_Url_Encode);
        string encoded = tb.Text;
        RunTool(Tool<UrlTool>(), Strings.Tool_Url_Decode);
        Check("URL 인코딩/디코딩 왕복 (한글)", tb.Text == "한글 경로/파일 이름.txt" && encoded.Contains("%ED%95%9C"), encoded);

        // 날짜/시간: 현지 시간
        SetText("");
        RunTool(Tool<DateTimeTool>(), Strings.Tool_DateTime_Sortable);
        bool parsed = DateTime.TryParse(tb.Text, out DateTime inserted);
        Check("날짜·시간은 현지 시간 (UTC 아님)", parsed && Math.Abs((inserted - DateTime.Now).TotalMinutes) < 2, tb.Text);

        // 자동 들여쓰기
        tb.AutoIndent = true;
        SetText("    들여쓴 줄");
        PressKey(tb, Key.Return);
        Check("자동 들여쓰기: 앞 공백을 이어 붙임", tb.Text == "    들여쓴 줄\r\n    " && tb.CaretIndex == tb.Text.Length, Program.Esc(tb.Text));
        tb.Undo();
        Pump();
        Check("자동 들여쓰기 후 Ctrl+Z 한 번에 되돌림", tb.Text == "    들여쓴 줄", Program.Esc(tb.Text));

        SetText("  ab선택ef", 4, 2);
        PressKey(tb, Key.Return);
        Check("자동 들여쓰기: 선택 영역을 바꾸며 줄 나눔", tb.Text == "  ab\r\n  ef", Program.Esc(tb.Text));

        // 자동 줄 바꿈으로 꺾인 줄에서도 '실제 줄'의 들여쓰기를 따름
        tb.WrapText = true;
        SetText("  " + string.Concat(Enumerable.Repeat("가나다라마바사 ", 12)));
        Pump();
        int visualLines = ((TextBox)tb).LineCount;
        PressKey(tb, Key.Return);
        Check("꺾인 줄 끝에서 Enter: 들여쓰기 2칸 유지", visualLines > 1 && tb.Text.EndsWith("\r\n  "), $"visualLines={visualLines} tail={Program.Esc(tb.Text[^6..])}");
        tb.WrapText = false;

        // 탭 너비 0 (예전: 0으로 나누기로 종료)
        tb.TabSpaces = true;
        tb.TabWidth = 0;
        SetText("x");
        bool tabOk = true;
        try { PressKey(tb, Key.Tab); } catch { tabOk = false; }
        Check("탭 너비 0이어도 Tab에서 앱이 죽지 않음", tabOk && tb.Text == "x ", Program.Esc(tb.Text));
        tb.TabWidth = 4;
        SetText("ab");
        PressKey(tb, Key.Tab);
        Check("탭을 공백으로: 다음 탭 위치까지 채움", tb.Text == "ab  ", Program.Esc(tb.Text));
        tb.TabSpaces = false;

        // 끝에 빈 줄 유지
        tb.NewLineAtEnd = true;
        SetText("abc");
        bool appendedImmediately = tb.Text.EndsWith(Environment.NewLine);
        for (int i = 0; i < 12; i++) { Thread.Sleep(50); Pump(); }
        Check("끝에 빈 줄은 입력이 멈춘 뒤에 붙음 (바로 붙이면 한글 조합이 끊김)", !appendedImmediately);
        Check("끝에 빈 줄 유지", tb.Text == "abc\r\n", Program.Esc(tb.Text));
        tb.NewLineAtEnd = false;

        // 파일 끌어다 놓기: 인코딩 판별
        MethodInfo decode = typeof(NoteTextBoxControl).GetMethod("DecodeTextFile", BindingFlags.NonPublic | BindingFlags.Static)!;
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        string sample = "한글 테스트 ABC";
        string? fromCp949 = (string?)decode.Invoke(null, [Encoding.GetEncoding(949).GetBytes(sample)]);
        string? fromUtf8 = (string?)decode.Invoke(null, [Encoding.UTF8.GetBytes(sample)]);
        string? fromUtf16 = (string?)decode.Invoke(null, [Encoding.Unicode.GetPreamble().Concat(Encoding.Unicode.GetBytes(sample)).ToArray()]);
        string? fromBinary = (string?)decode.Invoke(null, [new byte[] { 0x4D, 0x5A, 0x00, 0x01, 0x02 }]);
        Check("끌어다 놓은 파일: CP949(예전 메모장 ANSI) 한글", fromCp949 == sample, fromCp949 ?? "null");
        Check("끌어다 놓은 파일: UTF-8 / UTF-16", fromUtf8 == sample && fromUtf16 == sample);
        Check("끌어다 놓은 파일: 바이너리는 거부", fromBinary is null);

        // 창 제목
        NoteModel note = new(new NoteSettingsModel(), "Yellow");
        string t0 = note.WindowTitle;
        note.Content = "  \r\n첫 줄 제목\r\n둘째 줄";
        string t1 = note.WindowTitle;
        note.Content = new string('가', 60);
        string t2 = note.WindowTitle;
        Check("창 제목: 빈 메모", t0 == "FlowMemo", t0);
        Check("창 제목: 첫 번째 내용 줄", t1 == "첫 줄 제목 - FlowMemo", t1);
        Check("창 제목: 긴 줄은 말줄임", t2.StartsWith(new string('가', 40) + "…"), t2);

        // 테마
        DefaultTheme theme = new();
        Check("색 이름 한국어, DB 키는 영어", theme.ColourSchemes["Yellow"].DisplayName == "노랑" && theme.ColourSchemes.Keys.Contains("Green"));
        ThemeService themeService = new(null!, settings);
        bool paletteOk = true;
        try { _ = themeService.GetPalette("NoSuchColour", ColourMode.Light); } catch { paletteOk = false; }
        Check("알 수 없는 색 이름이어도 기본 색으로", paletteOk);

        // 설정 화면: 투명도 항목 활성 상태가 바로 바뀜
        SettingsViewModel vm = new(null!, settings, new MessengerService());
        List<string> changed = [];
        ((INotifyPropertyChanged)vm).PropertyChanged += (s, e) => changed.Add(e.PropertyName ?? "");
        settings.NoteSettings.TransparencyMode = TransparencyMode.Disabled;
        bool disabledNow = !vm.IsTransparencyEnabled;
        settings.NoteSettings.TransparencyMode = TransparencyMode.Enabled;
        Check("투명도 모드를 바꾸면 아래 항목 활성 상태가 바로 바뀜", disabledNow && vm.IsTransparencyEnabled && changed.Count(n => n == nameof(SettingsViewModel.IsTransparencyEnabled)) == 2);
        vm.Detach();

        // 잠금(읽기 전용): IsReadOnly는 사용자의 직접 입력만 막으므로, 코드로 텍스트를 바꾸는 경로를 모두 확인한다.
        // 실패해도 붙여넣은 클립보드 내용이 로그에 남지 않게 텍스트는 출력하지 않는다.
        void CheckLocked(string what, Action action, int selectionLength = 0)
        {
            tb.IsReadOnly = false;
            SetText("abc def", selectionLength > 0 ? 0 : 3, selectionLength);
            tb.IsReadOnly = true;
            action();
            Pump();
            bool unchanged = tb.Text == "abc def";
            tb.IsReadOnly = false;
            Check($"잠근 메모는 바뀌지 않음: {what}", unchanged);
        }

        tb.AutoIndent = true;
        tb.TabSpaces = true;
        tb.TabWidth = 4;
        tb.PasteAction = PasteAction.Paste;
        tb.CopyAction = CopyAction.CopySelected;
        CheckLocked("Enter", () => PressKey(tb, Key.Return));
        CheckLocked("Tab", () => PressKey(tb, Key.Tab));
        CheckLocked("붙여넣기", () => tb.PasteCommand.Execute(null));
        CheckLocked("잘라내기", () => tb.CutCommand.Execute(null), selectionLength: 3);
        CheckLocked("비우기", () => tb.ClearCommand.Execute(null));
        CheckLocked("도구 (선택 영역 변환)", () => RunTool(Tool<HashTool>(), "SHA256"), selectionLength: 3);
        CheckLocked("도구 (넣기)", () => RunTool(Tool<DateTimeTool>(), Strings.Tool_DateTime_Sortable));
        tb.NewLineAtEnd = true;
        CheckLocked("끝에 빈 줄 자동 추가", () => { for (int i = 0; i < 12; i++) { Thread.Sleep(50); Pump(); } });
        tb.NewLineAtEnd = false;
        tb.TabSpaces = false;

        tb.IsReadOnly = false;
        SetText("abc def", 0, 3);
        tb.IsReadOnly = true;
        menu.GetType().GetMethod("Update")!.Invoke(menu, null);
        MenuItem MenuField(string name) => (MenuItem)menu.GetType().GetField(name, BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(menu)!;
        Check("잠근 메모의 우클릭 메뉴: 잘라내기·붙여넣기·비우기·도구 비활성, 복사는 가능",
            !MenuField("_cutMenuItem").IsEnabled && !MenuField("_pasteMenuItem").IsEnabled && !MenuField("_clearMenuItem").IsEnabled
            && MenuField("_copyMenuItem").IsEnabled && !Tool<HashTool>().MenuItem.IsEnabled);
        tb.IsReadOnly = false;
        menu.GetType().GetMethod("Update")!.Invoke(menu, null);

        // 여러 줄 내어쓰기
        MethodInfo unindent = typeof(NoteTextBoxControl).GetMethod("RemoveIndentation", BindingFlags.NonPublic | BindingFlags.Static)!;
        string Unindent(string line, int width) => (string)unindent.Invoke(null, [line, width])!;
        Check("여러 줄 내어쓰기: 탭 너비 2면 공백 2칸씩 뺌", Unindent("    x", 2) == "  x" && Unindent("  x", 2) == "x");
        Check("여러 줄 내어쓰기: 공백이 탭 너비보다 적으면 있는 만큼 뺌", Unindent("  x", 4) == "x" && Unindent("   x", 4) == "x" && Unindent("     x", 4) == " x");
        Check("여러 줄 내어쓰기: 탭은 하나만, 들여쓰기가 없으면 그대로", Unindent("\t\tx", 4) == "\tx" && Unindent("x", 4) == "x" && Unindent("", 4) == "");

        // 관리 창의 '닫기': 임시 DB로 실제 메모 창을 열고 닫는다
        string closeDbDir = Path.Combine(outDir, "close-note-db");
        if (Directory.Exists(closeDbDir))
            Directory.Delete(closeDbDir, recursive: true);
        DatabaseConfiguration closeDb = new(closeDbDir);
        DatabaseInitialiser.Initialise(closeDb.ConnectionString).GetAwaiter().GetResult();
        NoteRepository closeRepository = new(closeDb);
        int closeNoteId = closeRepository.Create(
            new NoteDto(0, "관리 창에서 닫을 메모", 200, 200, 300, 300, 1, 1, "Yellow", false, true, false, null, null, null)
        ).GetAwaiter().GetResult();
        AppMetadataService closeMetadata = new(new AppMetadataRepository(closeDb));
        closeMetadata.Load().GetAwaiter().GetResult();
        MessengerService closeMessenger = new();
        NoteDto? closedMessage = null;
        closeMessenger.Subscribe<NoteActionMessage>(m => { if (m.Action == NoteAction.Closed) closedMessage = m.NoteDto; });
        WindowService closeWindows = new(FlowMemo.WpfUi.App.Services, closeMessenger, closeMetadata, settings, closeRepository, new ThemeService(closeMetadata, settings));
        // 창을 만들다 실패하면 오류 대화 상자가 뜨므로, 테스트가 거기서 멈추지 않게 자동으로 닫는다.
        bool openDialog = RunExpectingDialog(() =>
        {
            closeMessenger.Publish(new MultipleNoteWindowActionMessage([closeNoteId], NoteWindowAction.Open));
            Pump();
        });
        bool openedBeforeClose = !openDialog && closeWindows.IsNoteWindowOpen(closeNoteId);
        RunExpectingDialog(() =>
        {
            closeMessenger.Publish(new MultipleNoteWindowActionMessage([closeNoteId], NoteWindowAction.Close));
            Pump();
        });
        NoteDto closedInDb = closeRepository.GetById(closeNoteId).GetAwaiter().GetResult();
        Check("관리 창에서 닫은 메모: '열림' 표시가 꺼지고 다음 시작 때 다시 열리지 않음",
            openedBeforeClose && !closeWindows.IsNoteWindowOpen(closeNoteId) && closedMessage?.IsOpen == false && !closedInDb.IsOpen,
            $"opened={openedBeforeClose} message={closedMessage?.IsOpen} db={closedInDb.IsOpen}");

        // 트레이 전용 + 모든 메모 보기/숨기기 (위 임시 DB와 WindowService를 그대로 쓴다)
        int hideNoteId = closeRepository.Create(
            new NoteDto(0, "숨겼다 다시 볼 메모", 240, 240, 300, 300, 1, 1, "Blue", false, true, false, null, null, null)
        ).GetAwaiter().GetResult();
        RunExpectingDialog(() =>
        {
            closeMessenger.Publish(new MultipleNoteWindowActionMessage([hideNoteId], NoteWindowAction.Open));
            Pump();
        });
        var openNoteWindows = (Dictionary<int, FlowMemo.WpfUi.Views.NoteWindow>)typeof(WindowService)
            .GetField("_openNoteWindows", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(closeWindows)!;
        Window hideWindow = openNoteWindows[hideNoteId];

        Check("트레이 전용(기본): 메모가 작업 표시줄에 나오지 않음", !hideWindow.ShowInTaskbar);
        settings.ApplicationSettings.TrayOnly = false;
        Pump();
        Check("트레이 전용을 끄면 메모가 바로 작업 표시줄에 나옴", hideWindow.ShowInTaskbar);
        settings.ApplicationSettings.TrayOnly = true;
        Pump();

        settings.ApplicationSettings.ToggleNotesHotkey = "Ctrl+Alt+Shift+F24";
        GlobalHotkeyService hotkeyService = new(closeMessenger, settings);
        nint hotkeyHandle = ((System.Windows.Interop.HwndSource)typeof(GlobalHotkeyService)
            .GetField("_source", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(hotkeyService)!).Handle;
        Check("전역 단축키가 등록됨", !hotkeyService.HasConflict);
        Native.PostMessage(hotkeyHandle, 0x0312 /* WM_HOTKEY */, new IntPtr(1), IntPtr.Zero);
        Pump();
        Check("단축키: 모든 메모를 숨김", closeWindows.AreNotesHidden && !hideWindow.IsVisible);
        Native.PostMessage(hotkeyHandle, 0x0312 /* WM_HOTKEY */, new IntPtr(1), IntPtr.Zero);
        Pump();
        Check("단축키를 다시 누르면 모두 보임", !closeWindows.AreNotesHidden && hideWindow.IsVisible);

        closeWindows.ToggleNotesVisibility();
        Pump();
        RunExpectingDialog(() =>
        {
            closeMessenger.Publish(new OpenNoteWindowMessage());
            Pump();
        });
        Check("숨긴 상태에서 새 메모를 열면 모두 다시 보임", !closeWindows.AreNotesHidden && hideWindow.IsVisible);

        closeWindows.ToggleNotesVisibility();
        Pump();
        closeMessenger.Publish(new WindowActionMessage(WindowAction.Activate));
        Pump();
        Check("숨긴 상태에서 트레이 아이콘을 누르면 모두 다시 보임", !closeWindows.AreNotesHidden && hideWindow.IsVisible);

        hotkeyService.Dispose();
        foreach (Window openWindow in openNoteWindows.Values.ToList())
            openWindow.Close();
        Pump();

        // 단축키 형식
        Check("단축키 형식: 읽고 쓰기",
            HotkeyGesture.TryParse("Ctrl+Alt+H", out HotkeyGesture parsedHotkey)
            && parsedHotkey.Modifiers == (ModifierKeys.Control | ModifierKeys.Alt) && parsedHotkey.Key == Key.H && parsedHotkey.ToString() == "Ctrl+Alt+H");
        Check("단축키 형식: 숫자 키와 Win 키",
            HotkeyGesture.TryParse("Win+Shift+1", out HotkeyGesture digitHotkey) && digitHotkey.Key == Key.D1 && digitHotkey.ToString() == "Shift+Win+1");
        Check("단축키 형식: 수정 키가 없거나 잘못된 값은 거부",
            new[] { "H", "Ctrl", "Ctrl+Shift", "Ctrl+12", "Foo+H", "", null }.All(text => !HotkeyGesture.TryParse(text, out _)));

        // 시작 프로그램 등록: 실제 Run 키 대신 테스트용 키에 쓴다
        const string startupTestRoot = @"Software\FlowMemoTests";
        string startupTestKey = startupTestRoot + @"\Run";
        try
        {
            StartupRegistration registration = new("FlowMemo-Test", @"C:\앱 폴더\FlowMemo.exe", startupTestKey);
            string? StoredCommand()
            {
                using Microsoft.Win32.RegistryKey? key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(startupTestKey);
                return key?.GetValue("FlowMemo-Test") as string;
            }

            bool offAtFirst = !registration.IsEnabled;
            registration.SetEnabled(true);
            Check("시작 프로그램 등록: 켜면 따옴표로 감싼 경로를 저장", offAtFirst && registration.IsEnabled && StoredCommand() == "\"C:\\앱 폴더\\FlowMemo.exe\"", StoredCommand() ?? "null");

            new StartupRegistration("FlowMemo-Test", @"D:\옮긴 곳\FlowMemo.exe", startupTestKey).RefreshPath();
            Check("시작 프로그램 등록: 실행 파일을 옮기면 경로를 고침", StoredCommand() == "\"D:\\옮긴 곳\\FlowMemo.exe\"", StoredCommand() ?? "null");

            registration.SetEnabled(false);
            registration.RefreshPath();
            Check("시작 프로그램 등록: 끄면 지우고, 꺼진 상태에서는 경로 갱신이 다시 만들지 않음", !registration.IsEnabled && StoredCommand() is null);
        }
        finally
        {
            Microsoft.Win32.Registry.CurrentUser.DeleteSubKeyTree(startupTestRoot, throwOnMissingSubKey: false);
        }

        // 트레이 아이콘과 트레이 전용은 함께 움직인다 (둘 다 없으면 앱을 열 방법이 없다)
        ApplicationSettingsModel trayModel = new();
        bool trayOnlyByDefault = trayModel.TrayOnly && trayModel.ShowNotifyIcon;
        trayModel.ShowNotifyIcon = false;
        bool iconOffClearsTrayOnly = !trayModel.TrayOnly;
        trayModel.TrayOnly = true;
        Check("트레이 아이콘을 끄면 트레이 전용도 꺼지고, 트레이 전용을 켜면 아이콘도 켜짐", trayOnlyByDefault && iconOffClearsTrayOnly && trayModel.ShowNotifyIcon);

        // 우클릭 메뉴 그림
        try
        {
            tb.Text = "메뉴 미리 보기 텍스트";
            tb.SelectAll();
            menu.GetType().GetMethod("Update")!.Invoke(menu, null);
            menu.ApplyTemplate();
            menu.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            menu.Arrange(new Rect(menu.DesiredSize));
            menu.UpdateLayout();
            Pump();
            string path = Path.Combine(outDir, "L1_text_context_menu.png");
            SaveVisual(menu, path);
            Console.WriteLine($"info  우클릭 메뉴 그림: {path} ({menu.ActualWidth:0}x{menu.ActualHeight:0})");
        }
        catch (Exception ex)
        {
            Console.WriteLine("info  우클릭 메뉴 그림 실패: " + ex.Message);
        }

        // 실제 팝업으로 열어서 캡처 (실제 앱과 같은 템플릿·정렬)
        try
        {
            menu.Placement = System.Windows.Controls.Primitives.PlacementMode.AbsolutePoint;
            menu.HorizontalOffset = 0;
            menu.VerticalOffset = 0;
            menu.IsOpen = true;
            Pump();
            Thread.Sleep(300);
            Pump();
            if (PresentationSource.FromVisual(menu) is System.Windows.Interop.HwndSource menuSource)
                Native.Capture(menuSource.Handle, Path.Combine(outDir, "L2_text_context_menu_popup.png"));

            MenuItem toolsItem = menu.Items.OfType<MenuItem>().First(m => System.Windows.Automation.AutomationProperties.GetName(m) == Strings.TextMenu_Tools);
            toolsItem.IsSubmenuOpen = true;
            Pump();
            Thread.Sleep(300);
            Pump();
            MenuItem firstTool = toolsItem.Items.OfType<MenuItem>().First();
            if (PresentationSource.FromVisual(firstTool) is System.Windows.Interop.HwndSource subSource)
                Native.Capture(subSource.Handle, Path.Combine(outDir, "L3_tools_submenu_popup.png"));
            menu.IsOpen = false;
            Pump();
            Console.WriteLine("info  실제 팝업 캡처: L2_text_context_menu_popup.png, L3_tools_submenu_popup.png");
        }
        catch (Exception ex)
        {
            Console.WriteLine("info  실제 팝업 캡처 실패: " + ex.Message);
        }

        win.Close();
    }
}
