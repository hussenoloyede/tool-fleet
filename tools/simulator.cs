// Plays the part of field tools during a rollout: reports a success or failure for every
// pending tool in the running stage, with a configurable failure rate.
//
// A .NET 10 file-based app, so no project file is needed:
//   dotnet run tools/simulator.cs -- <rolloutId> [--fail-rate 0.1] [--delay 400] [--api http://localhost:5080]
//
// A draft rollout is started automatically. When the canary passes, promote it (UI or POST /promote)
// and run the simulator again for the full stage.

using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

if (args.Length == 0 || !Guid.TryParse(args[0], out var rolloutId))
{
    Console.WriteLine("Usage: dotnet run tools/simulator.cs -- <rolloutId> [--fail-rate 0.1] [--delay 400] [--api http://localhost:5080]");
    return 1;
}

var failRate = double.Parse(Option("--fail-rate") ?? "0.1", System.Globalization.CultureInfo.InvariantCulture);
var delay = TimeSpan.FromMilliseconds(int.Parse(Option("--delay") ?? "400"));
var api = new HttpClient { BaseAddress = new Uri(Option("--api") ?? "http://localhost:5080") };
var json = new JsonSerializerOptions(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };
string[] failureReasons = ["Checksum mismatch", "Flash write timeout", "Battery below 20%", "Bootloader rejected image"];

var rollout = await api.GetFromJsonAsync<Rollout>($"/api/rollouts/{rolloutId}", json)
    ?? throw new InvalidOperationException("Rollout not found.");

if (rollout.Status == "Draft")
{
    rollout = await Post($"/api/rollouts/{rolloutId}/start", null);
    Console.WriteLine("Started rollout.");
}

var stage = rollout.Status switch
{
    "Canary" => "Canary",
    "Full" => "Full",
    _ => null,
};
if (stage is null)
{
    Console.WriteLine($"Nothing to do: rollout is {rollout.Status}.");
    return 0;
}

Console.WriteLine($"{rollout.Model} -> {rollout.TargetVersion}, {stage} stage, fail rate {failRate:P0}");

foreach (var target in rollout.Targets.Where(t => t.Stage == stage && t.Status == "Pending").ToList())
{
    await Task.Delay(delay);
    var succeeded = Random.Shared.NextDouble() >= failRate;
    var reason = succeeded ? null : failureReasons[Random.Shared.Next(failureReasons.Length)];

    rollout = await Post($"/api/rollouts/{rolloutId}/targets/{target.ToolId}/result", new { succeeded, failureReason = reason });
    Console.WriteLine($"  {target.SerialNumber,-10} {target.LocationName,-10} {(succeeded ? "ok" : "FAILED: " + reason)}");

    if (rollout.Status is "Halted")
    {
        Console.WriteLine($"Rollout halted: {rollout.HaltReason}");
        return 0;
    }
}

Console.WriteLine(rollout.Status switch
{
    "AwaitingPromotion" => "Canary passed. Promote the rollout, then run the simulator again.",
    "Completed" => "Rollout completed.",
    var s => $"Rollout is {s}.",
});
return 0;

async Task<Rollout> Post(string url, object? body)
{
    var response = await api.PostAsJsonAsync(url, body, json);
    if (!response.IsSuccessStatusCode)
        throw new InvalidOperationException($"POST {url} failed ({(int)response.StatusCode}): {await response.Content.ReadAsStringAsync()}");
    return (await response.Content.ReadFromJsonAsync<Rollout>(json))!;
}

string? Option(string name)
{
    var i = Array.IndexOf(args, name);
    return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
}

// Only the fields the simulator needs. Statuses stay strings to keep this file standalone.
record Rollout(string Model, string TargetVersion, string Status, string? HaltReason, List<Target> Targets);
record Target(Guid ToolId, string SerialNumber, string LocationName, string Stage, string Status);
