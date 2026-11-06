using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows;
using System.Windows.Input;

using FlowMemo.Core.DataTransferObjects;
using FlowMemo.Core.Enums;
using FlowMemo.Core.Repositories;
using FlowMemo.WpfUi.Commands;
using FlowMemo.WpfUi.Messages;
using FlowMemo.WpfUi.Models;
using FlowMemo.WpfUi.Resources;
using FlowMemo.WpfUi.Services;
using FlowMemo.WpfUi.Themes;

namespace FlowMemo.WpfUi.ViewModels;

public class ManagementViewModel : BaseViewModel
{
    private readonly NoteRepository _noteRepository;
    private readonly ThemeService _themeService;
    private readonly WindowService _windowService;

    private readonly Dictionary<int, int> _notePreviewIdIndexMap = []; // Use this since we don't have a Observable Dictionary
    private readonly Dictionary<int, NotePreviewModel> _selectedNotePreviews = [];

    public ManagementViewModel(
        NoteRepository noteRepository,
        AppMetadataService appMetadataService,
        SettingsService settingsService,
        MessengerService messengerService,
        ThemeService themeService,
        WindowService windowService
    ) : base(appMetadataService, settingsService, messengerService)
    {
        _noteRepository = noteRepository;
        _themeService = themeService;
        _windowService = windowService;

        NewNoteCommand = new RelayCommand(OnNewNoteCommand);
        OpenSettingsCommand = new RelayCommand(OnOpenSettingsCommand);

        OpenNotesCommand = new RelayCommand(OnOpenNotesCommand);
        CloseNotesCommand = new RelayCommand(OnCloseNotesCommand);
        // 선택한 메모가 없으면 '메모 삭제'를 비활성으로 둔다 (예전에는 눌러도 아무 일도 없었다).
        DeleteNotesCommand = new RelayCommand(OnDeleteNotesCommand, () => _selectedNotePreviews.Count > 0);

        // 명령을 만든 뒤에 불러온다 (불러오는 중에 DeleteNotesCommand를 쓸 수 있다).
        LoadNotes();

        MessengerService.Subscribe<NoteActionMessage>(OnNoteActionMessage);
    }

    public ICommand NewNoteCommand { get; }
    public ICommand OpenSettingsCommand { get; }

    public ICommand OpenNotesCommand { get; }
    public ICommand CloseNotesCommand { get; }
    public RelayCommand DeleteNotesCommand { get; }

    public ObservableCollection<NotePreviewModel> NotePreviews { get; } = [];

    private async void LoadNotes()
    {
        ColourMode colourMode = SettingsService.NoteSettings.ColourMode;

        ClearNotePreviews();

        IEnumerable<NoteDto> noteDtos = await _noteRepository.GetAll();
        foreach (NoteDto noteDto in noteDtos)
        {
            AddNotePreview(noteDto, colourMode);
            // DB의 IsOpen은 '다음 시작 때 다시 열지'라서, 지금 실제로 창이 열려 있는지로 바꿔 보여 준다.
            NotePreviews[^1].IsOpen = _windowService.IsNoteWindowOpen(noteDto.Id);
        }
    }

    private void OnNoteActionMessage(NoteActionMessage message)
    {
        switch (message.Action)
        {
            case NoteAction.Created:
                AddNotePreview(message.NoteDto);
                break;
            case NoteAction.Updated:
                UpdatedNotePreview(message.NoteDto);
                break;
            case NoteAction.Closed:
                if (_notePreviewIdIndexMap.TryGetValue(message.NoteDto.Id, out int closedIndex))
                    NotePreviews[closedIndex].IsOpen = message.NoteDto.IsOpen;
                break;
            case NoteAction.Deleted:
                RemoveNotePreview(message.NoteDto.Id);
                break;
        }
    }

    /// <summary>관리 창이 닫힐 때 호출한다. 구독을 풀지 않으면 닫힌 창의 뷰모델이 계속 메시지를 받는다.</summary>
    public void Detach()
        => MessengerService.Unsubscribe<NoteActionMessage>(OnNoteActionMessage);

    private void UpdatedNotePreview(NoteDto dto)
    {
        if (!_notePreviewIdIndexMap.TryGetValue(dto.Id, out int index))
            return;

        NotePreviewModel notePreview = NotePreviews[index];

        notePreview.ContentPreview = dto.Content;
        notePreview.ThemeColourScheme = dto.ThemeColourScheme;
        notePreview.IsOpen = dto.IsOpen;

        UpdateNotePreviewBrushes(notePreview);
    }

