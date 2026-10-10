using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using ToolFleet.Api.Data;
using ToolFleet.Api.Endpoints;
using ToolFleet.Api.Infrastructure;

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
        options.UseSqlServer(connectionString);
});

var app = builder.Build();

app.UseExceptionHandler();
app.UseStatusCodePages();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi(); // JSON description at /openapi/v1.json
    await app.InitialiseDatabaseAsync(seed: true);
}

app.MapHealthChecks("/health");

var api = app.MapGroup("/api");
api.MapLocationEndpoints();
api.MapToolEndpoints();
api.MapFirmwareEndpoints();
api.MapRolloutEndpoints();
api.MapAuditEndpoints();

app.Run();

// Lets the test project's WebApplicationFactory<Program> see this class.
public partial class Program;
