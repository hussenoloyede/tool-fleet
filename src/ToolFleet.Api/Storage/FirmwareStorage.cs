using Azure;
using Azure.Identity;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Azure.Storage.Sas;
using Microsoft.Extensions.Options;

namespace ToolFleet.Api.Storage;

/// <summary>Settings from the "FirmwareStorage" section. Set exactly one of ConnectionString or ServiceUri.</summary>
public sealed class FirmwareStorageOptions
{
    public const string Section = "FirmwareStorage";

    /// <summary>Local development: "UseDevelopmentStorage=true" points at Azurite on this PC.</summary>
    public string? ConnectionString { get; set; }

    /// <summary>Azure: https://&lt;account&gt;.blob.core.windows.net, reached with the web app's managed identity.</summary>
    public string? ServiceUri { get; set; }

    public string Container { get; set; } = "firmware";
    public long MaxFileBytes { get; set; } = 50 * 1024 * 1024;
    public int DownloadLinkMinutes { get; set; } = 15;
}

/// <summary>Where firmware files live. Blob Storage in real life, a dictionary in the API tests.</summary>
public interface IFirmwareStore
{
    /// <summary>Saves a new file. Fails if something is already stored under that name.</summary>
    Task UploadAsync(string storageName, Stream content, CancellationToken ct);

    /// <summary>A read-only link to the file that stops working at <paramref name="expiresAt"/>.</summary>
    Task<Uri> GetDownloadLinkAsync(string storageName, string downloadFileName, DateTimeOffset expiresAt, CancellationToken ct);
}

public sealed class BlobFirmwareStore : IFirmwareStore
{
    private readonly BlobServiceClient _service;
    private readonly BlobContainerClient _container;
    private readonly TimeProvider _clock;
    private bool _containerExists;

    public BlobFirmwareStore(IOptions<FirmwareStorageOptions> options, TimeProvider clock)
    {
        var o = options.Value;
        if (!string.IsNullOrWhiteSpace(o.ConnectionString))
            _service = new BlobServiceClient(o.ConnectionString);
        else if (!string.IsNullOrWhiteSpace(o.ServiceUri))
            // No storage key anywhere: in Azure this signs in as the web app's managed identity.
            _service = new BlobServiceClient(new Uri(o.ServiceUri), new DefaultAzureCredential());
        else
            throw new InvalidOperationException(
                "Firmware storage isn't configured. Set FirmwareStorage:ConnectionString (Azurite) or FirmwareStorage:ServiceUri (Azure).");

        _container = _service.GetBlobContainerClient(o.Container);
        _clock = clock;
    }

    public async Task UploadAsync(string storageName, Stream content, CancellationToken ct)
    {
        if (!_containerExists)
        {
            await _container.CreateIfNotExistsAsync(PublicAccessType.None, cancellationToken: ct);
            _containerExists = true;
        }

        await _container.GetBlobClient(storageName).UploadAsync(content, new BlobUploadOptions
        {
            HttpHeaders = new BlobHttpHeaders { ContentType = "application/octet-stream" },
            // A published build never changes, so refuse to overwrite one.
            Conditions = new BlobRequestConditions { IfNoneMatch = ETag.All },
        }, ct);
    }

    public async Task<Uri> GetDownloadLinkAsync(
        string storageName, string downloadFileName, DateTimeOffset expiresAt, CancellationToken ct)
    {
        var blob = _container.GetBlobClient(storageName);
        var startsOn = _clock.GetUtcNow().AddMinutes(-5); // a little slack for clocks that run behind

        var sas = new BlobSasBuilder(BlobSasPermissions.Read, expiresAt)
        {
            BlobContainerName = _container.Name,
            BlobName = storageName,
            Resource = "b",
            StartsOn = startsOn,
            Protocol = blob.Uri.Scheme == Uri.UriSchemeHttps ? SasProtocol.Https : SasProtocol.HttpsAndHttp,
            // File names are already limited to letters, digits, dots, dashes and underscores, so no quotes are needed.
            ContentDisposition = $"attachment; filename={downloadFileName}",
        };

        // Azurite: the client holds the account key and signs the link itself.
        if (blob.CanGenerateSasUri)
            return blob.GenerateSasUri(sas);

        // Azure: the app has no account key. It asks Azure for a short-lived user delegation key
        // (allowed by its Storage Blob Data Contributor role) and signs the link with that.
        UserDelegationKey key = await _service.GetUserDelegationKeyAsync(startsOn, expiresAt, ct);
        return new BlobUriBuilder(blob.Uri) { Sas = sas.ToSasQueryParameters(key, _service.AccountName) }.ToUri();
    }
}
