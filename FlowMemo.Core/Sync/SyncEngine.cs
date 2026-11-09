using System.Security.Cryptography;
using System.Text.Json;

using FlowMemo.Core.Repositories;

namespace FlowMemo.Core.Sync;

/// <summary>
/// 동기화 한 주기: 이 PC의 변경을 올리고 다른 PC의 변경을 받는다.
/// - 받은 내용은 메모를 바꾸지 않고 '대기'로만 저장한다 (가져올지는 사용자가 정한다).
/// - 이 PC의 원본은 항상 SQLite이고, 서버가 없어도 메모는 그대로 동작한다.
/// </summary>
public sealed class SyncEngine(ISyncApi api, SyncRepository repository, SyncCrypto crypto)
{
    private const int MaxBlobLength = 1_400_000; // 서버 제한(1.5MB)보다 조금 작게

    public async Task<SyncCycleResult> RunCycle()
    {
        SyncCycleResult result = new();

        await Push(result);
        await Pull(result);

        return result;
    }

    private async Task Push(SyncCycleResult result)
    {
        foreach (NoteSyncRow row in await repository.GetAll())
        {
            // 사용자가 결정하기 전에는 올리지 않는다 (올리면 다른 PC의 변경을 덮어쓴다).
            if (row.HasPending)
            {
                // 직접 고치다 보니 받은 내용과 똑같아졌다면 더 물어볼 것이 없다.
                if (row.HasNote && !row.InTrash && !row.PendingDeleted && row.Content == row.PendingContent)
                {
                    await repository.CommitPending(row.NoteId);
                    result.ChangedNotes.Add(row.NoteId);
                }
                continue;
            }

            if (!row.HasNote || row.InTrash)
                await PushDeletion(row, result);
            else
                await PushContent(row, result);
        }
    }

    private async Task PushDeletion(NoteSyncRow row, SyncCycleResult result)
    {
        bool alreadyGone = row.BaseRevision == 0 || row.BaseDeleted;
        if (alreadyGone)
        {
            if (!row.HasNote)
                await repository.RemoveRow(row.NoteId);
            return;
        }

        SyncWriteResult write = await api.DeleteNote(row.SyncId, row.BaseRevision);
        if (write.Conflict)
        {
            if (write.Current is not null)
                await ApplyRemote(write.Current, result);
            return;
        }

        if (row.HasNote)
            await repository.SetBase(row.NoteId, write.Revision, row.Content, row.Colour, deleted: true);
        else
            await repository.RemoveRow(row.NoteId);
    }

    private async Task PushContent(NoteSyncRow row, SyncCycleResult result)
    {
        bool dirty = row.BaseRevision == 0
            ? !string.IsNullOrWhiteSpace(row.Content) // 아직 아무것도 안 쓴 새 메모는 올리지 않는다.
            : row.IsContentDirty || row.Colour != row.BaseColour || row.BaseDeleted;
        if (!dirty)
            return;

        string colour = row.Colour ?? "";
        string blob = crypto.Encrypt(JsonSerializer.Serialize(new SyncNotePayload(row.Content, colour, null, DateTime.UtcNow)), row.SyncId);
        if (blob.Length > MaxBlobLength)
        {
            result.TooLargeNotes++;
            return;
        }

        SyncWriteResult write = await api.PutNote(row.SyncId, row.BaseRevision, blob);
        if (write.Conflict)
        {
            if (write.Current is not null)
                await ApplyRemote(write.Current, result);
            return;
        }

        // 보낸 내용을 기준으로 삼는다. 그 사이 더 고쳤으면 다음 주기에 또 올라간다.
        await repository.SetBase(row.NoteId, write.Revision, row.Content, colour, deleted: false);
        result.ChangedNotes.Add(row.NoteId);
    }

    private async Task Pull(SyncCycleResult result)
    {
        long cursor = await repository.GetCursor();
        bool more = true;

        while (more)
        {
            SyncChangesPage page = await api.GetChanges(cursor);
            result.Presence = page.Presence;
            more = page.More;

            foreach (SyncRemoteNote change in page.Changes)
            {
                await ApplyRemote(change, result);
                cursor = Math.Max(cursor, change.Revision);
            }

            await repository.SetCursor(cursor);
        }
    }

