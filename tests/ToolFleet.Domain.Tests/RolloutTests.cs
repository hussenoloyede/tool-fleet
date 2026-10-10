using ToolFleet.Domain.Rollouts;
using static ToolFleet.Domain.Tests.Builders;

namespace ToolFleet.Domain.Tests;

public class RolloutTests
{
    private static (Rollout Rollout, List<Tool> Fleet) StartedRollout(
        int fleetSize = 20, int canaryPercent = 10, int threshold = 20)
    {
        var fleet = Fleet(fleetSize);
        var rollout = Rollout.Create(Release(), fleet, canaryPercent, threshold, Now);
        rollout.Start(Now);
        return (rollout, fleet);
    }

    private static List<Tool> ToolsIn(Rollout rollout, List<Tool> fleet, RolloutStage stage) =>
        rollout.Targets
            .Where(t => t.Stage == stage)
            .Select(t => fleet.Single(tool => tool.Id == t.ToolId))
            .ToList();

    [Theory]
    [InlineData(20, 10, 2)]
    [InlineData(25, 10, 3)]   // 2.5 rounds up
    [InlineData(3, 10, 1)]    // never fewer than one
    [InlineData(1, 50, 1)]
    public void Canary_size_rounds_up_with_a_minimum_of_one(int fleetSize, int percent, int expected)
    {
        Assert.Equal(expected, Rollout.CanarySize(fleetSize, percent));
    }

    [Fact]
    public void Create_targets_only_tools_of_the_model_that_need_the_update()
    {
        var fleet = Fleet(4);
        fleet.Add(Tool(serial: "MP-0005", firmware: "4.2.0"));           // already up to date
        fleet.Add(Tool(serial: "GR-0001", model: "Gamma Ray Sub"));      // different model

        var rollout = Rollout.Create(Release(), fleet, 25, 20, Now);

        Assert.Equal(4, rollout.Targets.Count);
        Assert.Equal(RolloutStatus.Draft, rollout.Status);
        Assert.Equal(1, rollout.Progress(RolloutStage.Canary).Total);
        Assert.Equal(3, rollout.Progress(RolloutStage.Full).Total);
    }

    [Fact]
    public void Create_fails_when_no_tool_needs_the_update()
    {
        var fleet = Fleet(3, firmware: "4.2.0");

        Assert.Throws<DomainException>(() => Rollout.Create(Release(), fleet, 10, 20, Now));
    }

    [Theory]
    [InlineData(0, 20)]
    [InlineData(51, 20)]
    [InlineData(10, -1)]
    [InlineData(10, 101)]
    public void Create_rejects_out_of_range_settings(int canaryPercent, int threshold)
    {
        Assert.Throws<DomainException>(() => Rollout.Create(Release(), Fleet(10), canaryPercent, threshold, Now));
    }

    [Fact]
    public void Successful_canary_waits_for_promotion_and_updates_firmware()
    {
        var (rollout, fleet) = StartedRollout();

        foreach (var tool in ToolsIn(rollout, fleet, RolloutStage.Canary))
            rollout.RecordResult(tool, succeeded: true, null, Now);

        Assert.Equal(RolloutStatus.AwaitingPromotion, rollout.Status);
        Assert.All(ToolsIn(rollout, fleet, RolloutStage.Canary), t => Assert.Equal("4.2.0", t.FirmwareVersion));
        Assert.All(ToolsIn(rollout, fleet, RolloutStage.Full), t => Assert.Equal("4.1.0", t.FirmwareVersion));
    }

    [Fact]
    public void Full_rollout_completes_after_promotion()
    {
        var (rollout, fleet) = StartedRollout();
        foreach (var tool in ToolsIn(rollout, fleet, RolloutStage.Canary))
            rollout.RecordResult(tool, true, null, Now);

        rollout.Promote();
        foreach (var tool in ToolsIn(rollout, fleet, RolloutStage.Full))
            rollout.RecordResult(tool, true, null, Now);

        Assert.Equal(RolloutStatus.Completed, rollout.Status);
        Assert.NotNull(rollout.FinishedAtUtc);
        Assert.All(fleet, t => Assert.Equal("4.2.0", t.FirmwareVersion));
    }

