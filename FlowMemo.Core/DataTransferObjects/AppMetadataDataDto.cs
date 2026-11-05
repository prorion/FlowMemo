namespace FlowMemo.Core.DataTransferObjects;

public record AppMetadataDataDto(
    int Id,

    long? LastUpdateCheck,
    string? ColourScheme
);
