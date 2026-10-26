using ToolFleet.Domain;
using ToolFleet.Domain.Rollouts;

namespace ToolFleet.Api;

// Request and response shapes. Kept separate from domain entities so the API
// contract doesn't change every time an entity gains a field.

public sealed record LocationDto(Guid Id, string Name, string Country);
public sealed record CreateLocationRequest(string Name, string Country);

public sealed record ToolDto(
    Guid Id,
    string SerialNumber,
    string Model,
    Guid LocationId,
    string LocationName,
    string FirmwareVersion,
    DateOnly LastCalibratedOn,
    DateOnly CalibrationDueOn,
    CalibrationStatus CalibrationStatus)
{
    public static ToolDto From(Tool tool, string locationName, DateOnly today) => new(
        tool.Id,
        tool.SerialNumber,
        tool.Model,
        tool.LocationId,
        locationName,
        tool.FirmwareVersion,
        tool.LastCalibratedOn,
        tool.CalibrationDueOn,
        tool.GetCalibrationStatus(today));
}

public sealed record CreateToolRequest(
    string SerialNumber,
    string Model,
    Guid LocationId,
    string FirmwareVersion,
    DateOnly LastCalibratedOn,
    int CalibrationIntervalDays);

public sealed record RecordCalibrationRequest(DateOnly CalibratedOn);

public sealed record FirmwareReleaseDto(
    Guid Id, string Model, string Version, string Notes, DateTime PublishedAtUtc, FirmwareFileDto? File)
{
    public static FirmwareReleaseDto From(FirmwareRelease r) => new(
        r.Id, r.Model, r.Version, r.Notes, r.PublishedAtUtc,
        r.HasFile ? new FirmwareFileDto(r.FileName!, r.FileSizeBytes!.Value, r.FileSha256!) : null);
}

/// <summary>Null File on a release means it was never uploaded (the demo fleet's baseline builds).</summary>
public sealed record FirmwareFileDto(string FileName, long SizeBytes, string Sha256);

/// <summary>A download link that stops working at ExpiresAtUtc. Check the file against Sha256 before using it.</summary>
public sealed record FirmwareDownloadDto(string Url, DateTime ExpiresAtUtc, string FileName, long SizeBytes, string Sha256);

public sealed record CreateRolloutRequest(Guid FirmwareReleaseId, int CanaryPercent, int FailureThresholdPercent);

public sealed record ReportResultRequest(bool Succeeded, string? FailureReason);

public sealed record RolloutSummaryDto(
    Guid Id,
    string Model,
    string TargetVersion,
    RolloutStatus Status,
    DateTime CreatedAtUtc,
    StageProgress Canary,
    StageProgress Full)
{
    public static RolloutSummaryDto From(Rollout r) => new(
        r.Id, r.Model, r.TargetVersion, r.Status, r.CreatedAtUtc,
        r.Progress(RolloutStage.Canary), r.Progress(RolloutStage.Full));
}

public sealed record RolloutTargetDto(
    Guid ToolId,
    string SerialNumber,
    string LocationName,
    RolloutStage Stage,
    TargetStatus Status,
    string PreviousVersion,
    string? FailureReason,
    DateTime? ReportedAtUtc);

public sealed record RolloutDetailDto(
    Guid Id,
    Guid FirmwareReleaseId,
    string Model,
    string TargetVersion,
    int CanaryPercent,
    int FailureThresholdPercent,
    RolloutStatus Status,
    string? HaltReason,
    DateTime CreatedAtUtc,
    DateTime? StartedAtUtc,
    DateTime? FinishedAtUtc,
    StageProgress Canary,
    StageProgress Full,
    IReadOnlyList<RolloutTargetDto> Targets);

public sealed record AuditEntryDto(
    long Id,
    DateTime AtUtc,
    string Actor,
    string EntityType,
    string EntityId,
    string Action,
    string Changes);
