using FlowMemo.Core.DataTransferObjects;
using FlowMemo.Core.Enums;

namespace FlowMemo.WpfUi.Messages;

public record NoteActionMessage(
    NoteAction Action,
    NoteDto NoteDto
);
