using EventPlatform.Api.Data;
using EventPlatform.Api.Events;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Testcontainers.PostgreSql;

namespace EventPlatform.Tests;

public sealed class WebAppFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer database = new PostgreSqlBuilder("postgres:18-alpine").Build();
    private WebApplicationFactory<Program> factory = null!;
    public HttpClient Client { get; private set; } = null!;
    public IServiceProvider Services => factory.Services;

    public async Task InitializeAsync()
    {
        await database.StartAsync();
        factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder => builder
            .UseEnvironment("Testing")
            .ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:Database"] = database.GetConnectionString(),
                ["Database:Initialize"] = "true", ["Demo:Seed"] = "true",
                ["Demo:AccountPassword"] = "Local-Test-Password123!"
            })));
        Client = CreateClient();
    }

    public HttpClient CreateClient() => factory.CreateClient(new() { AllowAutoRedirect = false });
    public WebApplicationFactory<Program> WithEnvironment(string environment) =>
        factory.WithWebHostBuilder(builder => builder.UseEnvironment(environment));

    public async Task<Guid> CreatePublishedEventAsync(int capacity, long priceMinor = 2000)
    {
        await using var scope = Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var item = new Event
        {
            Id = Guid.NewGuid(), Title = "Integration test event", Category = "workshop",
            City = "Kuala Lumpur", Venue = "Test venue", Timezone = "Asia/Kuala_Lumpur",
            StartsAt = DateTimeOffset.UtcNow.AddDays(7), EndsAt = DateTimeOffset.UtcNow.AddDays(7).AddHours(2),
            PriceMinor = priceMinor, Currency = "MYR", Capacity = capacity, IsPublished = true
        };
        db.Events.Add(item);
        await db.SaveChangesAsync();
        return item.Id;
    }

    public async Task DisposeAsync()
    {
        Client?.Dispose();
        if (factory is not null) await factory.DisposeAsync();
        await database.DisposeAsync();
    }
}
