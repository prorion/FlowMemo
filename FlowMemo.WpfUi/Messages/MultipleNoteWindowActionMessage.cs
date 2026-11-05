using FlowMemo.Core.Enums;

namespace FlowMemo.WpfUi.Messages;

public record MultipleNoteWindowActionMessage(
    IEnumerable<int> NoteIds,
    NoteWindowAction Action
);
