using Microsoft.Extensions.Options;
using Stripe;
using Stripe.Checkout;

namespace EventPlatform.Api.Payments;

public sealed class SandboxPaymentGateway(IOptions<PaymentOptions> configuration) : IPaymentGateway
{
    private PaymentOptions Options => configuration.Value;
    public bool Enabled => Options.Enabled;
    private StripeClient Client() => new(Options.ApiKey);

    public async Task<CheckoutSession> CreateCheckoutAsync(CheckoutRequest request, CancellationToken ct)
    {
        var options = new SessionCreateOptions {
            Mode = "payment", ClientReferenceId = request.BookingId.ToString(),
            Metadata = new() { ["bookingId"] = request.BookingId.ToString() },
            ExpiresAt = request.ExpiresAt.UtcDateTime,
            SuccessUrl = $"{Options.PublicOrigin}/bookings/{request.BookingId}?checkout=returned",
            CancelUrl = $"{Options.PublicOrigin}/bookings/{request.BookingId}?checkout=cancelled",
            IntegrationIdentifier = "gather-portfolio-mqztvpar",
            PaymentMethodConfiguration = string.IsNullOrWhiteSpace(Options.PaymentMethodConfiguration) ? null : Options.PaymentMethodConfiguration,
            LineItems = [new SessionLineItemOptions {
                Quantity = 1, PriceData = new SessionLineItemPriceDataOptions {
                    Currency = request.Currency.ToLowerInvariant(), UnitAmount = request.AmountMinor,
                    ProductData = new SessionLineItemPriceDataProductDataOptions { Name = "Gather demo event reservation" }
                }
            }]
        };
        try
        {
            var session = await Client().V1.Checkout.Sessions.CreateAsync(options,
                new RequestOptions { IdempotencyKey = request.IdempotencyKey }, ct);
            if (session.Livemode) throw new InvalidOperationException("Live payments are disabled.");
            return new(session.Id, session.Url, new DateTimeOffset(DateTime.SpecifyKind(session.ExpiresAt, DateTimeKind.Utc)));
        }
        catch (StripeException error) when (error.HttpStatusCode == System.Net.HttpStatusCode.BadRequest
            && error.StripeError?.Type == "invalid_request_error")
        {
            throw new CheckoutRejectedException();
        }
    }

    public async Task<RefundResult> RefundAsync(string paymentId, string idempotencyKey, CancellationToken ct)
    {
        var refund = await Client().V1.Refunds.CreateAsync(new RefundCreateOptions { PaymentIntent = paymentId },
            new RequestOptions { IdempotencyKey = idempotencyKey }, ct);
        return new(refund.Id, refund.Status);
    }
}
