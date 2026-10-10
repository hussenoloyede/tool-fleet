using Microsoft.EntityFrameworkCore;
using ToolFleet.Domain;

namespace ToolFleet.Api.Data;

/// <summary>
/// Demo data: four Malaysian field bases and 40 tools. Fixed random seed, so every
/// developer (and every demo) gets the same fleet, including a few overdue calibrations.
/// </summary>
public static class DevSeeder
{
    private static readonly (string Model, string Prefix, string Firmware, int Count, int IntervalDays)[] Models =
    [
        ("MWD Pulser", "MP", "4.1.0", 24, 180),
        ("Gamma Ray Sub", "GR", "2.3.1", 12, 365),
        ("Directional Sensor", "DS", "1.8.4", 4, 90),
    ];

    public static async Task SeedAsync(FleetDbContext db, TimeProvider clock)
    {
        if (await db.Locations.AnyAsync()) return;

        var now = clock.GetUtcNow().UtcDateTime;
        var today = DateOnly.FromDateTime(now);
        var random = new Random(42);

        FieldLocation[] locations =
        [
            new("Labuan", "Malaysia"),
            new("Miri", "Malaysia"),
            new("Bintulu", "Malaysia"),
            new("Kemaman", "Malaysia"),
        ];
        db.Locations.AddRange(locations);

        foreach (var (model, prefix, firmware, count, interval) in Models)
        {
            db.FirmwareReleases.Add(new FirmwareRelease(model, firmware, "Baseline release", now.AddMonths(-6)));

            for (var i = 1; i <= count; i++)
            {
                var location = locations[random.Next(locations.Length)];
                // Spread last-calibration dates so that some tools land in DueSoon and Overdue.
                var calibratedDaysAgo = random.Next(0, interval + 45);
                db.Tools.Add(new Tool(
                    serialNumber: $"{prefix}-{i:0000}",
                    model: model,
                    locationId: location.Id,
                    firmwareVersion: firmware,
                    lastCalibratedOn: today.AddDays(-calibratedDaysAgo),
                    calibrationIntervalDays: interval));
            }
        }

        await db.SaveChangesAsync();
    }
}