    private async Task ApplyRemote(SyncRemoteNote change, SyncCycleResult result)
    {
        NoteSyncRow? row = await repository.GetBySyncId(change.Id);

        if (row is not null && (change.Revision <= row.BaseRevision || change.Revision <= row.PendingRevision))
            return;

        if (change.Deleted)
        {
            await ApplyRemoteDeletion(change, row, result);
            return;
        }

        SyncNotePayload? payload = Decrypt(change, result);
        if (payload is null)
            return;

        if (row is null)
        {
            await InsertRemote(change, payload, result);
            return;
        }

        if (!row.HasNote)
        {
            // 이 PC에서 완전히 지웠는데 다른 PC가 그 뒤에 고쳤다. 수정이 삭제보다 우선이다.
            await repository.RemoveRow(row.NoteId);
            await InsertRemote(change, payload, result);
            return;
        }

        if (row.InTrash)
        {
            await repository.RestoreFromRemote(row.NoteId, change.Revision, payload);
            result.AddedNotes.Add(row.NoteId);
            return;
        }

        bool contentSameAsHere = payload.Content == row.Content;
        bool contentSameAsBase = row.BaseRevision > 0 && payload.Content == row.BaseContent;
        if (contentSameAsHere || contentSameAsBase)
        {
            // 내용 차이가 없으면 물어볼 것이 없다. 색이 바뀌었고 이 PC에서 색을 안 바꿨으면 그대로 따른다.
            bool followColour = payload.Colour != row.Colour && row.Colour == row.BaseColour;
            if (followColour)
            {
                await repository.UpdateColour(row.NoteId, payload.Colour);
                result.ColourChanges[row.NoteId] = payload.Colour;
            }

            // 내용이 기준과 같다면(색만 바뀐 변경) 이 PC에서 고친 내용은 그대로 두어 다음 주기에 올라가게 한다.
            await repository.SetBase(row.NoteId, change.Revision, payload.Content, payload.Colour, deleted: false);
            result.ChangedNotes.Add(row.NoteId);
            return;
        }

        await repository.SetPending(row.NoteId, change.Revision, payload.Content, payload.Colour, deleted: false, change.DeviceName, change.UpdatedAt);
        result.ChangedNotes.Add(row.NoteId);
    }

    private async Task ApplyRemoteDeletion(SyncRemoteNote change, NoteSyncRow? row, SyncCycleResult result)
    {
        if (row is null)
            return; // 이 PC가 모르는 메모는 받을 것이 없다.

        if (!row.HasNote)
        {
            await repository.RemoveRow(row.NoteId);
            return;
        }

        if (row.InTrash)
        {
            await repository.SetBase(row.NoteId, change.Revision, row.Content, row.Colour, deleted: true);
            return;
        }

        await repository.SetPending(row.NoteId, change.Revision, null, null, deleted: true, change.DeviceName, change.UpdatedAt);
        result.ChangedNotes.Add(row.NoteId);
    }

    private async Task InsertRemote(SyncRemoteNote change, SyncNotePayload payload, SyncCycleResult result)
    {
        // 위치는 PC마다 다르므로 동기화하지 않는다. 처음 열 때 겹치지 않게 조금씩 비켜 놓는다.
        int offset = (int)(change.Revision % 10) * 30;
        int noteId = await repository.InsertRemoteNote(change.Id, change.Revision, payload, 100 + offset, 100 + offset, 300, 300);
        result.AddedNotes.Add(noteId);
    }

    private SyncNotePayload? Decrypt(SyncRemoteNote change, SyncCycleResult result)
    {
        try
        {
            return JsonSerializer.Deserialize<SyncNotePayload>(crypto.Decrypt(change.Blob, change.Id));
        }
        catch (Exception ex) when (ex is CryptographicException or FormatException or JsonException)
        {
            result.UnreadableChanges++;
            return null;
        }
    }
}
