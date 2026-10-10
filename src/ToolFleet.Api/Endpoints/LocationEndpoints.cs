using Microsoft.EntityFrameworkCore;
using ToolFleet.Api.Data;
using ToolFleet.Domain;

namespace ToolFleet.Api.Endpoints;

public static class LocationEndpoints
{
    public static void MapLocationEndpoints(this IEndpointRouteBuilder api)
    {
        var group = api.MapGroup("/locations").WithTags("Locations");

        group.MapGet("/", async (FleetDbContext db, CancellationToken ct) =>
        {
            var locations = await db.Locations.AsNoTracking()
                .OrderBy(l => l.Name)
                .Select(l => new LocationDto(l.Id, l.Name, l.Country))
                .ToListAsync(ct);
            return TypedResults.Ok(locations);
        });

        group.MapPost("/", async (CreateLocationRequest request, FleetDbContext db, CancellationToken ct) =>
        {
            var location = new FieldLocation(request.Name, request.Country);
            if (await db.Locations.AnyAsync(l => l.Name == location.Name, ct))
                throw new DomainException($"A location called {location.Name} already exists.");

            db.Locations.Add(location);
            await db.SaveChangesAsync(ct);

            return TypedResults.Created(
                $"/api/locations/{location.Id}",
                new LocationDto(location.Id, location.Name, location.Country));
        });
    }
}