    private void UpdateNotePreviewBrushes(NotePreviewModel notePreview, ColourMode? colourMode = null)
    {
        Palette palette = _themeService.GetPalette(
            notePreview.ThemeColourScheme,
            colourMode ?? SettingsService.NoteSettings.ColourMode
        );

        notePreview.UpdateBrushes(palette);
    }

    private void OnNewNoteCommand()
    {
        MessengerService.Publish(new OpenNoteWindowMessage(isManagementWindowParent: true));
    }

    private void OnOpenSettingsCommand()
    {
        MessengerService.Publish(new OpenSettingsWindowMessage());
    }

    private void OnOpenNotesCommand()
    {
        List<int> selectedNoteIds = [.._selectedNotePreviews.Keys]; // Selected
        if (selectedNoteIds.Count == 0)
            selectedNoteIds = [.._notePreviewIdIndexMap.Keys]; // All

        MessengerService.Publish(new MultipleNoteWindowActionMessage(selectedNoteIds, NoteWindowAction.Open));
    }

    private void OnCloseNotesCommand()
    {
        List<int> selectedNoteIds = [.._selectedNotePreviews.Keys]; // Selected
        if (selectedNoteIds.Count == 0)
            selectedNoteIds = [.._notePreviewIdIndexMap.Keys]; // All

        MessengerService.Publish(new MultipleNoteWindowActionMessage(selectedNoteIds, NoteWindowAction.Close));
    }

    private async void OnDeleteNotesCommand()
    {
        List<int> selectedNoteIds = [.._selectedNotePreviews.Keys]; // Selected
        if (selectedNoteIds.Count == 0)
            return; // No delete all

        // 삭제는 되돌릴 수 없으므로 한 번 확인한다.
        MessageBoxResult answer = MessageBox.Show(
            string.Format(Strings.Management_DeleteConfirm_Message, selectedNoteIds.Count),
            Strings.Management_DeleteConfirm_Title,
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning,
            MessageBoxResult.No
        );
        if (answer != MessageBoxResult.Yes)
            return;

        MessengerService.Publish(new MultipleNoteWindowActionMessage(selectedNoteIds, NoteWindowAction.Close));

        foreach (int noteId in selectedNoteIds)
        {
            if (!_notePreviewIdIndexMap.ContainsKey(noteId))
                continue; // Note may have been deleted due to being empty when closed above

            RemoveNotePreview(noteId);
            await _noteRepository.Delete(noteId);
        }
    }

    private void NotePreview_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (sender is not NotePreviewModel notePreview)
            return;

        switch (e.PropertyName)
        {
            case nameof(NotePreviewModel.IsSelected):

                if (notePreview.IsSelected && !_selectedNotePreviews.ContainsKey(notePreview.Id))
                    _selectedNotePreviews[notePreview.Id] = notePreview;
                else if (!notePreview.IsSelected && _selectedNotePreviews.ContainsKey(notePreview.Id))
                    _selectedNotePreviews.Remove(notePreview.Id);

                DeleteNotesCommand.RaiseCanExecuteChanged();

                break;
        }
    }

    private void ClearNotePreviews()
    {
        foreach (NotePreviewModel notePreview in NotePreviews)
            notePreview.PropertyChanged -= NotePreview_PropertyChanged;

        NotePreviews.Clear();
        _notePreviewIdIndexMap.Clear();
        _selectedNotePreviews.Clear();
    }

    private void AddNotePreview(NoteDto dto, ColourMode? colourMode = null)
    {
        NotePreviewModel notePreview = new(dto);

        UpdateNotePreviewBrushes(notePreview, colourMode);

        notePreview.PropertyChanged += NotePreview_PropertyChanged;

        NotePreviews.Add(notePreview);
        _notePreviewIdIndexMap[dto.Id] = NotePreviews.Count - 1;
        if (notePreview.IsSelected)
            _selectedNotePreviews[notePreview.Id] = notePreview;
    }

    private void RemoveNotePreview(int noteId)
    {
        if (!_notePreviewIdIndexMap.TryGetValue(noteId, out int index))
            return;

        NotePreviewModel notePreview = NotePreviews[index];

        notePreview.PropertyChanged -= NotePreview_PropertyChanged;

        NotePreviews.Remove(notePreview);
        _selectedNotePreviews.Remove(notePreview.Id);
        DeleteNotesCommand.RaiseCanExecuteChanged();

        // Rebuild map not indexes will have changed
        _notePreviewIdIndexMap.Clear();
        for (int i = 0; i < NotePreviews.Count; i++)
            _notePreviewIdIndexMap[NotePreviews[i].Id] = i;
    }
}
