using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using EventPlatform.Api.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace EventPlatform.Tests;

public sealed class ReservationTests(WebAppFixture fixture) : IClassFixture<WebAppFixture>
{
    private static Task<HttpResponseMessage> Reserve(HttpClient client, Guid eventId, int quantity, Guid? requestId = null) =>
        WebAppFixture.SendAsync(client, $"/api/events/{eventId}/reservations", new { quantity, requestId = requestId ?? Guid.NewGuid() });
    private static async Task<JsonElement> Body(HttpResponseMessage response) =>
        JsonSerializer.Deserialize<JsonElement>(await response.Content.ReadAsStringAsync());

    [Theory]
    [InlineData(0)]
    [InlineData(5)]
    public async Task Quantity_must_be_between_one_and_four(int quantity)
    {
        using var client = await fixture.CustomerClientAsync();
        var id = await fixture.CreatePublishedEventAsync(10);
        Assert.Equal(HttpStatusCode.BadRequest, (await Reserve(client, id, quantity)).StatusCode);
    }

    [Fact]
    public async Task Two_customers_competing_for_one_ticket_have_exactly_one_winner()
    {
        var id = await fixture.CreatePublishedEventAsync(1);
        using var first = await fixture.CustomerClientAsync();
        using var second = await fixture.CustomerClientAsync();
        async Task<HttpRequestMessage> Request(HttpClient client)
        {
            var token = await Body(await client.GetAsync("/api/auth/csrf"));
            var request = new HttpRequestMessage(HttpMethod.Post, $"/api/events/{id}/reservations") {
                Content = JsonContent.Create(new { quantity = 1, requestId = Guid.NewGuid() }) };
            request.Headers.Add("X-CSRF-TOKEN", token.GetProperty("token").GetString());
            return request;
        }
        using var firstRequest = await Request(first);
        using var secondRequest = await Request(second);
        using var barrier = new Barrier(2);
        Task<HttpResponseMessage> Send(HttpClient client, HttpRequestMessage request) => Task.Run(async () =>
        {
            Assert.True(barrier.SignalAndWait(TimeSpan.FromSeconds(10)));
            return await client.SendAsync(request);
        });
        var replies = await Task.WhenAll(Send(first, firstRequest), Send(second, secondRequest));
        Assert.Single(replies, r => r.StatusCode == HttpStatusCode.Created);
        Assert.Single(replies, r => r.StatusCode == HttpStatusCode.Conflict);
        await using var scope = fixture.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.Equal(1, await db.Database.SqlQuery<long>($"SELECT COUNT(*) AS \"Value\" FROM \"Bookings\" WHERE \"EventId\" = {id}").SingleAsync());
    }

    [Fact]
    public async Task Replaying_a_request_returns_the_same_booking_and_rejects_changed_quantity()
    {
        using var client = await fixture.CustomerClientAsync();
        var id = await fixture.CreatePublishedEventAsync(10);
        var requestId = Guid.NewGuid();
        var first = await Reserve(client, id, 2, requestId);
        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        var replay = await Reserve(client, id, 2, requestId);
        Assert.Equal(HttpStatusCode.OK, replay.StatusCode);
        Assert.Equal((await Body(first)).GetProperty("bookingId").GetGuid(), (await Body(replay)).GetProperty("bookingId").GetGuid());
        Assert.Equal(HttpStatusCode.Conflict, (await Reserve(client, id, 3, requestId)).StatusCode);
    }

    [Fact]
    public async Task Concurrent_reuse_of_one_request_across_events_creates_only_one_booking()
    {
        using var client = await fixture.CustomerClientAsync();
        var firstId = await fixture.CreatePublishedEventAsync(1);
        var secondId = await fixture.CreatePublishedEventAsync(1);
        var requestId = Guid.NewGuid();
        var responses = await Task.WhenAll(Reserve(client, firstId, 1, requestId), Reserve(client, secondId, 1, requestId));
        Assert.Single(responses, response => response.StatusCode == HttpStatusCode.Created);
        Assert.Single(responses, response => response.StatusCode == HttpStatusCode.Conflict);
        await using var scope = fixture.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.Equal(1, await db.Database.SqlQuery<long>($"SELECT COUNT(*) AS \"Value\" FROM \"Bookings\" WHERE \"RequestId\" = {requestId}").SingleAsync());
    }

