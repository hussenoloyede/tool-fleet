using System.Collections.Concurrent;
using ToolFleet.Api.Storage;

namespace ToolFleet.Api.Tests;

/// <summary>Stands in for Blob Storage in the API tests.</summary>
public sealed class InMemoryFirmwareStore : IFirmwareStore
{
    public ConcurrentDictionary<string, byte[]> Files { get; } = new();

    public async Task UploadAsync(string storageName, Stream content, CancellationToken ct)
    {
        using var copy = new MemoryStream();
        await content.CopyToAsync(copy, ct);
        if (!Files.TryAdd(storageName, copy.ToArray()))
            throw new InvalidOperationException($"{storageName} already exists.");
    }

    public Task<Uri> GetDownloadLinkAsync(string storageName, string downloadFileName, DateTimeOffset expiresAt, CancellationToken ct) =>
        Task.FromResult(new Uri(
            $"https://files.test/{storageName}?se={expiresAt.ToUnixTimeSeconds()}&rscd={Uri.EscapeDataString($"attachment; filename=\"{downloadFileName}\"")}"));
}
