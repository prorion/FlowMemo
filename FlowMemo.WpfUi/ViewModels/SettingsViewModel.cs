using System.ComponentModel;
using System.Drawing.Text;

using FlowMemo.Core.Enums;
using FlowMemo.WpfUi.Helpers;
using FlowMemo.WpfUi.Models;
using FlowMemo.WpfUi.Resources;
using FlowMemo.WpfUi.Services;

namespace FlowMemo.WpfUi.ViewModels;

public class SettingsViewModel : BaseViewModel, INotifyPropertyChanged
{
    public SettingsViewModel(
        AppMetadataService appMetadata,
        SettingsService settingsService,
        MessengerService messengerService
    ) : base(appMetadata, settingsService, messengerService)
    {
        ApplicationSettings = SettingsService.ApplicationSettings;
        NoteSettings = SettingsService.NoteSettings;
        EditorSettings = SettingsService.EditorSettings;
        ToolSettings = SettingsService.ToolSettings;

        NoteSettings.PropertyChanged += OnNoteSettingsChanged;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public ApplicationSettingsModel ApplicationSettings { get; set; }
    public NoteSettingsModel NoteSettings { get; set; }
    public EditorSettingsModel EditorSettings { get; set; }
    public ToolSettingsModel ToolSettings { get; set; }

    // 투명도 모드를 바꾸면 바로 아래 항목들이 켜지거나 꺼지도록 계산 속성으로 둔다.
    public bool IsTransparencyEnabled => NoteSettings.TransparencyMode != TransparencyMode.Disabled;

    /// <summary>Windows 시작 시 자동 실행. DB가 아니라 레지스트리에 바로 저장한다.</summary>
    public bool RunAtStartup
    {
        get => StartupRegistration.Current.IsEnabled;
        set
        {
            try
            {
                StartupRegistration.Current.SetEnabled(value);
            }
            catch (Exception ex)
            {
                ErrorReporter.Report(ex, "Run at startup");
            }

            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(RunAtStartup)));
        }
    }

    /// <summary>설정 창이 닫힐 때 호출해 설정 모델에 대한 구독을 해제한다.</summary>
    public void Detach()
        => NoteSettings.PropertyChanged -= OnNoteSettingsChanged;

    private void OnNoteSettingsChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(NoteSettingsModel.TransparencyMode))
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsTransparencyEnabled)));
    }

    public static KeyValuePair<StartupBehaviour, string>[] StartupBehaviourList { get; } = [
        new(StartupBehaviour.RestoreOpenNotes, Strings.Option_Startup_RestoreOpenNotes),
        new(StartupBehaviour.CreateNewNote, Strings.Option_Startup_CreateNewNote),
        new(StartupBehaviour.ShowManagementWindow, Strings.Option_Startup_ShowManagement)
    ];

    public static KeyValuePair<NewInstanceBehaviour, string>[] NewInstanceBehaviourList { get; } = [
        new(NewInstanceBehaviour.CreateNewNote, Strings.Option_Startup_CreateNewNote),
        new(NewInstanceBehaviour.ShowManagementWindow, Strings.Option_Startup_ShowManagement)
    ];

    public static KeyValuePair<StartupPosition, string>[] StartupPositionsList { get; } = [
        new(StartupPosition.TopLeft, Strings.Option_Position_TopLeft),
        new(StartupPosition.TopCentre, Strings.Option_Position_TopCentre),
        new(StartupPosition.TopRight, Strings.Option_Position_TopRight),
        new(StartupPosition.MiddleLeft, Strings.Option_Position_MiddleLeft),
        new(StartupPosition.MiddleCentre, Strings.Option_Position_MiddleCentre),
        new(StartupPosition.MiddleRight, Strings.Option_Position_MiddleRight),
        new(StartupPosition.BottomLeft, Strings.Option_Position_BottomLeft),
        new(StartupPosition.BottomCentre, Strings.Option_Position_BottomCentre),
        new(StartupPosition.BottomRight, Strings.Option_Position_BottomRight)
    ];

    public static KeyValuePair<MinimizeMode, string>[] MinimizeModeList { get; } = [
        new(MinimizeMode.Allow, Strings.Option_Minimize_Allow),
        new(MinimizeMode.Prevent, Strings.Option_Minimize_Prevent),
        new(MinimizeMode.PreventIfPinned, Strings.Option_Minimize_PreventIfPinned)
    ];

    public static KeyValuePair<VisibilityMode, string>[] VisibilityModeList { get; } = [
        new(VisibilityMode.ShowInTaskbar, Strings.Option_Visibility_Show),
        new(VisibilityMode.HideInTaskbar, Strings.Option_Visibility_HideTaskbar),
        new(VisibilityMode.HideInTaskbarAndTaskSwitcher, Strings.Option_Visibility_HideAll)
    ];

    public static KeyValuePair<ColourMode, string>[] ColourModeList { get; } = [
        new(ColourMode.Light, Strings.Option_Colour_Light),
        new(ColourMode.Dark, Strings.Option_Colour_Dark),
        new(ColourMode.System, Strings.Option_Colour_System)
    ];

    public static KeyValuePair<TransparencyMode, string>[] TransparencyModeList { get; } = [
        new(TransparencyMode.Disabled, Strings.Option_Transparency_Disabled),
        new(TransparencyMode.Enabled, Strings.Option_Transparency_Enabled),
        new(TransparencyMode.WhenPinned, Strings.Option_Transparency_WhenPinned)
    ];

    public static KeyValuePair<string, string>[] FontFamilyList { get; } = [..
        new InstalledFontCollection().Families
                                     .Select(f => new KeyValuePair<string, string>(f.Name, f.Name))
                                     .OrderBy(f => f.Value, StringComparer.CurrentCulture)
    ];

    public static KeyValuePair<CopyAction, string>[] CopyActionList { get; } = [
        new(CopyAction.None, Strings.Option_None),
        new(CopyAction.CopySelected, Strings.Option_Copy_Selected),
        new(CopyAction.CopyLine, Strings.Option_Copy_Line),
        new(CopyAction.CopyAll, Strings.Option_Copy_All)
    ];

    public static KeyValuePair<PasteAction, string>[] PasteActionList { get; } = [
        new(PasteAction.None, Strings.Option_None),
        new(PasteAction.Paste, Strings.Option_Paste_Paste),
        new(PasteAction.PasteAndReplaceAll, Strings.Option_Paste_ReplaceAll),
        new(PasteAction.PasteAtEnd, Strings.Option_Paste_AtEnd)
    ];

    public static KeyValuePair<CopyFallbackAction, string>[] CopyFallbackActionList { get; } = [
        new(CopyFallbackAction.None, Strings.Option_None),
        new(CopyFallbackAction.CopyLine, Strings.Option_Copy_Line),
        new(CopyFallbackAction.CopyNote, Strings.Option_CopyFallback_Note)
    ];

    public static KeyValuePair<ToolState, string>[] ToolStateList { get; } = [
        new(ToolState.Disabled, Strings.Option_Tool_Disabled),
        new(ToolState.Enabled, Strings.Option_Tool_Enabled),
        new(ToolState.Favourite, Strings.Option_Tool_Favourite)
    ];
}