    [Fact]
    public async Task Only_the_customer_can_read_or_cancel_their_booking()
    {
        using var owner = await fixture.CustomerClientAsync();
        using var other = await fixture.CustomerClientAsync();
        var id = await fixture.CreatePublishedEventAsync(1);
        var response = await Reserve(owner, id, 1);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var bookingId = (await Body(response)).GetProperty("bookingId").GetGuid();
        Assert.Equal(HttpStatusCode.Forbidden, (await other.GetAsync($"/api/bookings/{bookingId}")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await WebAppFixture.SendAsync(other, $"/api/bookings/{bookingId}/cancel", new { })).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await Reserve(other, id, 1)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await WebAppFixture.SendAsync(owner, $"/api/bookings/{bookingId}/cancel", new { })).StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await Reserve(other, id, 1)).StatusCode);
    }

    [Fact]
    public async Task Expired_holds_release_capacity_without_the_cleanup_worker()
    {
        using var first = await fixture.CustomerClientAsync();
        using var second = await fixture.CustomerClientAsync();
        var id = await fixture.CreatePublishedEventAsync(1);
        var response = await Reserve(first, id, 1);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var bookingId = (await Body(response)).GetProperty("bookingId").GetGuid();
        Assert.Equal(HttpStatusCode.Conflict, (await Reserve(second, id, 1)).StatusCode);
        await using var scope = fixture.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE \"Bookings\" SET \"CreatedAt\" = clock_timestamp() - interval '1 hour', \"ExpiresAt\" = clock_timestamp() - interval '1 minute' WHERE \"Id\" = {bookingId}");
        Assert.Equal("Expired", (await Body(await first.GetAsync($"/api/bookings/{bookingId}"))).GetProperty("state").GetString());
        Assert.Equal(HttpStatusCode.Created, (await Reserve(second, id, 1)).StatusCode);
    }

    [Fact]
    public async Task Expiry_and_price_come_from_the_server_despite_a_wrong_client_clock()
    {
        using var client = await fixture.CustomerClientAsync();
        client.DefaultRequestHeaders.Date = DateTimeOffset.UtcNow.AddYears(5);
        var id = await fixture.CreatePublishedEventAsync(10, 2550);
        var response = await Reserve(client, id, 3);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var booking = await Body(response);
        Assert.Equal(7650, booking.GetProperty("totalMinor").GetInt64());
        var now = booking.GetProperty("serverNow").GetDateTimeOffset();
        Assert.InRange((now - DateTimeOffset.UtcNow).TotalSeconds, -10, 10);
        Assert.Equal(TimeSpan.FromMinutes(35), booking.GetProperty("expiresAt").GetDateTimeOffset() - now);
    }

    [Fact]
    public async Task Bookings_freeze_inventory_fields_and_survive_unpublishing()
    {
        using var customer = await fixture.CustomerClientAsync();
        using var organiser = await fixture.OrganiserClientAsync();
        var id = await fixture.CreatePublishedEventAsync(10);
        var response = await Reserve(customer, id, 1);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var bookingId = (await Body(response)).GetProperty("bookingId").GetGuid();
        await using var scope = fixture.Services.CreateAsyncScope();
        var item = await scope.ServiceProvider.GetRequiredService<AppDbContext>().Events.FindAsync(id);
        foreach (var changed in new[] { "capacity", "priceMinor", "startsAt" })
        {
            var update = new Dictionary<string, object?> {
                ["title"] = item!.Title, ["description"] = "Updated description", ["category"] = item.Category,
                ["city"] = item.City, ["venue"] = item.Venue, ["timezone"] = item.Timezone,
                ["startsAt"] = item.StartsAt, ["endsAt"] = item.EndsAt, ["currency"] = item.Currency,
                ["priceMinor"] = item.PriceMinor, ["capacity"] = item.Capacity
            };
            update[changed] = changed == "startsAt" ? item.StartsAt.AddMinutes(30) : changed == "capacity" ? 20 : 9999;
            Assert.Equal(HttpStatusCode.Conflict, (await WebAppFixture.SendAsync(organiser, $"/api/organiser/events/{id}", update, HttpMethod.Put)).StatusCode);
        }
        Assert.Equal(HttpStatusCode.OK, (await WebAppFixture.SendAsync(organiser, $"/api/organiser/events/{id}/unpublish", new { })).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await Reserve(customer, id, 1)).StatusCode);
        Assert.Equal("Held", (await Body(await customer.GetAsync($"/api/bookings/{bookingId}"))).GetProperty("state").GetString());
    }
}
