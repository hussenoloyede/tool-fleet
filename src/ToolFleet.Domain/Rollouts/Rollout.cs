namespace ToolFleet.Domain.Rollouts;

public enum RolloutStatus
{
    Draft,
    Canary,
    AwaitingPromotion,
    Full,
    Completed,
    Halted,
    Aborted,
}

public sealed record StageProgress(int Total, int Succeeded, int Failed, int Pending);

/// <summary>
/// A staged firmware rollout. All state changes go through methods on this class,
/// so the rules in docs/mvp-scope.md are enforced in one place:
///
///   Draft -> Canary -> AwaitingPromotion -> Full -> Completed
///   Canary or Full -> Halted (failure rate in the stage exceeds the threshold)
///   any non-final state -> Aborted
/// </summary>
public class Rollout
{
    public const int MaxCanaryPercent = 50;

    private readonly List<RolloutTarget> _targets = [];

    private Rollout() { } // for EF Core

    /// <summary>
    /// Creates a draft rollout. <paramref name="candidateTools"/> should already be shuffled:
    /// the first N eligible tools become the canary group. Keeping randomness out of the
    /// domain makes these rules deterministic to test.
    /// </summary>
    public static Rollout Create(
        FirmwareRelease release,
        IEnumerable<Tool> candidateTools,
        int canaryPercent,
        int failureThresholdPercent,
        DateTime nowUtc)
    {
        if (canaryPercent is < 1 or > MaxCanaryPercent)
            throw new DomainException($"Canary percent must be between 1 and {MaxCanaryPercent}.");
        if (failureThresholdPercent is < 0 or > 100)
            throw new DomainException("Failure threshold must be between 0 and 100 percent.");

        var eligible = candidateTools
            .Where(t => t.Model == release.Model && t.FirmwareVersion != release.Version)
            .ToList();

        if (eligible.Count == 0)
            throw new DomainException($"No {release.Model} tools need firmware {release.Version}.");

        var rollout = new Rollout
        {
            Id = Guid.NewGuid(),
            FirmwareReleaseId = release.Id,
            Model = release.Model,
            TargetVersion = release.Version,
            CanaryPercent = canaryPercent,
            FailureThresholdPercent = failureThresholdPercent,
            Status = RolloutStatus.Draft,
            CreatedAtUtc = nowUtc,
        };

        var canaryCount = CanarySize(eligible.Count, canaryPercent);
        for (var i = 0; i < eligible.Count; i++)
        {
            var stage = i < canaryCount ? RolloutStage.Canary : RolloutStage.Full;
            rollout._targets.Add(new RolloutTarget(rollout.Id, eligible[i], stage));
        }

        return rollout;
    }

    /// <summary>Percentage of the fleet, rounded up, never fewer than one tool.</summary>
    public static int CanarySize(int targetCount, int canaryPercent) =>
        Math.Max(1, (int)Math.Ceiling(targetCount * canaryPercent / 100.0));

    public Guid Id { get; private set; }
    public Guid FirmwareReleaseId { get; private set; }
    public string Model { get; private set; } = "";
    public string TargetVersion { get; private set; } = "";
    public int CanaryPercent { get; private set; }
    public int FailureThresholdPercent { get; private set; }
    public RolloutStatus Status { get; private set; }
    public string? HaltReason { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }
    public DateTime? StartedAtUtc { get; private set; }
    public DateTime? FinishedAtUtc { get; private set; }

    public IReadOnlyList<RolloutTarget> Targets => _targets;

    public bool IsFinished => Status is RolloutStatus.Completed or RolloutStatus.Halted or RolloutStatus.Aborted;

    public StageProgress Progress(RolloutStage stage)
    {
        var inStage = _targets.Where(t => t.Stage == stage).ToList();
        return new StageProgress(
            inStage.Count,
            inStage.Count(t => t.Status == TargetStatus.Succeeded),
            inStage.Count(t => t.Status == TargetStatus.Failed),
            inStage.Count(t => t.Status == TargetStatus.Pending));
    }

    public void Start(DateTime nowUtc)
    {
        if (Status != RolloutStatus.Draft)
            throw new DomainException($"Only a draft rollout can be started (this one is {Status}).");

        Status = RolloutStatus.Canary;
        StartedAtUtc = nowUtc;
    }

    public void Promote()
    {
        if (Status != RolloutStatus.AwaitingPromotion)
            throw new DomainException($"Only a rollout awaiting promotion can be promoted (this one is {Status}).");

        Status = RolloutStatus.Full;
    }

    public void Abort(DateTime nowUtc)
    {
        if (IsFinished)
            throw new DomainException($"The rollout is already {Status}.");

        Status = RolloutStatus.Aborted;
        FinishedAtUtc = nowUtc;
    }

    /// <summary>
    /// Records the outcome of flashing one tool. On success the tool's firmware version changes.
    /// The rollout then halts if failures in the active stage exceed the threshold, or moves on
    /// once every tool in the stage has reported.
    /// </summary>
    public void RecordResult(Tool tool, bool succeeded, string? failureReason, DateTime nowUtc)
    {
        var activeStage = Status switch
        {
            RolloutStatus.Canary => RolloutStage.Canary,
            RolloutStatus.Full => RolloutStage.Full,
            _ => throw new DomainException($"Results can only be reported while a stage is running (this rollout is {Status})."),
        };

        var target = _targets.SingleOrDefault(t => t.ToolId == tool.Id)
            ?? throw new DomainException($"Tool {tool.SerialNumber} is not part of this rollout.");

        if (target.Stage != activeStage)
            throw new DomainException($"Tool {tool.SerialNumber} is in the {target.Stage} stage, which is not running.");
        if (target.Status != TargetStatus.Pending)
            throw new DomainException($"Tool {tool.SerialNumber} has already reported ({target.Status}).");

        if (succeeded)
        {
            target.MarkSucceeded(nowUtc);
            tool.ApplyFirmware(TargetVersion);
        }
        else
        {
            target.MarkFailed(failureReason, nowUtc);
        }

        EvaluateStage(activeStage, nowUtc);
    }

    private void EvaluateStage(RolloutStage stage, DateTime nowUtc)
    {
        var progress = Progress(stage);

        // Integer maths avoids rounding surprises: failed/total > threshold/100.
        if (progress.Failed * 100 > FailureThresholdPercent * progress.Total)
        {
            Status = RolloutStatus.Halted;
            HaltReason = $"{progress.Failed} of {progress.Total} tools failed in the {stage} stage, " +
                         $"above the {FailureThresholdPercent}% threshold.";
            FinishedAtUtc = nowUtc;
            return;
        }

        if (progress.Pending > 0) return;

        if (stage == RolloutStage.Canary && Progress(RolloutStage.Full).Total > 0)
        {
            Status = RolloutStatus.AwaitingPromotion;
        }
        else
        {
            Status = RolloutStatus.Completed;
            FinishedAtUtc = nowUtc;
        }
    }
}
