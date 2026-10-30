using EventPlatform.Api.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Npgsql;

namespace EventPlatform.Api.Bookings;

public sealed class ReservationService(AppDbContext db, IOptions<ReservationOptions> options)
{
    public async Task<ReservationResult> ReserveAsync(Guid customerId, Guid eventId, int quantity, Guid requestId, CancellationToken ct)
    {
        if (quantity is < 1 or > 4 || requestId == Guid.Empty) return new(400, Error: "Reserve one to four tickets with a request ID.");
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var item = await Inventory.LockEventAsync(db, eventId, ct);
        if (item is null) return new(404);
        var now = await Inventory.UtcNowAsync(db, ct);
        var existing = await db.Bookings.AsNoTracking().SingleOrDefaultAsync(b => b.CustomerId == customerId && b.RequestId == requestId, ct);
        if (existing is not null) return Replay(existing, eventId, quantity, now);
        if (!item.IsPublished || item.StartsAt <= now) return new(409, Error: "This event is not accepting new reservations.");
        var used = await db.Bookings.Where(b => b.EventId == eventId &&
            (b.State == BookingState.Confirmed || b.State == BookingState.Held && b.ExpiresAt > now))
            .SumAsync(b => (int?)b.Quantity, ct) ?? 0;
        if (used + quantity > item.Capacity) return new(409, Error: "There are not enough tickets left for that quantity.");
        var booking = new Booking
        {
            Id = Guid.NewGuid(), CustomerId = customerId, EventId = eventId, RequestId = requestId,
            Quantity = quantity, UnitPriceMinor = item.PriceMinor, TotalMinor = checked(item.PriceMinor * quantity),
            Currency = item.Currency, CreatedAt = now, ExpiresAt = now.AddMinutes(options.Value.HoldMinutes)
        };
        db.Bookings.Add(booking);
        try
        {
            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
        }
        catch (DbUpdateException exception) when (exception.InnerException is PostgresException
            { SqlState: "23505", ConstraintName: "IX_Bookings_CustomerId_RequestId" })
        {
            // Different events lock different rows; the unique request key still prevents a duplicate hold.
            await transaction.RollbackAsync(ct);
            db.ChangeTracker.Clear();
            var winner = await db.Bookings.AsNoTracking().SingleAsync(b => b.CustomerId == customerId && b.RequestId == requestId, ct);
            return Replay(winner, eventId, quantity, await Inventory.UtcNowAsync(db, ct));
        }
        return new(201, View(booking, now));
    }

    public async Task<ReservationResult> GetAsync(Guid customerId, Guid bookingId, CancellationToken ct)
    {
        var booking = await db.Bookings.AsNoTracking().SingleOrDefaultAsync(b => b.Id == bookingId, ct);
        if (booking is null) return new(404);
        if (booking.CustomerId != customerId) return new(403);
        return new(200, View(booking, await Inventory.UtcNowAsync(db, ct)));
    }

    public async Task<ReservationResult> CancelAsync(Guid customerId, Guid bookingId, CancellationToken ct)
    {
        var reference = await db.Bookings.AsNoTracking().SingleOrDefaultAsync(b => b.Id == bookingId, ct);
        if (reference is null) return new(404);
        if (reference.CustomerId != customerId) return new(403);
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        await Inventory.LockEventAsync(db, reference.EventId, ct);
        var booking = await db.Bookings.SingleAsync(b => b.Id == bookingId, ct);
        var now = await Inventory.UtcNowAsync(db, ct);
        if (booking.State is BookingState.Confirmed or BookingState.PaymentReview)
            return new(409, Error: "This booking can no longer be cancelled as a temporary hold.");
        if (booking.State == BookingState.Held)
            booking.State = booking.ExpiresAt <= now ? BookingState.Expired : BookingState.Cancelled;
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return new(200, View(booking, now));
    }

    public async Task ExpireAsync(Guid bookingId, CancellationToken ct)
    {
        var reference = await db.Bookings.AsNoTracking().SingleOrDefaultAsync(b => b.Id == bookingId, ct);
        if (reference is null) return;
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        await Inventory.LockEventAsync(db, reference.EventId, ct);
        var booking = await db.Bookings.SingleAsync(b => b.Id == bookingId, ct);
        if (booking.State == BookingState.Held && booking.ExpiresAt <= await Inventory.UtcNowAsync(db, ct))
        {
            booking.State = BookingState.Expired;
            await db.SaveChangesAsync(ct);
        }
        await transaction.CommitAsync(ct);
    }

    private static ReservationResult Replay(Booking existing, Guid eventId, int quantity, DateTimeOffset now) =>
        existing.EventId != eventId || existing.Quantity != quantity
            ? new(409, Error: "This request ID belongs to a different reservation. Start a new request.")
            : new(200, View(existing, now));

    private static BookingView View(Booking booking, DateTimeOffset now) => new(booking.Id, booking.EventId,
        booking.State == BookingState.Held && booking.ExpiresAt <= now ? "Expired" : booking.State.ToString(),
        booking.Quantity, booking.TotalMinor, booking.Currency, now, booking.ExpiresAt);
}
