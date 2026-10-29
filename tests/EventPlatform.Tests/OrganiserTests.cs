using System.Net;
using System.Text.Json;
using EventPlatform.Api.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace EventPlatform.Tests;

public sealed class OrganiserTests(WebAppFixture fixture) : IClassFixture<WebAppFixture>
{
    private static Dictionary<string, object?> ValidEvent() => new()
    {
        ["title"] = "A new gathering", ["description"] = "An evening of conversation.",
        ["category"] = "community", ["city"] = "Kuala Lumpur", ["venue"] = "Test venue",
        ["timezone"] = "Asia/Kuala_Lumpur", ["startsAt"] = DateTimeOffset.UtcNow.AddDays(7),
        ["endsAt"] = DateTimeOffset.UtcNow.AddDays(7).AddHours(2), ["currency"] = "MYR",
        ["priceMinor"] = 2000, ["capacity"] = 30, ["imageUrl"] = null
    };

    [Fact]
    public async Task Organiser_can_create_publish_and_unpublish_an_event()
    {
        using var client = await fixture.OrganiserClientAsync();
        var created = await WebAppFixture.SendAsync(client, "/api/organiser/events",
            new { organisationId = DemoAccounts.OrganisationId, @event = ValidEvent() });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        using var item = JsonDocument.Parse(await created.Content.ReadAsStringAsync());
        var id = item.RootElement.GetProperty("id").GetGuid();
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/events/{id}")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await WebAppFixture.SendAsync(client, $"/api/organiser/events/{id}/publish", new { })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync($"/api/events/{id}")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await WebAppFixture.SendAsync(client, $"/api/organiser/events/{id}/unpublish", new { })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/events/{id}")).StatusCode);
        using var catalogue = JsonDocument.Parse(await client.GetStringAsync("/api/events?pageSize=100"));
        Assert.DoesNotContain(catalogue.RootElement.GetProperty("items").EnumerateArray(), e => e.GetProperty("id").GetGuid() == id);
    }

    [Fact]
    public async Task Customer_cannot_edit_an_event()
    {
        using var client = fixture.CreateClient();
        var registered = await WebAppFixture.SendAsync(client, "/api/auth/register", new {
            email = $"customer-{Guid.NewGuid():N}@example.test", displayName = "Attendee", password = "Local-Test-Password123!" });
        Assert.Equal(HttpStatusCode.Created, registered.StatusCode);
        var response = await WebAppFixture.SendAsync(client, "/api/organiser/events/00000000-0000-0000-0000-000000000001", ValidEvent(), HttpMethod.Put);
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        await using var scope = fixture.Services.CreateAsyncScope();
        var original = await scope.ServiceProvider.GetRequiredService<AppDbContext>().Events.FindAsync(Guid.Parse("00000000-0000-0000-0000-000000000001"));
        Assert.Equal("Make something tangible", original!.Title);
    }

    [Theory]
    [InlineData("capacity", 0)]
    [InlineData("currency", "BAD")]
    [InlineData("timezone", "Invalid/Zone")]
    [InlineData("priceMinor", -1)]
    [InlineData("imageUrl", "javascript:alert(1)")]
    public async Task Invalid_event_fields_are_rejected(string field, object invalid)
    {
        using var client = await fixture.OrganiserClientAsync();
        var body = ValidEvent(); body[field] = invalid;
        await using var scope = fixture.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var before = await db.Events.CountAsync();
        var response = await WebAppFixture.SendAsync(client, "/api/organiser/events", new { organisationId = DemoAccounts.OrganisationId, @event = body });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(before, await db.Events.CountAsync());
    }

    [Fact]
    public async Task Organiser_cannot_list_or_edit_another_organisations_event()
    {
        using var client = await fixture.OrganiserClientAsync();
        var eventId = await fixture.CreatePublishedEventAsync(10);
        var otherId = Guid.NewGuid();
        await using var scope = fixture.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.Organisations.Add(new() { Id = otherId, Name = "Independent organiser" });
        var item = await db.Events.FindAsync(eventId);
        item!.OrganisationId = otherId;
        await db.SaveChangesAsync();
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync($"/api/organiser/events?organisationId={otherId}")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await WebAppFixture.SendAsync(client, $"/api/organiser/events/{eventId}", ValidEvent(), HttpMethod.Put)).StatusCode);
        await db.Entry(item).ReloadAsync();
        Assert.Equal("Integration test event", item.Title);
    }

    [Fact]
    public async Task An_event_in_the_past_cannot_be_published()
    {
        using var client = await fixture.OrganiserClientAsync();
        var body = ValidEvent(); body["startsAt"] = DateTimeOffset.UtcNow.AddDays(-2); body["endsAt"] = DateTimeOffset.UtcNow.AddDays(-1);
        var response = await WebAppFixture.SendAsync(client, "/api/organiser/events", new { organisationId = DemoAccounts.OrganisationId, @event = body });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        using var item = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var id = item.RootElement.GetProperty("id").GetGuid();
        Assert.Equal(HttpStatusCode.BadRequest, (await WebAppFixture.SendAsync(client, $"/api/organiser/events/{id}/publish", new { })).StatusCode);
    }
}
