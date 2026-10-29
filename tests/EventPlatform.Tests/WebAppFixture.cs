using EventPlatform.Api.Data;
using EventPlatform.Api.Events;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Testcontainers.PostgreSql;
using System.Net.Http.Json;
using System.Text.Json;

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

    public static async Task<HttpResponseMessage> SendAsync(HttpClient client, string path, object body, HttpMethod? method = null)
    {
        using var csrfResponse = await client.GetAsync("/api/auth/csrf");
        csrfResponse.EnsureSuccessStatusCode();
        using var csrf = JsonDocument.Parse(await csrfResponse.Content.ReadAsStringAsync());
        using var request = new HttpRequestMessage(method ?? HttpMethod.Post, path) { Content = JsonContent.Create(body) };
        request.Headers.Add("X-CSRF-TOKEN", csrf.RootElement.GetProperty("token").GetString());
        return await client.SendAsync(request);
    }

    public async Task<HttpClient> OrganiserClientAsync()
    {
        var client = CreateClient();
        using var login = await SendAsync(client, "/api/auth/login", new { email = "organiser@gather.test", password = "Local-Test-Password123!" });
        login.EnsureSuccessStatusCode();
        return client;
    }

    public async Task<Guid> CreatePublishedEventAsync(int capacity, long priceMinor = 2000)
    {
        await using var scope = Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var item = new Event
        {
            Id = Guid.NewGuid(), Title = "Integration test event", Category = "workshop",
            OrganisationId = DemoAccounts.OrganisationId,
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
