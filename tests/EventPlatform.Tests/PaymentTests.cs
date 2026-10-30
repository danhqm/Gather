using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using EventPlatform.Api.Data;
using EventPlatform.Api.Payments;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace EventPlatform.Tests;

public sealed class FakeGateway : IPaymentGateway
{
    public bool Enabled => true;
    public List<CheckoutRequest> CheckoutRequests { get; } = [];
    public string? NextCheckoutFailure { get; set; }
    public Task<CheckoutSession> CreateCheckoutAsync(CheckoutRequest request, CancellationToken ct)
    {
        CheckoutRequests.Add(request);
        var failure = NextCheckoutFailure; NextCheckoutFailure = null;
        if (failure == "ambiguous") throw new HttpRequestException("Simulated timeout after provider accepted request");
        if (failure == "rejected") throw new CheckoutRejectedException();
        return Task.FromResult(new CheckoutSession($"cs_test_{request.BookingId:N}", "https://checkout.stripe.com/test", request.ExpiresAt));
    }
    public List<string> RefundKeys { get; } = [];
    public bool FailNextRefund { get; set; }
    public Task<RefundResult> RefundAsync(string paymentId, string key, CancellationToken ct)
    {
        RefundKeys.Add(key);
        if (FailNextRefund) { FailNextRefund = false; throw new HttpRequestException("Simulated ambiguous response"); }
        return Task.FromResult(new RefundResult("re_test_" + paymentId, "succeeded"));
    }
}
public sealed class PaymentFixture : WebAppFixture
{
    public PaymentFixture() => ConfigureServices = services => {
        services.RemoveAll<IPaymentGateway>(); services.AddSingleton<IPaymentGateway, FakeGateway>();
    };
}

public sealed class PaymentTests(PaymentFixture fixture) : IClassFixture<PaymentFixture>
{
    private async Task<(HttpClient Client, Guid EventId, Guid BookingId)> Hold(int capacity = 4, int quantity = 2)
    {
        var client = await fixture.CustomerClientAsync();
        var eventId = await fixture.CreatePublishedEventAsync(capacity);
        var response = await WebAppFixture.SendAsync(client, $"/api/events/{eventId}/reservations", new { quantity, requestId = Guid.NewGuid() });
        response.EnsureSuccessStatusCode();
        var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var bookingId = body.RootElement.GetProperty("bookingId").GetGuid();
        var checkout = await WebAppFixture.SendAsync(client, $"/api/bookings/{bookingId}/checkout", new { });
        Assert.Equal(HttpStatusCode.OK, checkout.StatusCode);
        return (client, eventId, bookingId);
    }