    [Fact]
    public void Canary_failure_above_threshold_halts_immediately()
    {
        // 20 tools at 10% gives a canary of 2; one failure is 50%, above 20%.
        var (rollout, fleet) = StartedRollout();
        var canary = ToolsIn(rollout, fleet, RolloutStage.Canary);

        rollout.RecordResult(canary[0], succeeded: false, "Checksum mismatch", Now);

        Assert.Equal(RolloutStatus.Halted, rollout.Status);
        Assert.Contains("1 of 2", rollout.HaltReason);
        Assert.Equal("4.1.0", canary[0].FirmwareVersion);
        Assert.Throws<DomainException>(() => rollout.RecordResult(canary[1], true, null, Now));
    }

    [Fact]
    public void Failures_within_the_threshold_do_not_halt()
    {
        // 10 canary tools, 20% threshold: two failures is exactly 20%, which is allowed.
        var (rollout, fleet) = StartedRollout(fleetSize: 20, canaryPercent: 50, threshold: 20);
        var canary = ToolsIn(rollout, fleet, RolloutStage.Canary);

        rollout.RecordResult(canary[0], false, "Timeout", Now);
        rollout.RecordResult(canary[1], false, "Timeout", Now);
        foreach (var tool in canary.Skip(2))
            rollout.RecordResult(tool, true, null, Now);

        Assert.Equal(RolloutStatus.AwaitingPromotion, rollout.Status);
        Assert.Equal(new StageProgress(10, 8, 2, 0), rollout.Progress(RolloutStage.Canary));
    }

    [Fact]
    public void Zero_threshold_halts_on_any_failure()
    {
        var (rollout, fleet) = StartedRollout(threshold: 0);

        rollout.RecordResult(ToolsIn(rollout, fleet, RolloutStage.Canary)[0], false, null, Now);

        Assert.Equal(RolloutStatus.Halted, rollout.Status);
    }

    [Fact]
    public void Small_fleet_with_no_full_stage_completes_after_canary()
    {
        var (rollout, fleet) = StartedRollout(fleetSize: 1);

        rollout.RecordResult(fleet[0], true, null, Now);

        Assert.Equal(RolloutStatus.Completed, rollout.Status);
    }

    [Fact]
    public void Reporting_twice_for_the_same_tool_is_rejected()
    {
        var (rollout, fleet) = StartedRollout(fleetSize: 20, canaryPercent: 50);
        var tool = ToolsIn(rollout, fleet, RolloutStage.Canary)[0];
        rollout.RecordResult(tool, true, null, Now);

        Assert.Throws<DomainException>(() => rollout.RecordResult(tool, true, null, Now));
    }

    [Fact]
    public void Full_stage_tools_cannot_report_during_canary()
    {
        var (rollout, fleet) = StartedRollout();

        var fullStageTool = ToolsIn(rollout, fleet, RolloutStage.Full)[0];

        Assert.Throws<DomainException>(() => rollout.RecordResult(fullStageTool, true, null, Now));
    }

    [Fact]
    public void Tools_outside_the_rollout_are_rejected()
    {
        var (rollout, _) = StartedRollout();

        Assert.Throws<DomainException>(() => rollout.RecordResult(Tool(serial: "XX-9999"), true, null, Now));
    }

    [Fact]
    public void Results_are_rejected_before_start()
    {
        var fleet = Fleet(5);
        var rollout = Rollout.Create(Release(), fleet, 20, 20, Now);

        Assert.Throws<DomainException>(() => rollout.RecordResult(fleet[0], true, null, Now));
    }

    [Fact]
    public void Promote_requires_a_passed_canary()
    {
        var (rollout, _) = StartedRollout();

        Assert.Throws<DomainException>(() => rollout.Promote());
    }

    [Fact]
    public void Start_only_works_once()
    {
        var (rollout, _) = StartedRollout();

        Assert.Throws<DomainException>(() => rollout.Start(Now));
    }

    [Fact]
    public void Abort_stops_a_running_rollout_but_not_a_finished_one()
    {
        var (rollout, _) = StartedRollout();

        rollout.Abort(Now);

        Assert.Equal(RolloutStatus.Aborted, rollout.Status);
        Assert.Throws<DomainException>(() => rollout.Abort(Now));
    }
}
