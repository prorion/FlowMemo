using Microsoft.Extensions.DependencyInjection;
using FlowMemo.Core.DataTransferObjects;
using FlowMemo.Core.Enums;
using FlowMemo.Core.Repositories;
using FlowMemo.WpfUi.Helpers;
using FlowMemo.WpfUi.Messages;
using FlowMemo.WpfUi.Models;
using FlowMemo.WpfUi.ViewModels;
using FlowMemo.WpfUi.Views;

namespace FlowMemo.WpfUi.Services;

public class WindowService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly MessengerService _messengerService;
    private readonly AppMetadataService _appMetadataService;
    private readonly SettingsService _settingsService;
    private readonly NoteRepository _noteRepository;
    private readonly ThemeService _themeService;

    private readonly Dictionary<int, NoteWindow> _openNoteWindows = [];
    private SettingsWindow? _settingsWindow;
    private ManagementWindow? _managementWindow;

    public WindowService(
        IServiceProvider serviceProvider,
        MessengerService messengerService,
        AppMetadataService appMetadataService,
        SettingsService settingsService,
        NoteRepository noteRepository,
        ThemeService themeService
    )
    {
        _serviceProvider = serviceProvider;
        _messengerService = messengerService;
        _appMetadataService = appMetadataService;
        _settingsService = settingsService;
        _noteRepository = noteRepository;
        _themeService = themeService;

        _messengerService.Subscribe<ApplicationActionMessage>(OnApplicationActionMessage);

        _messengerService.Subscribe<OpenNoteWindowMessage>(OnOpenNoteWindowMessage);
        _messengerService.Subscribe<MultipleNoteWindowActionMessage>(OnMultipleNoteWindowActionMessage);
        _messengerService.Subscribe<NoteActionMessage>(OnNoteActionMessage);

        _messengerService.Subscribe<OpenSettingsWindowMessage>(OnOpenSettingsWindowMessage);

        _messengerService.Subscribe<OpenManagementWindowMessage>(OnOpenManagementWindowMessage);
    }

    /// <summary>지금 창이 열려 있는 메모인지. (DB의 IsOpen은 '다음 시작 때 다시 열지'라서 실제와 다를 수 있다.)</summary>
    public bool IsNoteWindowOpen(int noteId)
        => _openNoteWindows.ContainsKey(noteId);

    /// <summary>열린 메모를 모두 바로 저장한다 (앱 종료, Windows 종료 때).</summary>
    public void SaveAllOpenNotes()
    {
        foreach (NoteWindow window in _openNoteWindows.Values.ToList())
        {
            try
            {
                window.SaveNow();
            }
            catch (Exception ex)
            {
                ErrorReporter.Log(ex, "SaveAllOpenNotes");
            }
        }
    }

    private void OnApplicationActionMessage(ApplicationActionMessage message)
    {
        // TO DO: Add settings for this behaviour.
        switch (message.Action)
        {
            case ApplicationAction.Start:
                ErrorReporter.FireAndForget(DoStartUpAction(), "Startup");
                break;
            case ApplicationAction.NewInstance:
                DoNewInstanceAction();
                break;
        }
    }

    private async Task DoStartUpAction()
    {
        switch (_settingsService.ApplicationSettings.StartupBehaviour)
        {
            case StartupBehaviour.RestoreOpenNotes:

                IEnumerable<NoteDto> openNotes = await _noteRepository.GetOpen();
                if (!openNotes.Any()) // Fallback to new instance action if no notes open
                {
                    DoNewInstanceAction();
                    return;
                }

                foreach (NoteDto note in openNotes)
                    OpenNoteWindowSafe(note.Id);

                break;
            case StartupBehaviour.CreateNewNote:
                OpenNoteWindowSafe();
                break;
            case StartupBehaviour.ShowManagementWindow:
                OpenManagementWindow();
                break;
        }
    }

    private void DoNewInstanceAction()
    {
        switch (_settingsService.ApplicationSettings.NewInstanceBehaviour)
        {
            case NewInstanceBehaviour.CreateNewNote:
                OpenNoteWindowSafe();
                break;
            case NewInstanceBehaviour.ShowManagementWindow:
                OpenManagementWindow();
                break;
        }
    }

    private void OnOpenNoteWindowMessage(OpenNoteWindowMessage message)
    {
        nint? managementWindowHandle = null;
        if (_managementWindow is not null && message.isManagementWindowParent)
            managementWindowHandle = ScreenHelper.GetWindowHandle(_managementWindow);

        OpenNoteWindowSafe(message.NoteId, message.ParentNote, managementWindowHandle);
    }

    private void OnMultipleNoteWindowActionMessage(MultipleNoteWindowActionMessage message)
    {
        foreach (int noteId in message.NoteIds)
        {
            switch (message.Action)
            {
                case NoteWindowAction.Open:
                    OpenNoteWindowSafe(noteId);
                    break;
                case NoteWindowAction.Close:
                    CloseNoteWindow(noteId);
                    break;
            }
        }
    }

    private void OnNoteActionMessage(NoteActionMessage message)
    {
        switch (message.Action)
        {
            case NoteAction.Closed:
                _openNoteWindows.Remove(message.NoteDto.Id);
                break;
        }
    }

    private void OnOpenSettingsWindowMessage(OpenSettingsWindowMessage message)
    {
        if (_settingsWindow is null || !_settingsWindow.IsLoaded)
        {
            _settingsWindow = _serviceProvider.GetRequiredService<SettingsWindow>();
            _settingsWindow.Closed += (s, e) => _settingsWindow = null;
        }

        _settingsWindow.SetOwnerAndPosition(message.Owner);

        if (!_settingsWindow.IsVisible)
            _settingsWindow.Show();

        _settingsWindow.Activate();
    }

    private void OnOpenManagementWindowMessage(OpenManagementWindowMessage message)
        => OpenManagementWindow();

    // 예전에는 `_ = OpenNoteWindow()`로 호출해서, 창을 만들다 예외가 나면 아무 표시 없이 사라졌다
    // (한국어 Windows에서 노트 창이 하나도 안 열리던 원인).
    private void OpenNoteWindowSafe(int? noteId = null, NoteModel? parentNote = null, nint? managementWindowHandle = null)
        => ErrorReporter.FireAndForget(OpenNoteWindow(noteId, parentNote, managementWindowHandle), "OpenNoteWindow");

    private async Task OpenNoteWindow(int? noteId = null, NoteModel? parentNote = null, nint? managementWindowHandle = null)
    {
        if (noteId is not null && _openNoteWindows.TryGetValue((int)noteId, out NoteWindow? existingWindow))
        {
            existingWindow.ActivateNote();
            return;
        }

        NoteViewModel viewModel = new(_noteRepository, _appMetadataService, _settingsService, _messengerService, _themeService);
        await viewModel.Initialize(noteId, parentNote, managementWindowHandle);

        NoteWindow window = new(_settingsService, _messengerService, _themeService, viewModel);
        int id = viewModel.Note.Id;
        window.Closed += (s, e) =>
        {
            if (_openNoteWindows.TryGetValue(id, out NoteWindow? tracked) && ReferenceEquals(tracked, window))
                _openNoteWindows.Remove(id);
        };

        _openNoteWindows[id] = window;
        window.Show();
    }

    private void CloseNoteWindow(int noteId)
    {
        if (_openNoteWindows.TryGetValue(noteId, out NoteWindow? window) && window.IsLoaded)
        {
            window.Close();
            _openNoteWindows.Remove(noteId);
        }
    }

    private void OpenManagementWindow()
    {
        if (_managementWindow is null || !_managementWindow.IsLoaded)
        {
            _managementWindow = _serviceProvider.GetRequiredService<ManagementWindow>();
            // 예전 코드는 여기서 _settingsWindow를 비웠다 (원본 Pinny Notes의 오타).
            _managementWindow.Closed += (s, e) => _managementWindow = null;
        }

        if (!_managementWindow.IsVisible)
            _managementWindow.Show();

        if (_managementWindow.WindowState == System.Windows.WindowState.Minimized)
            _managementWindow.WindowState = System.Windows.WindowState.Normal;

        _managementWindow.Activate();
    }
}
