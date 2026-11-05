using FlowMemo.WpfUi.Models;

namespace FlowMemo.WpfUi.Messages;

public record OpenNoteWindowMessage(
    int? NoteId = null,
    NoteModel? ParentNote = null,
    bool isManagementWindowParent = false
);
