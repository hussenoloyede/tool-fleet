using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ToolFleet.Api.Tests;

public class FirmwareApiTests(FleetApiFactory factory) : IClassFixture<FleetApiFactory>
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly FleetApiFactory _factory = factory;
    private readonly HttpClient _client = factory.CreateClient();

    [Fact]
    public async Task Publishing_stores_the_file_with_its_size_and_sha256()
    {
        var bytes = FirmwareUpload.FakeBuild(5000);

        var response = await FirmwareUpload.PublishAsync(_client, UniqueModel(), "4.2.0", bytes, "mp-4.2.0.bin");

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var release = (await response.Content.ReadFromJsonAsync<FirmwareReleaseDto>(Json))!;
        Assert.NotNull(release.File);
        Assert.Equal("mp-4.2.0.bin", release.File.FileName);
        Assert.Equal(5000, release.File.SizeBytes);
        Assert.Equal(Convert.ToHexStringLower(SHA256.HashData(bytes)), release.File.Sha256);

        var stored = Assert.Single(_factory.Store.Files, f => f.Key.EndsWith("/mp-4.2.0.bin") && f.Value.Length == 5000);
        Assert.Equal(bytes, stored.Value);
    }

    [Fact]
    public async Task Download_link_expires_after_15_minutes()
    {
        var bytes = FirmwareUpload.FakeBuild();
        var published = await FirmwareUpload.PublishAsync(_client, UniqueModel(), "4.2.0", bytes);
        var release = (await published.Content.ReadFromJsonAsync<FirmwareReleaseDto>(Json))!;

        var link = await _client.GetFromJsonAsync<FirmwareDownloadDto>($"/api/firmware/{release.Id}/download", Json);

        Assert.NotNull(link);
        Assert.StartsWith("https://files.test/", link.Url);
        Assert.DoesNotContain('"', link.Url); // the signed query must stay percent-encoded
        Assert.InRange(link.ExpiresAtUtc, DateTime.UtcNow.AddMinutes(14), DateTime.UtcNow.AddMinutes(16));
        Assert.Equal(release.File!.Sha256, link.Sha256);
        Assert.Equal(bytes.Length, link.SizeBytes);
    }

    [Fact]
    public async Task Publishing_without_a_file_is_rejected()
    {
        var response = await FirmwareUpload.PublishAsync(_client, UniqueModel(), "4.2.0", content: null);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    [Fact]
    public async Task File_over_the_size_limit_is_rejected_and_not_stored()
    {
        var model = UniqueModel();
        var tooBig = FirmwareUpload.FakeBuild((int)FleetApiFactory.MaxFileBytes + 1);

        var response = await FirmwareUpload.PublishAsync(_client, model, "4.2.0", tooBig, "too-big.bin");

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        Assert.Contains("The limit is 64 KB", await response.Content.ReadAsStringAsync());
        Assert.DoesNotContain(_factory.Store.Files.Keys, k => k.EndsWith("/too-big.bin"));
    }

    [Fact]
    public async Task Duplicate_version_is_rejected_before_the_file_is_stored()
    {
        var model = UniqueModel();
        await FirmwareUpload.PublishAsync(_client, model, "4.2.0", FirmwareUpload.FakeBuild());

        var response = await FirmwareUpload.PublishAsync(_client, model, "4.2.0", FirmwareUpload.FakeBuild(), "second-copy.bin");

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        Assert.DoesNotContain(_factory.Store.Files.Keys, k => k.EndsWith("/second-copy.bin"));
    }

    [Theory]
    [InlineData("ds 4.3.1 (final).bin", "ds_4.3.1__final_.bin")]
    [InlineData("../../etc/passwd", "passwd")]
    [InlineData("...", "firmware.bin")]
    public async Task Uploaded_file_names_are_made_safe(string uploaded, string expected)
    {
        var response = await FirmwareUpload.PublishAsync(_client, UniqueModel(), "4.2.0", FirmwareUpload.FakeBuild(), uploaded);

        var release = (await response.Content.ReadFromJsonAsync<FirmwareReleaseDto>(Json))!;
        Assert.Equal(expected, release.File!.FileName);
    }

    [Fact]
    public async Task Download_link_for_a_build_without_a_file_is_refused()
    {
        var baseline = _factory.AddReleaseWithoutFile(UniqueModel(), "1.0.0");

        var response = await _client.GetAsync($"/api/firmware/{baseline.Id}/download");

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    [Fact]
    public async Task Download_link_for_an_unknown_build_is_404()
    {
        var response = await _client.GetAsync($"/api/firmware/{Guid.NewGuid()}/download");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    private static string UniqueModel() => $"Test Model {Guid.NewGuid():N}";
}
