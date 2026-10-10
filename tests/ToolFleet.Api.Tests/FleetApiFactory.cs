using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using ToolFleet.Api.Data;

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

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting("Database:Provider", "Sqlite");
        builder.UseSetting("ConnectionStrings:Fleet", _connectionString);
    }

    protected override IHost CreateHost(IHostBuilder builder)
    {
        var host = base.CreateHost(builder);

        using var scope = host.Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<FleetDbContext>().Database.EnsureCreated();

        return host;
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing) _keepAlive.Dispose();
    }
}
