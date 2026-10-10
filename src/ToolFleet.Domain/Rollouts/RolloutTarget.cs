namespace ToolFleet.Domain.Rollouts;

public enum RolloutStage
{
    Canary,
    Full,
}

public enum TargetStatus
{
    Pending,
    Succeeded,
    Failed,
}

/// <summary>One tool's place in a rollout. Created and changed only through <see cref="Rollout"/>.</summary>
public class RolloutTarget
{
    private RolloutTarget() { } // for EF Core

    internal RolloutTarget(Guid rolloutId, Tool tool, RolloutStage stage)
    {
        Id = Guid.NewGuid();
        RolloutId = rolloutId;
        ToolId = tool.Id;
        Stage = stage;
        PreviousVersion = tool.FirmwareVersion;
        Status = TargetStatus.Pending;
    }

    public Guid Id { get; private set; }
    public Guid RolloutId { get; private set; }
    public Guid ToolId { get; private set; }
    public RolloutStage Stage { get; private set; }
    public TargetStatus Status { get; private set; }

    /// <summary>Kept so a future rollback feature knows what to restore.</summary>
    public string PreviousVersion { get; private set; } = "";

    public string? FailureReason { get; private set; }
    public DateTime? ReportedAtUtc { get; private set; }

    internal void MarkSucceeded(DateTime nowUtc)
    {
        Status = TargetStatus.Succeeded;
        ReportedAtUtc = nowUtc;
    }

    internal void MarkFailed(string? reason, DateTime nowUtc)
    {
        Status = TargetStatus.Failed;
        FailureReason = string.IsNullOrWhiteSpace(reason) ? "No reason given" : reason.Trim();
        ReportedAtUtc = nowUtc;
    }
}
