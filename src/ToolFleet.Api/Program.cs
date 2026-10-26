using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using ToolFleet.Api.Data;
using ToolFleet.Api.Endpoints;
using ToolFleet.Api.Infrastructure;
using ToolFleet.Api.Storage;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddOpenApi();
builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<DomainExceptionHandler>();
builder.Services.AddHealthChecks();
builder.Services.AddHttpContextAccessor();
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddScoped<ICurrentUser, HttpCurrentUser>();

// Enums go over the wire as "Canary", not 1.
builder.Services.ConfigureHttpJsonOptions(o =>
    o.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));

builder.Services.AddDbContext<FleetDbContext>(options =>
{
    var connectionString = builder.Configuration.GetConnectionString("Fleet");
    if (DatabaseInitialiser.UsesSqlite(builder.Configuration))
        options.UseSqlite(connectionString);
    else
        // Azure SQL's free tier pauses when idle and takes a few seconds to wake; retry instead of failing.
        options.UseSqlServer(connectionString, sql => sql.EnableRetryOnFailure());
});

// Firmware files go to Azure Blob Storage (Azurite on a developer PC). See docs/azure-setup.md.
builder.Services.Configure<FirmwareStorageOptions>(builder.Configuration.GetSection(FirmwareStorageOptions.Section));
builder.Services.AddSingleton<IFirmwareStore, BlobFirmwareStore>();

var app = builder.Build();

app.UseExceptionHandler();
app.UseStatusCodePages();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi(); // JSON description at /openapi/v1.json
    await app.InitialiseDatabaseAsync(seed: true);
}
else if (app.Configuration.GetValue<bool>("Database:MigrateOnStartup"))
{
    // Azure: apply migrations on startup, and seed the demo fleet into an empty database if asked to.
    await app.InitialiseDatabaseAsync(seed: app.Configuration.GetValue<bool>("Database:SeedDemoData"));
}

// The website. The pipeline copies the Angular build into wwwroot, so one App Service hosts both.
app.UseDefaultFiles();
app.UseStaticFiles();

app.MapHealthChecks("/health");

var api = app.MapGroup("/api");
api.MapLocationEndpoints();
api.MapToolEndpoints();
api.MapFirmwareEndpoints();
api.MapRolloutEndpoints();
api.MapAuditEndpoints();

// Unknown /api routes are a 404, not the website. Every other unknown path is an Angular route
// (e.g. /rollouts/123 after a refresh), so it gets index.html and Angular takes over.
app.Map("/api/{**rest}", () => Results.NotFound());
app.MapFallbackToFile("index.html");

app.Run();

// Lets the test project's WebApplicationFactory<Program> see this class.
public partial class Program;
