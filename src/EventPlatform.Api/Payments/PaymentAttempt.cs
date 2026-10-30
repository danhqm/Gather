namespace EventPlatform.Api.Payments;

public sealed class PaymentAttempt
{
    public Guid BookingId { get; set; }
    public string IdempotencyKey { get; set; } = "";
    public DateTimeOffset ExpiresAt { get; set; }
    public string? SessionId { get; set; }
    public string? CheckoutUrl { get; set; }
    public string? PaymentId { get; set; }
    public bool Rejected { get; set; }
}

public sealed class ProcessedWebhook
{
    public string Id { get; set; } = "";
    public Guid BookingId { get; set; }
    public DateTimeOffset ProcessedAt { get; set; }
}

public sealed class RefundJob
{
    public Guid Id { get; set; }
    public Guid BookingId { get; set; }
    public string PaymentId { get; set; } = "";
    public string IdempotencyKey { get; set; } = "";
    public string State { get; set; } = "Pending";
    public string? ProviderRefundId { get; set; }
    public DateTimeOffset? FirstAttemptAt { get; set; }
    public DateTimeOffset NextAttemptAt { get; set; }
    public int Attempts { get; set; }
}

public sealed class PaymentOptions
{
    public string ApiKey { get; set; } = "";
    public string WebhookSecret { get; set; } = "";
    public string PublicOrigin { get; set; } = "http://localhost:8080";
    public string PaymentMethodConfiguration { get; set; } = "";
    public bool RunRefundWorker { get; set; } = true;
    public bool Enabled => !string.IsNullOrWhiteSpace(ApiKey) && !string.IsNullOrWhiteSpace(WebhookSecret);
}
