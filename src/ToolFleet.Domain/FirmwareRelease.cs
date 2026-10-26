using System.Text.RegularExpressions;

namespace ToolFleet.Domain;

/// <summary>A firmware build for one tool model. Version is unique per model (enforced by a DB index).</summary>
public partial class FirmwareRelease
{
    private FirmwareRelease() { } // for EF Core

    /// <param name="file">
    /// The uploaded build. Null only for the baseline versions the demo fleet shipped with,
    /// which were never uploaded; every build published through the API has one.
    /// </param>
    public FirmwareRelease(string model, string version, string notes, DateTime publishedAtUtc, FirmwareFile? file = null)
    {
        if (string.IsNullOrWhiteSpace(model)) throw new DomainException("Model is required.");
        if (string.IsNullOrWhiteSpace(version)) throw new DomainException("Version is required.");

        Id = Guid.NewGuid();
        Model = model.Trim();
        Version = version.Trim();
        Notes = notes?.Trim() ?? "";
        PublishedAtUtc = publishedAtUtc;

        if (file is not null)
        {
            if (string.IsNullOrWhiteSpace(file.FileName)) throw new DomainException("File name is required.");
            if (file.SizeBytes <= 0) throw new DomainException("The firmware file is empty.");
            if (!Sha256Hex().IsMatch(file.Sha256)) throw new DomainException("SHA-256 must be 64 lowercase hex characters.");
            if (string.IsNullOrWhiteSpace(file.StorageName)) throw new DomainException("Storage name is required.");

            FileName = file.FileName;
            FileSizeBytes = file.SizeBytes;
            FileSha256 = file.Sha256;
            FileStorageName = file.StorageName;
        }
    }

    public Guid Id { get; private set; }
    public string Model { get; private set; } = "";
    public string Version { get; private set; } = "";
    public string Notes { get; private set; } = "";
    public DateTime PublishedAtUtc { get; private set; }

    /// <summary>Original name of the uploaded file, e.g. "ds-4.3.1.bin".</summary>
    public string? FileName { get; private set; }
    public long? FileSizeBytes { get; private set; }

    /// <summary>Fingerprint of the file. A tool compares it with what it downloaded before flashing.</summary>
    public string? FileSha256 { get; private set; }

    /// <summary>Where the file sits in storage (the blob name). Never shown to users.</summary>
    public string? FileStorageName { get; private set; }

    public bool HasFile => FileStorageName is not null;

    [GeneratedRegex("^[0-9a-f]{64}$")]
    private static partial Regex Sha256Hex();
}

/// <summary>Facts about an uploaded firmware file.</summary>
public sealed record FirmwareFile(string FileName, long SizeBytes, string Sha256, string StorageName);
