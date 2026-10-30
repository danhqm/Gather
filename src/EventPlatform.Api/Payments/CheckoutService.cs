using EventPlatform.Api.Bookings;
using EventPlatform.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace EventPlatform.Api.Payments;

public sealed record CheckoutResult(int Status, string? Url = null, string? Error = null)
{
    public IResult ToResult() => Status switch {
        200 => Results.Ok(new { url = Url }), 403 => Results.Forbid(), 404 => Results.NotFound(),
        _ => Results.Problem(Error, statusCode: Status)
    };
}

public sealed class CheckoutService(AppDbContext db, IPaymentGateway gateway)
{
    public async Task<CheckoutResult> EnsureAsync(Guid customerId, Guid bookingId, CancellationToken ct)
    {
        var reference = await db.Bookings.AsNoTracking().SingleOrDefaultAsync(b => b.Id == bookingId, ct);
        if (reference is null) return new(404);
        if (reference.CustomerId != customerId) return new(403);
        CheckoutRequest request;
        await using (var transaction = await db.Database.BeginTransactionAsync(ct))
        {
            var item = await Inventory.LockEventAsync(db, reference.EventId, ct);
            var booking = await db.Bookings.SingleAsync(b => b.Id == bookingId, ct);
            var now = await Inventory.UtcNowAsync(db, ct);
            if (booking.State == BookingState.Confirmed) return new(200);
            if (booking.State != BookingState.Held || booking.ExpiresAt <= now || item!.StartsAt <= now)
                return new(409, Error: "This reservation is no longer available for checkout.");
            if (booking.TotalMinor == 0)
            {
                PaymentConfirmation.Confirm(db, booking, now);
                await db.SaveChangesAsync(ct);
                await transaction.CommitAsync(ct);
                return new(200);
            }
            if (!gateway.Enabled) return new(503, Error: "Sandbox checkout is awaiting configuration. Your temporary hold is preserved.");
            var attempt = await db.PaymentAttempts.SingleOrDefaultAsync(p => p.BookingId == bookingId, ct);
            if (attempt?.Rejected == true) return new(409, Error: "Checkout was declined. Start a new reservation.");
            if (attempt?.CheckoutUrl is not null) return new(200, attempt.CheckoutUrl);
            // Stripe requires at least 30 minutes at creation; leave a minute for network transit.
            if (attempt is null && booking.ExpiresAt < now.AddMinutes(31))
                return new(409, Error: "Too little time remains to start checkout. Cancel this hold and reserve again.");
            if (attempt is null)
            {
                attempt = new PaymentAttempt { BookingId = bookingId, ExpiresAt = booking.ExpiresAt,
                    IdempotencyKey = $"checkout:{bookingId:N}" };
                db.PaymentAttempts.Add(attempt);
                await db.SaveChangesAsync(ct);
            }
            request = new(bookingId, booking.TotalMinor, booking.Currency, attempt.ExpiresAt, attempt.IdempotencyKey);
            await transaction.CommitAsync(ct);
        }
        // Never hold an inventory lock while talking to the payment provider.
        try
        {
            var session = await gateway.CreateCheckoutAsync(request, ct);
            db.ChangeTracker.Clear();
            await using var transaction = await db.Database.BeginTransactionAsync(ct);
            await Inventory.LockEventAsync(db, reference.EventId, ct);
            var attempt = await db.PaymentAttempts.SingleAsync(p => p.BookingId == bookingId, ct);
            if (attempt.SessionId is not null && attempt.SessionId != session.Id)
                return new(409, Error: "Checkout reconciliation is required.");
            attempt.SessionId = session.Id;
            attempt.CheckoutUrl = session.Url;
            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
            return new(200, session.Url);
        }
        catch (CheckoutRejectedException)
        {
            db.ChangeTracker.Clear();
            await using var transaction = await db.Database.BeginTransactionAsync(ct);
            await Inventory.LockEventAsync(db, reference.EventId, ct);
            var booking = await db.Bookings.SingleAsync(b => b.Id == bookingId, ct);
            var attempt = await db.PaymentAttempts.SingleAsync(p => p.BookingId == bookingId, ct);
            if (attempt.SessionId is null && attempt.PaymentId is null)
            {
                attempt.Rejected = true;
                if (booking.State == BookingState.Held) booking.State = BookingState.Cancelled;
                await db.SaveChangesAsync(ct);
            }
            await transaction.CommitAsync(ct);
            return new(409, Error: "The payment provider rejected checkout. The reservation was released.");
        }
        catch (Exception error) when (error is HttpRequestException or Stripe.StripeException or OperationCanceledException)
        {
            return new(503, Error: "Checkout could not be confirmed. Retry this booking; your reservation and payment request are preserved.");
        }
    }
}
