using Microsoft.EntityFrameworkCore;
using ToolFleet.Api.Data;
using ToolFleet.Domain;

namespace ToolFleet.Api.Endpoints;

public static class FirmwareEndpoints
{
    public static void MapFirmwareEndpoints(this IEndpointRouteBuilder api)
    {
        var group = api.MapGroup("/firmware").WithTags("Firmware");

        group.MapGet("/", async (string? model, FleetDbContext db, CancellationToken ct) =>
        {
            var query = db.FirmwareReleases.AsNoTracking();
            if (!string.IsNullOrWhiteSpace(model)) query = query.Where(r => r.Model == model);

            var releases = await query
                .OrderBy(r => r.Model)
                .ThenByDescending(r => r.PublishedAtUtc)
                .ToListAsync(ct);
            return TypedResults.Ok(releases.Select(FirmwareReleaseDto.From).ToList());
        });

        group.MapPost("/", async (CreateFirmwareReleaseRequest request, FleetDbContext db, TimeProvider clock, CancellationToken ct) =>
        {
            var release = new FirmwareRelease(request.Model, request.Version, request.Notes ?? "", clock.GetUtcNow().UtcDateTime);

            if (await db.FirmwareReleases.AnyAsync(r => r.Model == release.Model && r.Version == release.Version, ct))
                throw new DomainException($"{release.Model} firmware {release.Version} already exists.");

            db.FirmwareReleases.Add(release);
            await db.SaveChangesAsync(ct);

            return TypedResults.Created($"/api/firmware/{release.Id}", FirmwareReleaseDto.From(release));
        });
    }
}
