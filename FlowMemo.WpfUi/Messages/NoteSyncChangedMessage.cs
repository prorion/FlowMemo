namespace FlowMemo.WpfUi.Messages;

/// <summary>메모의 동기화 상태가 바뀌었다. NewColour가 있으면 다른 PC에서 바꾼 색을 적용한다.</summary>
public record NoteSyncChangedMessage(int NoteId, string? NewColour = null);

/// <summary>다른 PC에서 만든 메모가 들어왔다 (관리 창 목록을 다시 읽는다).</summary>
public record SyncNotesAddedMessage();
