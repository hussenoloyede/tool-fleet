using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using ToolFleet.Domain.Rollouts;

namespace ToolFleet.Api.Tests;

/// <summary>
/// End-to-end tests through HTTP. Each test uses its own tool model name so tests sharing
/// the database (one per class) never see each other's tools or rollouts.
/// </summary>
public class RolloutApiTests(FleetApiFactory factory) : IClassFixture<FleetApiFactory>
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly HttpClient _client = factory.CreateClient();

    [Fact]
    public async Task Health_check_responds()
    {
        var response = await _client.GetAsync("/health");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Rollout_runs_from_draft_to_completed_and_updates_every_tool()
    {
        var model = UniqueModel();
        var tools = await RegisterFleetAsync(model, count: 5);
        var release = await PublishAsync(model, "4.2.0");

        var rollout = await CreateRolloutAsync(release.Id, canaryPercent: 20, threshold: 0);
        Assert.Equal(RolloutStatus.Draft, rollout.Status);
        Assert.Equal(1, rollout.Canary.Total);
        Assert.Equal(4, rollout.Full.Total);

        rollout = await PostAsync<RolloutDetailDto>($"/api/rollouts/{rollout.Id}/start");
        Assert.Equal(RolloutStatus.Canary, rollout.Status);

        rollout = await ReportAllAsync(rollout, RolloutStage.Canary);
        Assert.Equal(RolloutStatus.AwaitingPromotion, rollout.Status);

        rollout = await PostAsync<RolloutDetailDto>($"/api/rollouts/{rollout.Id}/promote");
        rollout = await ReportAllAsync(rollout, RolloutStage.Full);
        Assert.Equal(RolloutStatus.Completed, rollout.Status);

        foreach (var tool in tools)
        {
            var updated = await _client.GetFromJsonAsync<ToolDto>($"/api/tools/{tool.Id}", Json);
            Assert.Equal("4.2.0", updated!.FirmwareVersion);
        }

        var audit = await _client.GetFromJsonAsync<List<AuditEntryDto>>(
            $"/api/audit?entityType=Rollout&entityId={rollout.Id}", Json);
        Assert.Contains(audit!, a => a.Action == "Insert");
        Assert.Contains(audit!, a => a.Action == "Update" && a.Changes.Contains("Completed"));
    }

    [Fact]
    public async Task Failed_canary_halts_the_rollout()
    {
        var model = UniqueModel();
        await RegisterFleetAsync(model, count: 10);
        var release = await PublishAsync(model, "4.2.0");
        var rollout = await CreateRolloutAsync(release.Id, canaryPercent: 20, threshold: 0);
        await PostAsync<RolloutDetailDto>($"/api/rollouts/{rollout.Id}/start");

        var canaryTool = rollout.Targets.First(t => t.Stage == RolloutStage.Canary);
        rollout = await ReportAsync(rollout.Id, canaryTool.ToolId, succeeded: false, "Checksum mismatch");

        Assert.Equal(RolloutStatus.Halted, rollout.Status);
        Assert.NotNull(rollout.HaltReason);
        Assert.Equal("Checksum mismatch", rollout.Targets.Single(t => t.ToolId == canaryTool.ToolId).FailureReason);
    }

    [Fact]
    public async Task Invalid_transition_returns_422_with_problem_details()
    {
        var model = UniqueModel();
        await RegisterFleetAsync(model, count: 3);
        var release = await PublishAsync(model, "4.2.0");
        var rollout = await CreateRolloutAsync(release.Id, canaryPercent: 10, threshold: 20);

        var response = await _client.PostAsync($"/api/rollouts/{rollout.Id}/promote", null);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task Second_active_rollout_for_the_same_model_is_rejected()
    {
        var model = UniqueModel();
        await RegisterFleetAsync(model, count: 3);
        var first = await PublishAsync(model, "4.2.0");
        var second = await PublishAsync(model, "4.3.0");
        await CreateRolloutAsync(first.Id, canaryPercent: 10, threshold: 20);

        var response = await _client.PostAsJsonAsync("/api/rollouts", new CreateRolloutRequest(second.Id, 10, 20), Json);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    [Fact]
    public async Task Duplicate_serial_number_is_rejected()
    {
        var model = UniqueModel();
        var tools = await RegisterFleetAsync(model, count: 1);
        var location = await CreateLocationAsync();

        var response = await _client.PostAsJsonAsync("/api/tools",
            new CreateToolRequest(tools[0].SerialNumber, model, location.Id, "4.1.0", Today, 180), Json);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    [Fact]
    public async Task Unknown_api_route_returns_404_not_the_website()
    {
        var response = await _client.GetAsync("/api/does-not-exist");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Unknown_calibration_filter_returns_400()
    {
        var response = await _client.GetAsync("/api/tools?calibration=whenever");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    // ---- helpers ----

    private static readonly DateOnly Today = DateOnly.FromDateTime(DateTime.UtcNow);

    private static string UniqueModel() => $"Test Model {Guid.NewGuid():N}";

    private async Task<LocationDto> CreateLocationAsync() =>
        await PostAsync<LocationDto>("/api/locations", new CreateLocationRequest($"Base {Guid.NewGuid():N}", "Malaysia"));

    private async Task<List<ToolDto>> RegisterFleetAsync(string model, int count)
    {
        var location = await CreateLocationAsync();
        var prefix = Guid.NewGuid().ToString("N")[..6];
        var tools = new List<ToolDto>();
        for (var i = 1; i <= count; i++)
        {
            tools.Add(await PostAsync<ToolDto>("/api/tools",
                new CreateToolRequest($"{prefix}-{i:000}", model, location.Id, "4.1.0", Today, 180)));
        }
        return tools;
    }

    private Task<FirmwareReleaseDto> PublishAsync(string model, string version) =>
        PostAsync<FirmwareReleaseDto>("/api/firmware", new CreateFirmwareReleaseRequest(model, version, "Test build"));

    private Task<RolloutDetailDto> CreateRolloutAsync(Guid releaseId, int canaryPercent, int threshold) =>
        PostAsync<RolloutDetailDto>("/api/rollouts", new CreateRolloutRequest(releaseId, canaryPercent, threshold));

    private Task<RolloutDetailDto> ReportAsync(Guid rolloutId, Guid toolId, bool succeeded, string? reason = null) =>
        PostAsync<RolloutDetailDto>($"/api/rollouts/{rolloutId}/targets/{toolId}/result",
            new ReportResultRequest(succeeded, reason));

    private async Task<RolloutDetailDto> ReportAllAsync(RolloutDetailDto rollout, RolloutStage stage)
    {
        foreach (var target in rollout.Targets.Where(t => t.Stage == stage).ToList())
            rollout = await ReportAsync(rollout.Id, target.ToolId, succeeded: true);
        return rollout;
    }

    private async Task<T> PostAsync<T>(string url, object? body = null)
    {
        var response = body is null
            ? await _client.PostAsync(url, null)
            : await _client.PostAsJsonAsync(url, body, Json);

        if (!response.IsSuccessStatusCode)
        {
            var problem = await response.Content.ReadAsStringAsync();
            Assert.Fail($"POST {url} returned {(int)response.StatusCode}: {problem}");
        }

        return (await response.Content.ReadFromJsonAsync<T>(Json))!;
    }
}
