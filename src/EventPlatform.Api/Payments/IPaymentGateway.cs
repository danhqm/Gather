namespace EventPlatform.Api.Payments;

public sealed record CheckoutRequest(Guid BookingId, long AmountMinor, string Currency,
    DateTimeOffset ExpiresAt, string IdempotencyKey);
public sealed record CheckoutSession(string Id, string Url, DateTimeOffset ExpiresAt);
public sealed record RefundResult(string Id, string Status);
public interface IPaymentGateway
{
    bool Enabled { get; }
    Task<CheckoutSession> CreateCheckoutAsync(CheckoutRequest request, CancellationToken ct);
    Task<RefundResult> RefundAsync(string providerPaymentId, string idempotencyKey, CancellationToken ct);
}
public sealed class CheckoutRejectedException : Exception;
