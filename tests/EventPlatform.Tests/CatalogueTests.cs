using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Testcontainers.PostgreSql;

namespace EventPlatform.Tests;

public sealed class CatalogueTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer database = new PostgreSqlBuilder("postgres:18-alpine").Build();
    private WebApplicationFactory<Program> factory = null!;
    private HttpClient client = null!;
    public async Task InitializeAsync()
    {
        await database.StartAsync();
        factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder => builder
            .UseEnvironment("Testing")
            .ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string,string?> {
                ["ConnectionStrings:Database"] = database.GetConnectionString(),
                ["Database:Initialize"] = "true", ["Demo:Seed"] = "true"
            })));
        client = factory.CreateClient();
    }
    public async Task DisposeAsync() { client?.Dispose(); if(factory is not null) await factory.DisposeAsync(); await database.DisposeAsync(); }

    [Fact]
    public async Task Catalogue_excludes_drafts_and_applies_combined_filters()
    {
        var response = await client.GetAsync("/api/events?category=workshop&maxPriceMinor=3000&currency=MYR");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var items = body.RootElement.GetProperty("items").EnumerateArray().ToArray();
        Assert.Single(items);
        Assert.Equal("Make something tangible", items[0].GetProperty("title").GetString());
        Assert.Equal(2500, items[0].GetProperty("priceMinor").GetInt64());
    }
    [Fact]
    public async Task City_and_UTC_date_filters_return_only_matching_events()
    {
        var response = await client.GetAsync("/api/events?city=George%20Town&from=2026-11-21T00%3A00%3A00Z&to=2026-11-22T00%3A00%3A00Z");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var item = Assert.Single(body.RootElement.GetProperty("items").EnumerateArray().ToArray());
        Assert.Equal("A seat at the long table", item.GetProperty("title").GetString());
    }
    [Fact]
    public async Task Pages_are_stable_and_do_not_include_the_seeded_draft()
    {
        using var first = JsonDocument.Parse(await client.GetStringAsync("/api/events?pageSize=2"));
        using var second = JsonDocument.Parse(await client.GetStringAsync("/api/events?page=2&pageSize=2"));
        Assert.Equal(6, first.RootElement.GetProperty("total").GetInt32());
        var firstIds = first.RootElement.GetProperty("items").EnumerateArray().Select(e => e.GetProperty("id").GetGuid()).ToArray();
        var secondIds = second.RootElement.GetProperty("items").EnumerateArray().Select(e => e.GetProperty("id").GetGuid()).ToArray();
        Assert.Equal(2, firstIds.Length);
        Assert.Equal(2, secondIds.Length);
        Assert.Empty(firstIds.Intersect(secondIds));
        Assert.Equal(Guid.Parse("00000000-0000-0000-0000-000000000001"), firstIds[0]);
    }
    [Fact]
    public async Task Draft_cannot_be_read_by_direct_id()
    {
        var response = await client.GetAsync("/api/events/00000000-0000-0000-0000-000000000099");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
    [Fact]
    public async Task Pagination_and_invalid_filters_are_bounded()
    {
        Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync("/api/events?pageSize=101")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync("/api/events?maxPriceMinor=-1")).StatusCode);
    }
    [Fact]
    public async Task Catalogue_local_day_handles_spring_DST()
    {
        var response = await client.GetAsync("/api/date-window?from=2026-03-08&to=2026-03-09&timezone=America%2FNew_York");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(DateTimeOffset.Parse("2026-03-08T05:00:00Z"),body.RootElement.GetProperty("from").GetDateTimeOffset());
        Assert.Equal(DateTimeOffset.Parse("2026-03-09T04:00:00Z"),body.RootElement.GetProperty("to").GetDateTimeOffset());
    }
}
