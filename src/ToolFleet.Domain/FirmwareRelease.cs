namespace ToolFleet.Domain;

/// <summary>A firmware build for one tool model. Version is unique per model (enforced by a DB index).</summary>
public class FirmwareRelease
{
    private FirmwareRelease() { } // for EF Core

    public FirmwareRelease(string model, string version, string notes, DateTime publishedAtUtc)
    {
        if (string.IsNullOrWhiteSpace(model)) throw new DomainException("Model is required.");
        if (string.IsNullOrWhiteSpace(version)) throw new DomainException("Version is required.");

        Id = Guid.NewGuid();
        Model = model.Trim();
        Version = version.Trim();
        Notes = notes?.Trim() ?? "";
        PublishedAtUtc = publishedAtUtc;
    }

    public Guid Id { get; private set; }
    public string Model { get; private set; } = "";
    public string Version { get; private set; } = "";
    public string Notes { get; private set; } = "";
    public DateTime PublishedAtUtc { get; private set; }
}
