using FlowMemo.Core.Repositories;

namespace FlowMemo.Core.Migrations;

/// <summary>
/// v5 → v6: PC 간 동기화. 메모마다 고유 ID(SyncId)를 주고, 동기화 상태를 별도 테이블에 둔다.
/// 메모를 완전히 지워도 NoteSync 행은 남아서 "다른 PC에도 지웠다고 알릴 대상"이 된다.
/// </summary>
public class Schema5To6Migration : SchemaMigration
{
    public override int TargetSchemaVersion => 5;
    public override int ResultingSchemaVersion => 6;

    public override string UpdateQuery => $@"
        {SyncRepository.CreateTablesSql}

        INSERT INTO NoteSync (NoteId, SyncId)
        SELECT Id, {SyncRepository.NewSyncIdSql} FROM Notes;

        UPDATE SchemaInfo
        SET Version = {ResultingSchemaVersion}
        WHERE Id = 0;
    ";
}
