using Microsoft.EntityFrameworkCore;

namespace ToolFleet.Api.Data;

public static class DatabaseInitialiser
{
    /// <summary>Set "Database:Provider" to "Sqlite" to run on a local file instead of SQL Server.</summary>
    public static bool UsesSqlite(IConfiguration configuration) =>
        string.Equals(configuration["Database:Provider"], "Sqlite", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Runs in Development, and in Azure when Database:MigrateOnStartup is true.
    /// SQL Server: applies EF migrations from Data/Migrations. SQLite: EnsureCreated, because the
    /// migrations are written for SQL Server. Seeding only adds the demo fleet to an empty database.
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
