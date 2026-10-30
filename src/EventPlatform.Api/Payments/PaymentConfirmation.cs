using EventPlatform.Api.Bookings;
using EventPlatform.Api.Data;
using EventPlatform.Api.Tickets;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace EventPlatform.Api.Payments;

public sealed record VerifiedPayment(string EventId, string PaymentId, string SessionId,
    Guid BookingId, long AmountMinor, string Currency);
public enum PaymentDecision { Applied, Duplicate, Invalid, Unknown }

public sealed class PaymentConfirmation(AppDbContext db)
{
    public async Task<PaymentDecision> ApplyAsync(VerifiedPayment payment, CancellationToken ct)
    {
        var reference = await db.Bookings.AsNoTracking().SingleOrDefaultAsync(b => b.Id == payment.BookingId, ct);
        if (reference is null) return PaymentDecision.Unknown;
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var item = await Inventory.LockEventAsync(db, reference.EventId, ct);
        var booking = await db.Bookings.SingleAsync(b => b.Id == reference.Id, ct);
        var attempt = await db.PaymentAttempts.SingleOrDefaultAsync(p => p.BookingId == booking.Id, ct);
        if (attempt is null || attempt.Rejected || booking.TotalMinor == 0 || booking.TotalMinor != payment.AmountMinor
            || !string.Equals(booking.Currency, payment.Currency, StringComparison.OrdinalIgnoreCase)
            || attempt.SessionId is not null && attempt.SessionId != payment.SessionId
            || attempt.PaymentId is not null && attempt.PaymentId != payment.PaymentId)
            return PaymentDecision.Invalid;
        var processed = await db.ProcessedWebhooks.FindAsync([payment.EventId], ct);
        if (processed is not null) return processed.BookingId == booking.Id ? PaymentDecision.Duplicate : PaymentDecision.Invalid;
        var now = await Inventory.UtcNowAsync(db, ct);
        if (attempt.PaymentId is null)
        {
            attempt.SessionId = payment.SessionId;
            attempt.PaymentId = payment.PaymentId;
            var occupied = await db.Bookings.Where(b => b.EventId == booking.EventId && b.Id != booking.Id &&
                (b.State == BookingState.Confirmed || b.State == BookingState.Held && b.ExpiresAt > now))
                .SumAsync(b => (int?)b.Quantity, ct) ?? 0;
            if (booking.State is BookingState.Held or BookingState.Expired && item!.StartsAt > now
                && occupied + booking.Quantity <= item.Capacity)
            {
                Confirm(db, booking, now);
            }
            else
            {
                booking.State = BookingState.PaymentReview;
                db.RefundJobs.Add(new RefundJob {
                    Id = Guid.NewGuid(), BookingId = booking.Id, PaymentId = payment.PaymentId,
                    IdempotencyKey = $"refund:{booking.Id:N}", NextAttemptAt = now
                });
            }
        }
        db.ProcessedWebhooks.Add(new ProcessedWebhook { Id = payment.EventId, BookingId = booking.Id, ProcessedAt = now });
        try
        {
            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
            return PaymentDecision.Applied;
        }
        catch (DbUpdateException exception) when (exception.InnerException is PostgresException { SqlState: "23505" })
        {
            // A provider event/payment/session cannot be attached to two independently locked events.
            await transaction.RollbackAsync(ct);
            db.ChangeTracker.Clear();
            return PaymentDecision.Invalid;
        }
    }

    internal static void Confirm(AppDbContext db, Booking booking, DateTimeOffset now)
    {
        booking.State = BookingState.Confirmed;
        for (var number = 1; number <= booking.Quantity; number++)
            db.Tickets.Add(new Ticket { Id = Guid.NewGuid(), BookingId = booking.Id, Number = number, IssuedAt = now });
    }
}
