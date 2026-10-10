using Microsoft.EntityFrameworkCore;
using ToolFleet.Api.Data;

namespace ToolFleet.Api.Endpoints;

public static class AuditEndpoints
{
    public static void MapAuditEndpoints(this IEndpointRouteBuilder api)
    {
        // GET /api/audit?entityType=Rollout&entityId=...&take=50
        api.MapGet("/audit", async (string? entityType, string? entityId, int? take, FleetDbContext db, CancellationToken ct) =>
        {
            var limit = Math.Clamp(take ?? 100, 1, 500);
            var query = db.AuditEntries.AsNoTracking();
            if (!string.IsNullOrWhiteSpace(entityType)) query = query.Where(a => a.EntityType == entityType);
            if (!string.IsNullOrWhiteSpace(entityId)) query = query.Where(a => a.EntityId == entityId);

            var entries = await query
                .OrderByDescending(a => a.Id)
                .Take(limit)
                .Select(a => new AuditEntryDto(a.Id, a.AtUtc, a.Actor, a.EntityType, a.EntityId, a.Action, a.Changes))
                .ToListAsync(ct);
            return TypedResults.Ok(entries);
        }).WithTags("Audit");
    }
}
