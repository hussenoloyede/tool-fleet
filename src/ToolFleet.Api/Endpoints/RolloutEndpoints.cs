using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using ToolFleet.Api.Data;
using ToolFleet.Domain;
using ToolFleet.Domain.Rollouts;

namespace ToolFleet.Api.Endpoints;

public static class RolloutEndpoints
{
    public static void MapRolloutEndpoints(this IEndpointRouteBuilder api)
    {
        var group = api.MapGroup("/rollouts").WithTags("Rollouts");

        group.MapGet("/", async (FleetDbContext db, CancellationToken ct) =>
        {
            var rollouts = await db.Rollouts.AsNoTracking()
                .Include(r => r.Targets)
                .OrderByDescending(r => r.CreatedAtUtc)
                .ToListAsync(ct);
            return TypedResults.Ok(rollouts.Select(RolloutSummaryDto.From).ToList());
        });

        group.MapGet("/{id:guid}", async Task<Results<Ok<RolloutDetailDto>, NotFound>> (
            Guid id, FleetDbContext db, CancellationToken ct) =>
        {
            var rollout = await LoadAsync(db, id, ct);
            return rollout is null ? TypedResults.NotFound() : TypedResults.Ok(await ToDetailAsync(rollout, db, ct));
        });

        group.MapPost("/", async (CreateRolloutRequest request, FleetDbContext db, TimeProvider clock, CancellationToken ct) =>
        {
            var release = await db.FirmwareReleases.FindAsync([request.FirmwareReleaseId], ct)
                ?? throw new DomainException("Unknown firmware release.");

            var activeForModel = await db.Rollouts.AnyAsync(r =>
                r.Model == release.Model &&
                r.Status != RolloutStatus.Completed &&
                r.Status != RolloutStatus.Halted &&
                r.Status != RolloutStatus.Aborted, ct);
            if (activeForModel)
                throw new DomainException($"{release.Model} already has an active rollout. Finish or abort it first.");

            var candidates = await db.Tools
                .Where(t => t.Model == release.Model && t.FirmwareVersion != release.Version)
                .ToArrayAsync(ct);

            // The domain takes the first N tools as the canary, so shuffle here to make it a random sample.
            Random.Shared.Shuffle(candidates);

            var rollout = Rollout.Create(
                release, candidates, request.CanaryPercent, request.FailureThresholdPercent, clock.GetUtcNow().UtcDateTime);

            db.Rollouts.Add(rollout);
            await db.SaveChangesAsync(ct);

            return TypedResults.Created($"/api/rollouts/{rollout.Id}", await ToDetailAsync(rollout, db, ct));
        });

        group.MapPost("/{id:guid}/start", (Guid id, FleetDbContext db, TimeProvider clock, CancellationToken ct) =>
            ChangeAsync(db, id, r => r.Start(clock.GetUtcNow().UtcDateTime), ct));

        group.MapPost("/{id:guid}/promote", (Guid id, FleetDbContext db, CancellationToken ct) =>
            ChangeAsync(db, id, r => r.Promote(), ct));

        group.MapPost("/{id:guid}/abort", (Guid id, FleetDbContext db, TimeProvider clock, CancellationToken ct) =>
            ChangeAsync(db, id, r => r.Abort(clock.GetUtcNow().UtcDateTime), ct));

        // Called by the simulator (or, one day, a field device) after it tries to flash the firmware.
        group.MapPost("/{id:guid}/targets/{toolId:guid}/result", async Task<Results<Ok<RolloutDetailDto>, NotFound>> (
            Guid id, Guid toolId, ReportResultRequest request, FleetDbContext db, TimeProvider clock, CancellationToken ct) =>
        {
            var tool = await db.Tools.FindAsync([toolId], ct);
            if (tool is null) return TypedResults.NotFound();

            return await ChangeAsync(
                db, id,
                r => r.RecordResult(tool, request.Succeeded, request.FailureReason, clock.GetUtcNow().UtcDateTime),
                ct);
        });
    }

    private static Task<Rollout?> LoadAsync(FleetDbContext db, Guid id, CancellationToken ct) =>
        db.Rollouts.Include(r => r.Targets).SingleOrDefaultAsync(r => r.Id == id, ct);

    /// <summary>Load, apply one domain method, save. Rule violations surface as 422 via DomainExceptionHandler.</summary>
    private static async Task<Results<Ok<RolloutDetailDto>, NotFound>> ChangeAsync(
        FleetDbContext db, Guid id, Action<Rollout> change, CancellationToken ct)
    {
        var rollout = await LoadAsync(db, id, ct);
        if (rollout is null) return TypedResults.NotFound();

        change(rollout);
        await db.SaveChangesAsync(ct);

        return TypedResults.Ok(await ToDetailAsync(rollout, db, ct));
    }

    private static async Task<RolloutDetailDto> ToDetailAsync(Rollout r, FleetDbContext db, CancellationToken ct)
    {
        var toolIds = r.Targets.Select(t => t.ToolId).ToList();
        var tools = await db.Tools.AsNoTracking()
            .Where(t => toolIds.Contains(t.Id))
            .Select(t => new { t.Id, t.SerialNumber, LocationName = t.Location!.Name })
            .ToDictionaryAsync(t => t.Id, ct);

        var targets = r.Targets
            .OrderBy(t => t.Stage)
            .ThenBy(t => tools[t.ToolId].SerialNumber)
            .Select(t => new RolloutTargetDto(
                t.ToolId,
                tools[t.ToolId].SerialNumber,
                tools[t.ToolId].LocationName,
                t.Stage,
                t.Status,
                t.PreviousVersion,
                t.FailureReason,
                t.ReportedAtUtc))
            .ToList();

        return new RolloutDetailDto(
            r.Id, r.FirmwareReleaseId, r.Model, r.TargetVersion, r.CanaryPercent, r.FailureThresholdPercent,
            r.Status, r.HaltReason, r.CreatedAtUtc, r.StartedAtUtc, r.FinishedAtUtc,
            r.Progress(RolloutStage.Canary), r.Progress(RolloutStage.Full), targets);
    }
}
