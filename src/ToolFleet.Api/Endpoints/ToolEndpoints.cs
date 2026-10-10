using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using ToolFleet.Api.Data;
using ToolFleet.Domain;

namespace ToolFleet.Api.Endpoints;

public static class ToolEndpoints
{
    public static void MapToolEndpoints(this IEndpointRouteBuilder api)
    {
        var group = api.MapGroup("/tools").WithTags("Tools");

        // GET /api/tools?model=MWD%20Pulser&locationId=...&calibration=Overdue
        group.MapGet("/", async Task<Results<Ok<List<ToolDto>>, ValidationProblem>> (
            string? model,
            Guid? locationId,
            string? calibration,
            FleetDbContext db,
            TimeProvider clock,
            CancellationToken ct) =>
        {
            CalibrationStatus? calibrationFilter = null;
            if (!string.IsNullOrEmpty(calibration))
            {
                if (!Enum.TryParse<CalibrationStatus>(calibration, ignoreCase: true, out var parsed))
                {
                    return TypedResults.ValidationProblem(new Dictionary<string, string[]>
                    {
                        ["calibration"] = [$"Use one of: {string.Join(", ", Enum.GetNames<CalibrationStatus>())}."],
                    });
                }
                calibrationFilter = parsed;
            }

            var query = db.Tools.AsNoTracking().Include(t => t.Location).AsQueryable();
            if (!string.IsNullOrWhiteSpace(model)) query = query.Where(t => t.Model == model);
            if (locationId is { } id) query = query.Where(t => t.LocationId == id);

            var today = Today(clock);
            var tools = await query.OrderBy(t => t.SerialNumber).ToListAsync(ct);

            // Calibration status is a domain rule, so it's computed in memory rather than
            // re-implemented in SQL. Fine at fleet sizes in the hundreds.
            var result = tools
                .Select(t => ToolDto.From(t, t.Location!.Name, today))
                .Where(t => calibrationFilter is null || t.CalibrationStatus == calibrationFilter)
                .ToList();

            return TypedResults.Ok(result);
        });

        group.MapGet("/{id:guid}", async Task<Results<Ok<ToolDto>, NotFound>> (
            Guid id, FleetDbContext db, TimeProvider clock, CancellationToken ct) =>
        {
            var tool = await db.Tools.AsNoTracking().Include(t => t.Location).SingleOrDefaultAsync(t => t.Id == id, ct);
            return tool is null
                ? TypedResults.NotFound()
                : TypedResults.Ok(ToolDto.From(tool, tool.Location!.Name, Today(clock)));
        });

        group.MapPost("/", async (CreateToolRequest request, FleetDbContext db, TimeProvider clock, CancellationToken ct) =>
        {
            var location = await db.Locations.FindAsync([request.LocationId], ct)
                ?? throw new DomainException("Unknown location.");

            var tool = new Tool(
                request.SerialNumber,
                request.Model,
                location.Id,
                request.FirmwareVersion,
                request.LastCalibratedOn,
                request.CalibrationIntervalDays);

            if (await db.Tools.AnyAsync(t => t.SerialNumber == tool.SerialNumber, ct))
                throw new DomainException($"Serial number {tool.SerialNumber} is already registered.");

            db.Tools.Add(tool);
            await db.SaveChangesAsync(ct);

            return TypedResults.Created($"/api/tools/{tool.Id}", ToolDto.From(tool, location.Name, Today(clock)));
        });

        group.MapPost("/{id:guid}/calibrations", async Task<Results<Ok<ToolDto>, NotFound>> (
            Guid id, RecordCalibrationRequest request, FleetDbContext db, TimeProvider clock, CancellationToken ct) =>
        {
            var tool = await db.Tools.Include(t => t.Location).SingleOrDefaultAsync(t => t.Id == id, ct);
            if (tool is null) return TypedResults.NotFound();

            var today = Today(clock);
            tool.RecordCalibration(request.CalibratedOn, today);
            await db.SaveChangesAsync(ct);

            return TypedResults.Ok(ToolDto.From(tool, tool.Location!.Name, today));
        });
    }

    private static DateOnly Today(TimeProvider clock) => DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime);
}
