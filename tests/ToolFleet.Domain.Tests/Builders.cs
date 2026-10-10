using ToolFleet.Domain;

namespace ToolFleet.Domain.Tests;

/// <summary>Small helpers so each test only spells out the details it cares about.</summary>
internal static class Builders
{
    public static readonly DateTime Now = new(2026, 10, 11, 8, 0, 0, DateTimeKind.Utc);
    public static readonly DateOnly Today = DateOnly.FromDateTime(Now);
    public static readonly Guid Labuan = Guid.NewGuid();

    public static Tool Tool(
        string serial = "MP-0001",
        string model = "MWD Pulser",
        string firmware = "4.1.0",
        DateOnly? lastCalibrated = null,
        int intervalDays = 180) =>
        new(serial, model, Labuan, firmware, lastCalibrated ?? Today.AddDays(-10), intervalDays);

    public static List<Tool> Fleet(int count, string model = "MWD Pulser", string firmware = "4.1.0") =>
        Enumerable.Range(1, count)
            .Select(i => Tool(serial: $"MP-{i:0000}", model: model, firmware: firmware))
            .ToList();

    public static FirmwareRelease Release(string model = "MWD Pulser", string version = "4.2.0") =>
        new(model, version, "Fixes pulse timing drift", Now);
}
