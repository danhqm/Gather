using EventPlatform.Api.Data;
using EventPlatform.Api.Auth;
using EventPlatform.Api.Events;
using EventPlatform.Api.Bookings;
using EventPlatform.Api.Payments;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddProblemDetails();
builder.Services.AddDbContext<AppDbContext>((services, options) => options.UseNpgsql(
    services.GetRequiredService<IConfiguration>().GetConnectionString("Database")
    ?? throw new InvalidOperationException("Set ConnectionStrings__Database before starting the API.")));
builder.Services.AddScoped<EventQuery>();
builder.Services.AddScoped<EventManagement>();
builder.Services.AddScoped<ReservationService>();
builder.Services.AddOptions<PaymentOptions>().BindConfiguration("Payments")
    .Validate(options => string.IsNullOrEmpty(options.ApiKey) || options.ApiKey.StartsWith("rk_test_") || options.ApiKey.StartsWith("sk_test_"),
        "Only Stripe sandbox/test keys are permitted.")
    .Validate(options => Uri.TryCreate(options.PublicOrigin, UriKind.Absolute, out var uri)
        && (uri.Scheme == "https" || uri.Scheme == "http" && uri.IsLoopback)
        && uri.AbsolutePath == "/" && string.IsNullOrEmpty(uri.Query) && string.IsNullOrEmpty(uri.Fragment) && string.IsNullOrEmpty(uri.UserInfo),
        "PublicOrigin must be an HTTPS origin or localhost HTTP origin.").ValidateOnStart();
builder.Services.AddSingleton<IPaymentGateway, SandboxPaymentGateway>();
builder.Services.AddScoped<CheckoutService>();
builder.Services.AddScoped<PaymentConfirmation>();
builder.Services.AddScoped<RefundProcessor>();
builder.Services.AddHostedService<RefundWorker>();
builder.Services.AddOptions<ReservationOptions>().BindConfiguration("Reservations")
    .Validate(options => options.HoldMinutes is >= 1 and <= 60, "HoldMinutes must be between 1 and 60.").ValidateOnStart();
builder.Services.AddHostedService<ReservationExpiryWorker>();
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
app.MapOrganiserEndpoints();
app.MapBookingEndpoints();
app.MapPaymentEndpoints();
app.UseDefaultFiles();
app.UseStaticFiles();
app.MapFallbackToFile("index.html");
app.Run();

public partial class Program { }
