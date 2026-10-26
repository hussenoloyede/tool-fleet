using System.Globalization;
using System.Security.Cryptography;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using ToolFleet.Api.Data;
using ToolFleet.Api.Storage;
using ToolFleet.Domain;

namespace ToolFleet.Api.Endpoints;

public static class FirmwareEndpoints
{
    public static void MapFirmwareEndpoints(this IEndpointRouteBuilder api)
    {
        var group = api.MapGroup("/firmware").WithTags("Firmware");
        var maxFileBytes = api.ServiceProvider.GetRequiredService<IOptions<FirmwareStorageOptions>>().Value.MaxFileBytes;

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

        // Publish a build: a multipart form with model, version, notes and the file itself.
        group.MapPost("/", async (
            [FromForm] string model,
            [FromForm] string version,
            [FromForm] string? notes,
            IFormFile? file,
            FleetDbContext db,
            IFirmwareStore store,
            TimeProvider clock,
            CancellationToken ct) =>
        {
            if (file is null) throw new DomainException("Attach the firmware file.");
            if (file.Length == 0) throw new DomainException("The firmware file is empty.");
            if (file.Length > maxFileBytes)
                throw new DomainException($"The file is {Size(file.Length)}. The limit is {Size(maxFileBytes)}.");

            model = model.Trim();
            version = version.Trim();
            if (await db.FirmwareReleases.AnyAsync(r => r.Model == model && r.Version == version, ct))
                throw new DomainException($"{model} firmware {version} already exists.");

            // ASP.NET Core has already buffered the upload, so it can be read twice: once to hash, once to store.
            string sha256;
            await using (var content = file.OpenReadStream())
                sha256 = Convert.ToHexStringLower(await SHA256.HashDataAsync(content, ct));

            var fileName = SafeFileName(file.FileName);
            var storageName = $"{Guid.NewGuid():N}/{fileName}";

            // Store the file before saving the row, so the database never points at a file that isn't there.
            await using (var content = file.OpenReadStream())
                await store.UploadAsync(storageName, content, ct);

            var release = new FirmwareRelease(model, version, notes ?? "", clock.GetUtcNow().UtcDateTime,
                new FirmwareFile(fileName, file.Length, sha256, storageName));
            db.FirmwareReleases.Add(release);
            await db.SaveChangesAsync(ct);

            return TypedResults.Created($"/api/firmware/{release.Id}", FirmwareReleaseDto.From(release));
        })
        // Antiforgery tokens protect cookie sign-ins, and this API has none yet. Revisit when sign-in is added.
        .DisableAntiforgery()
        // Kestrel stops request bodies at about 28 MB by default. Allow the file limit plus room for the form fields.
        .WithMetadata(new RequestSizeLimitAttribute(maxFileBytes + 1024 * 1024));

        // A short-lived link to the file. Field tools (and the website's Download button) call this.
        group.MapGet("/{id:guid}/download", async Task<Results<Ok<FirmwareDownloadDto>, NotFound>> (
            Guid id,
            FleetDbContext db,
            IFirmwareStore store,
            IOptions<FirmwareStorageOptions> options,
            TimeProvider clock,
            CancellationToken ct) =>
        {
            var release = await db.FirmwareReleases.AsNoTracking().SingleOrDefaultAsync(r => r.Id == id, ct);
            if (release is null) return TypedResults.NotFound();
            if (!release.HasFile)
                throw new DomainException($"{release.Model} {release.Version} has no file on record.");

            var expiresAt = clock.GetUtcNow().AddMinutes(options.Value.DownloadLinkMinutes);
            var url = await store.GetDownloadLinkAsync(release.FileStorageName!, release.FileName!, expiresAt, ct);

            return TypedResults.Ok(new FirmwareDownloadDto(
                // AbsoluteUri keeps the link percent-encoded; Uri.ToString() would decode parts of the signed query.
                url.AbsoluteUri, expiresAt.UtcDateTime, release.FileName!, release.FileSizeBytes!.Value, release.FileSha256!));
        });
    }

    /// <summary>Keeps letters, digits, dots, dashes and underscores, so the name is safe in storage paths and headers.</summary>
    internal static string SafeFileName(string uploadedName)
    {
        var name = Path.GetFileName(uploadedName.Replace('\\', '/'));
        var safe = new string(name.Select(c => char.IsAsciiLetterOrDigit(c) || c is '.' or '-' or '_' ? c : '_').ToArray())
            .Trim('.');
        if (safe.Length == 0) return "firmware.bin";
        return safe.Length <= 100 ? safe : safe[^100..];
    }

    private static string Size(long bytes) => bytes >= 1024 * 1024
        ? string.Create(CultureInfo.InvariantCulture, $"{bytes / (1024.0 * 1024.0):0.#} MB")
        : string.Create(CultureInfo.InvariantCulture, $"{bytes / 1024.0:0.#} KB");
}
