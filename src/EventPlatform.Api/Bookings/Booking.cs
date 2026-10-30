namespace EventPlatform.Api.Bookings;

public enum BookingState { Held, Confirmed, Cancelled, Expired, PaymentReview }

public sealed class Booking
{
    public Guid Id { get; set; }
    public Guid EventId { get; set; }
    public Guid CustomerId { get; set; }
    public Guid RequestId { get; set; }
    public int Quantity { get; set; }
    public long UnitPriceMinor { get; set; }
    public long TotalMinor { get; set; }
    public string Currency { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
    public BookingState State { get; set; } = BookingState.Held;
}

public sealed class ReservationOptions
{
    public int HoldMinutes { get; set; } = 35;
    public bool RunExpiryWorker { get; set; } = true;
}

public sealed record BookingView(Guid BookingId, Guid EventId, string State, int Quantity,
    long TotalMinor, string Currency, DateTimeOffset ServerNow, DateTimeOffset ExpiresAt);

public sealed record ReservationResult(int Status, BookingView? Value = null, string? Error = null)
{
    public IResult ToResult() => Status switch
    {
        201 => Results.Created($"/api/bookings/{Value!.BookingId}", Value),
        200 => Results.Ok(Value),
        403 => Results.Forbid(),
        404 => Results.NotFound(),
        _ => Results.Problem(Error, statusCode: Status)
    };
}
