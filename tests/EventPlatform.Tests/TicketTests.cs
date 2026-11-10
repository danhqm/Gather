using System.Net;
using System.Text.Json;
using EventPlatform.Api.Auth;
using EventPlatform.Api.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace EventPlatform.Tests;

public sealed class TicketTests(WebAppFixture fixture) : IClassFixture<WebAppFixture>
{
    private async Task<(HttpClient Owner, Guid EventId, Guid TicketId, string Code)> Ticket()
    {
        var owner = await fixture.CustomerClientAsync();
        var eventId = await fixture.CreatePublishedEventAsync(4, 0);
        var hold = await WebAppFixture.SendAsync(owner, $"/api/events/{eventId}/reservations", new { quantity = 1, requestId = Guid.NewGuid() });
        var bookingId = JsonDocument.Parse(await hold.Content.ReadAsStringAsync()).RootElement.GetProperty("bookingId").GetGuid();
        (await WebAppFixture.SendAsync(owner, $"/api/bookings/{bookingId}/checkout", new { })).EnsureSuccessStatusCode();
        var tickets = await owner.GetAsync("/api/me/tickets");
        Assert.Equal(HttpStatusCode.OK, tickets.StatusCode);
        var id = JsonDocument.Parse(await tickets.Content.ReadAsStringAsync()).RootElement[0].GetProperty("id").GetGuid();
        var qr = await owner.GetAsync($"/api/me/tickets/{id}/qr");
        qr.EnsureSuccessStatusCode();
        Assert.Contains("no-store", qr.Headers.CacheControl!.ToString());
        var data = JsonDocument.Parse(await qr.Content.ReadAsStringAsync()).RootElement;
        Assert.StartsWith("data:image/png;base64,iVBOR", data.GetProperty("image").GetString());
        return (owner, eventId, id, data.GetProperty("code").GetString()!);
    }

    private async Task<HttpClient> Staff()
    {
        var client = fixture.CreateClient();
        (await WebAppFixture.SendAsync(client, "/api/auth/login", new { email = "staff@gather.test", password = "Local-Test-Password123!" })).EnsureSuccessStatusCode();
        return client;
    }
    private static Task<HttpResponseMessage> Scan(HttpClient staff, Guid eventId, string code) =>
        WebAppFixture.SendAsync(staff, $"/api/organiser/events/{eventId}/check-ins", new { code });

    [Fact]
    public async Task Tickets_and_codes_are_private_to_the_customer()
    {
        var (_, _, id, _) = await Ticket();
        using var other = await fixture.CustomerClientAsync();
        Assert.Equal(HttpStatusCode.Forbidden, (await other.GetAsync($"/api/me/tickets/{id}/qr")).StatusCode);
        var list = JsonDocument.Parse(await (await other.GetAsync("/api/me/tickets")).Content.ReadAsStringAsync());
        Assert.Equal(0, list.RootElement.GetArrayLength());
        Assert.Equal(HttpStatusCode.Unauthorized, (await fixture.Client.GetAsync($"/api/me/tickets/{id}/qr")).StatusCode);
    }

    [Fact]
    public async Task Altered_codes_and_codes_for_another_event_are_rejected()
    {
        var (_, eventId, _, code) = await Ticket();
        using var staff = await Staff();
        var altered = code[..20] + (code[20] == 'A' ? 'B' : 'A') + code[21..];
        Assert.Equal(HttpStatusCode.BadRequest, (await Scan(staff, eventId, altered)).StatusCode);
        var otherEvent = await fixture.CreatePublishedEventAsync(4);
        var response = await Scan(staff, otherEvent, code);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("wrong-event", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Customer_and_staff_from_another_organisation_cannot_admit()
    {
        var (owner, eventId, _, code) = await Ticket();
        Assert.Equal(HttpStatusCode.Forbidden, (await Scan(owner, eventId, code)).StatusCode);
        using var outsider = await fixture.CustomerClientAsync();
        var me = JsonDocument.Parse(await (await outsider.GetAsync("/api/auth/me")).Content.ReadAsStringAsync()).RootElement.GetProperty("id").GetGuid();
        await using var scope = fixture.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var organisation = new Organisation { Id = Guid.NewGuid(), Name = "Other organisation" };
        db.Organisations.Add(organisation);
        db.OrganisationMemberships.Add(new OrganisationMembership { OrganisationId = organisation.Id, UserId = me, Role = OrganisationRoles.CheckInStaff });
        await db.SaveChangesAsync();
        Assert.Equal(HttpStatusCode.Forbidden, (await Scan(outsider, eventId, code)).StatusCode);
    }

    [Fact]
    public async Task Simultaneous_scans_admit_once_even_after_unpublishing()
    {
        var (_, eventId, ticketId, code) = await Ticket();
        using var organiser = await fixture.OrganiserClientAsync();
        (await WebAppFixture.SendAsync(organiser, $"/api/organiser/events/{eventId}/unpublish", new { })).EnsureSuccessStatusCode();
        using var first = await Staff();
        using var second = await Staff();
        var responses = await Task.WhenAll(Scan(first, eventId, code), Scan(second, eventId, code));
        Assert.Single(responses, r => r.StatusCode == HttpStatusCode.OK);
        Assert.Single(responses, r => r.StatusCode == HttpStatusCode.Conflict);
        await using var scope = fixture.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.Equal(1, await db.Database.SqlQuery<long>($"SELECT COUNT(*) AS \"Value\" FROM \"Tickets\" WHERE \"Id\" = {ticketId} AND \"AdmittedAt\" IS NOT NULL").SingleAsync());
    }
}
