using System.Security.Claims;
using EventPlatform.Api.Auth;
using Microsoft.Extensions.Options;
using Stripe;
using Stripe.Checkout;

namespace EventPlatform.Api.Payments;

public static class PaymentEndpoints
{
    public static void MapPaymentEndpoints(this WebApplication app)
    {
        app.MapGet("/api/payments/config", (IPaymentGateway gateway) => Results.Ok(new { sandboxCheckoutEnabled = gateway.Enabled }));
        app.MapPost("/api/bookings/{bookingId:guid}/checkout", async (Guid bookingId, ClaimsPrincipal user,
            CheckoutService checkout, CancellationToken ct) =>
            (await checkout.EnsureAsync(Guid.Parse(user.FindFirstValue(ClaimTypes.NameIdentifier)!), bookingId, ct)).ToResult())
            .RequireAuthorization().AddEndpointFilter<CsrfFilter>();

        app.MapPost("/api/payments/webhook", async (HttpRequest request, IOptions<PaymentOptions> options,
            PaymentConfirmation confirmation, CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(options.Value.WebhookSecret)) return Results.StatusCode(503);
            if (request.ContentLength > 262144) return Results.StatusCode(413);
            // Stream with an explicit bound so chunked bodies cannot bypass Content-Length checks.
            using var body = new MemoryStream();
            var buffer = new byte[8192];
            int count;
            while ((count = await request.Body.ReadAsync(buffer, ct)) > 0)
            {
                if (body.Length + count > 262144) return Results.StatusCode(413);
                await body.WriteAsync(buffer.AsMemory(0, count), ct);
            }
            Stripe.Event notification;
            try
            {
                notification = EventUtility.ConstructEvent(System.Text.Encoding.UTF8.GetString(body.ToArray()),
                    request.Headers["Stripe-Signature"], options.Value.WebhookSecret);
            }
            catch (Exception error) when (error is StripeException or System.Text.Json.JsonException or ArgumentException)
            {
                return Results.BadRequest();
            }
            if (notification.Livemode) return Results.BadRequest();
            if (notification.Type is not ("checkout.session.completed" or "checkout.session.async_payment_succeeded"))
                return Results.Ok();
            if (notification.Data.Object is not Session session || session.Livemode || session.Mode != "payment") return Results.BadRequest();
            if (session.PaymentStatus != "paid") return Results.Ok();
            if (!Guid.TryParse(session.ClientReferenceId, out var bookingId)
                || session.Metadata?.GetValueOrDefault("bookingId") != bookingId.ToString()
                || string.IsNullOrEmpty(session.PaymentIntentId) || session.AmountTotal is null
                || string.IsNullOrEmpty(session.Id) || string.IsNullOrEmpty(notification.Id)) return Results.BadRequest();
            var decision = await confirmation.ApplyAsync(new(notification.Id, session.PaymentIntentId, session.Id,
                bookingId, session.AmountTotal.Value, session.Currency), ct);
            return decision is PaymentDecision.Invalid or PaymentDecision.Unknown ? Results.BadRequest() : Results.Ok();
        });
    }
}
