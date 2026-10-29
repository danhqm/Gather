using EventPlatform.Api.Data;
using EventPlatform.Api.Auth;
using EventPlatform.Api.Events;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddProblemDetails();
builder.Services.AddDbContext<AppDbContext>((services, options) => options.UseNpgsql(
    services.GetRequiredService<IConfiguration>().GetConnectionString("Database")
    ?? throw new InvalidOperationException("Set ConnectionStrings__Database before starting the API.")));
builder.Services.AddScoped<EventQuery>();
builder.AddAccounts();
var app = builder.Build();
app.UseExceptionHandler();
app.UseAuthentication();
app.UseAuthorization();
app.UseRateLimiter();

if (app.Configuration.GetValue<bool>("Database:Initialize"))
{
    await using var scope = app.Services.CreateAsyncScope();
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    await db.Database.MigrateAsync();
    if (app.Configuration.GetValue<bool>("Demo:Seed"))
    {
        await DemoSeeder.SeedAsync(db);
        await DemoAccounts.SeedAsync(scope.ServiceProvider, app.Configuration);
    }
}

app.MapGet("/api/health", async (AppDbContext db, CancellationToken ct) =>
    await db.Database.CanConnectAsync(ct) ? Results.Ok(new { status = "healthy" }) : Results.StatusCode(503));
app.MapEventEndpoints();
app.MapAuthEndpoints();
app.UseDefaultFiles();
app.UseStaticFiles();
app.MapFallbackToFile("index.html");
app.Run();

public partial class Program { }
