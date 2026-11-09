namespace FlowMemo.WpfUi.Models;

/// <summary>비교 창에서 고른 처리 방법.</summary>
public enum SyncDecision
{
    /// <summary>결정하지 않음 (나중에).</summary>
    None,
    TakeRemote,
    KeepLocal,
    Merge,
    KeepBoth,
    Skip
}