    private Task<HttpResponseMessage> Notify(Guid bookingId, string eventId, string? paymentId = null,
        long amount = 4000, string currency = "myr", bool valid = true, bool live = false, string type = "checkout.session.completed", HttpClient? target = null)
    {
        var payload = JsonSerializer.Serialize(new {
            id = eventId, @object = "event", api_version = Stripe.StripeConfiguration.ApiVersion,
            type, livemode = live,
            data = new { @object = new {
                id = $"cs_test_{bookingId:N}", @object = "checkout.session", mode = "payment", livemode = live,
                payment_status = "paid", payment_intent = paymentId ?? $"pi_{bookingId:N}",
                client_reference_id = bookingId.ToString(), amount_total = amount, currency,
                metadata = new { bookingId = bookingId.ToString() }
            } }
        });
        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var signature = Convert.ToHexString(HMACSHA256.HashData(Encoding.UTF8.GetBytes("whsec_integration_tests_only"),
            Encoding.UTF8.GetBytes($"{timestamp}.{payload}"))).ToLowerInvariant();
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/payments/webhook") {
            Content = new StringContent(payload, Encoding.UTF8, "application/json") };
        request.Headers.Add("Stripe-Signature", $"t={timestamp},v1={(valid ? signature : "invalid")}");
        return (target ?? fixture.Client).SendAsync(request);
    }

    private async Task<long> Tickets(Guid id)
    {
        await using var scope = fixture.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<AppDbContext>().Database
            .SqlQuery<long>($"SELECT COUNT(*) AS \"Value\" FROM \"Tickets\" WHERE \"BookingId\" = {id}").SingleAsync();
    }
    private async Task Expire(Guid id)
    {
        await using var scope = fixture.Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE \"Bookings\" SET \"CreatedAt\" = clock_timestamp() - interval '1 hour', \"ExpiresAt\" = clock_timestamp() - interval '1 minute' WHERE \"Id\" = {id}");
    }

    [Fact]
    public async Task Duplicate_and_concurrent_notifications_issue_exactly_one_ticket_set()
    {
        var (_, _, id) = await Hold();
        var eventId = "evt_" + Guid.NewGuid().ToString("N");
        var replies = await Task.WhenAll(Notify(id, eventId), Notify(id, eventId), Notify(id, "evt_" + Guid.NewGuid().ToString("N")));
        Assert.All(replies, response => Assert.Equal(HttpStatusCode.OK, response.StatusCode));
        Assert.Equal(2, await Tickets(id));
        using var restarted = fixture.WithEnvironment("Testing");
        using var restartedClient = restarted.CreateClient(new() { AllowAutoRedirect = false });
        Assert.Equal(HttpStatusCode.OK, (await Notify(id, "evt_replay_" + id, target: restartedClient)).StatusCode);
        Assert.Equal(2, await Tickets(id));
        Assert.Equal(HttpStatusCode.OK, (await Notify(id, "evt_expire_" + id, type: "checkout.session.expired")).StatusCode);
        Assert.Equal(2, await Tickets(id));
    }

    [Theory]
    [InlineData(3999, "myr", true, false)]
    [InlineData(4000, "usd", true, false)]
    [InlineData(4000, "myr", false, false)]
    [InlineData(4000, "myr", true, true)]
    public async Task Untrusted_or_mismatched_notifications_never_fulfil(long amount, string currency, bool valid, bool live)
    {
        var (_, _, id) = await Hold();
        Assert.Equal(HttpStatusCode.BadRequest, (await Notify(id, "evt_" + id, amount: amount, currency: currency, valid: valid, live: live)).StatusCode);
        Assert.Equal(0, await Tickets(id));
    }

    [Fact]
    public async Task Late_payment_reacquires_available_capacity()
    {
        var (_, _, id) = await Hold();
        await Expire(id);
        Assert.Equal(HttpStatusCode.OK, (await Notify(id, "evt_" + id)).StatusCode);
        Assert.Equal(2, await Tickets(id));
    }

    [Fact]
    public async Task Late_payment_without_capacity_queues_refund_and_never_issues_tickets()
    {
        var (client, eventId, id) = await Hold(2);
        await Expire(id);
        using var other = await fixture.CustomerClientAsync();
        var reserve = await WebAppFixture.SendAsync(other, $"/api/events/{eventId}/reservations", new { quantity = 2, requestId = Guid.NewGuid() });
        Assert.Equal(HttpStatusCode.Created, reserve.StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await Notify(id, "evt_" + id)).StatusCode);
        Assert.Equal(0, await Tickets(id));
        var body = JsonDocument.Parse(await (await client.GetAsync($"/api/bookings/{id}")).Content.ReadAsStringAsync());
        Assert.Equal("PaymentReview", body.RootElement.GetProperty("state").GetString());
        await using var scope = fixture.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.Equal(1, await db.Database.SqlQuery<long>($"SELECT COUNT(*) AS \"Value\" FROM \"RefundJobs\" WHERE \"BookingId\" = {id}").SingleAsync());
        var gateway = (FakeGateway)fixture.Services.GetRequiredService<IPaymentGateway>();
        gateway.FailNextRefund = true;
        await scope.ServiceProvider.GetRequiredService<RefundProcessor>().ProcessOneAsync(default);
        Assert.Equal("Pending", await db.RefundJobs.AsNoTracking().Where(j => j.BookingId == id).Select(j => j.State).SingleAsync());
        // Simulate a process restart with an abandoned lease; the next processor sees only persisted state.
        await db.RefundJobs.Where(j => j.BookingId == id).ExecuteUpdateAsync(update => update
            .SetProperty(j => j.State, "Running").SetProperty(j => j.NextAttemptAt, DateTimeOffset.UtcNow.AddMinutes(-1)));
        await using var restarted = fixture.Services.CreateAsyncScope();
        await restarted.ServiceProvider.GetRequiredService<RefundProcessor>().ProcessOneAsync(default);
        Assert.Equal("Succeeded", await db.RefundJobs.AsNoTracking().Where(j => j.BookingId == id).Select(j => j.State).SingleAsync());
        Assert.Equal(2, gateway.RefundKeys.Count);
        Assert.Single(gateway.RefundKeys.Distinct());
        Assert.Equal(0, await Tickets(id));
    }

    [Fact]
    public async Task Checkout_cannot_be_started_by_another_customer()
    {
        var (_, _, id) = await Hold();
        using var other = await fixture.CustomerClientAsync();
        Assert.Equal(HttpStatusCode.Forbidden, (await WebAppFixture.SendAsync(other, $"/api/bookings/{id}/checkout", new { })).StatusCode);
    }

    [Theory]
    [InlineData("ambiguous", "Held")]
    [InlineData("rejected", "Cancelled")]
    public async Task Hold_starts_checkout_and_retains_only_ambiguous_requests(string failure, string expectedState)
    {
        var gateway = (FakeGateway)fixture.Services.GetRequiredService<IPaymentGateway>();
        using var client = await fixture.CustomerClientAsync();
        var eventId = await fixture.CreatePublishedEventAsync(4);
        gateway.NextCheckoutFailure = failure;
        var response = await WebAppFixture.SendAsync(client, $"/api/events/{eventId}/reservations", new { quantity = 1, requestId = Guid.NewGuid() });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var id = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.GetProperty("bookingId").GetGuid();
        Assert.Single(gateway.CheckoutRequests, r => r.BookingId == id);
        var body = JsonDocument.Parse(await (await client.GetAsync($"/api/bookings/{id}")).Content.ReadAsStringAsync());
        Assert.Equal(expectedState, body.RootElement.GetProperty("state").GetString());
        if (failure == "ambiguous")
        {
            Assert.Equal(HttpStatusCode.OK, (await WebAppFixture.SendAsync(client, $"/api/bookings/{id}/checkout", new { })).StatusCode);
            var requests = gateway.CheckoutRequests.Where(r => r.BookingId == id).ToList();
            Assert.Equal(2, requests.Count);
            Assert.Single(requests.Distinct());
        }
    }

    [Fact]
    public async Task Free_checkout_confirms_once_without_contacting_Stripe()
    {
        using var client = await fixture.CustomerClientAsync();
        var eventId = await fixture.CreatePublishedEventAsync(4, 0);
        var response = await WebAppFixture.SendAsync(client, $"/api/events/{eventId}/reservations", new { quantity = 3, requestId = Guid.NewGuid() });
        var id = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.GetProperty("bookingId").GetGuid();
        Assert.Equal(HttpStatusCode.OK, (await WebAppFixture.SendAsync(client, $"/api/bookings/{id}/checkout", new { })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await WebAppFixture.SendAsync(client, $"/api/bookings/{id}/checkout", new { })).StatusCode);
        Assert.Equal(3, await Tickets(id));
        Assert.DoesNotContain(((FakeGateway)fixture.Services.GetRequiredService<IPaymentGateway>()).CheckoutRequests, r => r.BookingId == id);
    }
}
