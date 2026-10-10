using Microsoft.EntityFrameworkCore;

namespace ToolFleet.Api.Data;

public static class DatabaseInitialiser
{
    /// <summary>Set "Database:Provider" to "Sqlite" to run on a local file instead of SQL Server.</summary>
    public static bool UsesSqlite(IConfiguration configuration) =>
        string.Equals(configuration["Database:Provider"], "Sqlite", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Development only. SQL Server: applies EF migrations (falls back to EnsureCreated until the first
    /// migration exists). SQLite: EnsureCreated, because the migrations are written for SQL Server.
    /// </summary>
    public static async Task InitialiseDatabaseAsync(this WebApplication app, bool seed)
    {
        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<FleetDbContext>();

        if (!UsesSqlite(app.Configuration) && db.Database.GetMigrations().Any())
            await db.Database.MigrateAsync();
        else
            await db.Database.EnsureCreatedAsync();

        if (seed)
            await DevSeeder.SeedAsync(db, scope.ServiceProvider.GetRequiredService<TimeProvider>());
    }
}
