using GarmentStylePilot.Api.Data;
using GarmentStylePilot.Api.Features;
using Microsoft.EntityFrameworkCore;
using System.Text.Json.Serialization;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddOpenApi();
builder.Services.ConfigureHttpJsonOptions(options =>
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));
var provider = builder.Configuration["Database:Provider"] ?? "Sqlite";
builder.Services.AddDbContext<GarmentDbContext>(options =>
{
    if (provider.Equals("SqlServer", StringComparison.OrdinalIgnoreCase))
    {
        var cs = builder.Configuration.GetConnectionString("SqlServer");
        if (string.IsNullOrWhiteSpace(cs))
            throw new InvalidOperationException("ConnectionStrings:SqlServer is required.");
        options.UseSqlServer(cs);
    }
    else if (provider.Equals("Sqlite", StringComparison.OrdinalIgnoreCase))
    {
        options.UseSqlite(builder.Configuration.GetConnectionString("Sqlite")
            ?? "Data Source=garmentstylepilot.db");
    }
    else throw new InvalidOperationException("Supported Database:Provider values: Sqlite, SqlServer");
});

var app = builder.Build();

if (app.Environment.IsDevelopment())
    app.MapOpenApi();

var apiKey = app.Configuration["GSP_API_KEY"];
if (app.Environment.IsProduction() && string.IsNullOrWhiteSpace(apiKey))
    throw new InvalidOperationException("Set GSP_API_KEY before running in Production.");

app.Use(async (context, next) =>
{
    if (context.Request.Path.StartsWithSegments("/api") && !string.IsNullOrWhiteSpace(apiKey)
        && !string.Equals(context.Request.Headers["X-Api-Key"], apiKey, StringComparison.Ordinal))
    {
        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
        await context.Response.WriteAsJsonAsync(new { error = "Invalid X-Api-Key" });
        return;
    }
    await next();
});

// Bootstrap database for the prototype. Replace EnsureCreated with migrations before production.
await using (var scope = app.Services.CreateAsyncScope())
{
    var db = scope.ServiceProvider.GetRequiredService<GarmentDbContext>();
    await db.Database.EnsureCreatedAsync();
}

app.UseDefaultFiles();
app.UseStaticFiles();
app.MapGet("/healthz", () => Results.Ok(new { status = "ok" }));
app.MapGarmentEndpoints();
app.MapLinePlanningEndpoints();
app.MapOperationStandardEndpoints();
app.MapWorkstationEndpoints();
app.MapProductionHandoverEndpoints();
app.Run();

public partial class Program;
