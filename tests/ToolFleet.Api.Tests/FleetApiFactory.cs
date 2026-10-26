using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using ToolFleet.Api.Data;
using ToolFleet.Api.Storage;
using ToolFleet.Domain;

namespace ToolFleet.Api.Tests;

/// <summary>
/// Runs the real API in memory against an in-memory SQLite database, so tests need
/// no Docker and no SQL Server. One database per factory (per test class).
/// </summary>
public sealed class FleetApiFactory : WebApplicationFactory<Program>
{
    private readonly string _connectionString = $"Data Source=fleet-tests-{Guid.NewGuid():N};Mode=Memory;Cache=Shared";

    // A shared-cache in-memory database lives only while a connection is open, so hold one open.
    private readonly SqliteConnection _keepAlive;

    public FleetApiFactory()
    {
        _keepAlive = new SqliteConnection(_connectionString);
        _keepAlive.Open();
    }

    /// <summary>Uploaded firmware files end up here instead of Blob Storage.</summary>
    public InMemoryFirmwareStore Store { get; } = new();

    /// <summary>Kept small so the "file too big" test doesn't need a 50 MB upload.</summary>
    public const long MaxFileBytes = 64 * 1024;

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting("Database:Provider", "Sqlite");
        builder.UseSetting("ConnectionStrings:Fleet", _connectionString);
        builder.UseSetting("FirmwareStorage:MaxFileBytes", MaxFileBytes.ToString());
        builder.ConfigureTestServices(services => services.AddSingleton<IFirmwareStore>(Store));
    }

    protected override IHost CreateHost(IHostBuilder builder)
    {
        var host = base.CreateHost(builder);

        using var scope = host.Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<FleetDbContext>().Database.EnsureCreated();

        return host;
    }

    /// <summary>A build with no file, like the seeded baseline versions. The API can't create one.</summary>
    public FirmwareRelease AddReleaseWithoutFile(string model, string version)
    {
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<FleetDbContext>();
        var release = new FirmwareRelease(model, version, "Baseline", DateTime.UtcNow);
        db.FirmwareReleases.Add(release);
        db.SaveChanges();
        return release;
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing) _keepAlive.Dispose();
    }
}
