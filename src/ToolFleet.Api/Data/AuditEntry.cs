namespace ToolFleet.Api.Data;

/// <summary>One row per inserted, updated or deleted entity. Written by <see cref="FleetDbContext"/>.</summary>
public class AuditEntry
{
    public long Id { get; set; }
    public DateTime AtUtc { get; set; }
    public string Actor { get; set; } = "";
    public string EntityType { get; set; } = "";
    public string EntityId { get; set; } = "";
    public string Action { get; set; } = "";

    /// <summary>JSON. For inserts: every value. For updates: { "Property": { "from": x, "to": y } }.</summary>
    public string Changes { get; set; } = "{}";
}
