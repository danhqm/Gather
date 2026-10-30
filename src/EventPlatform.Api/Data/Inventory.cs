using EventPlatform.Api.Events;
using Microsoft.EntityFrameworkCore;

namespace EventPlatform.Api.Data;

public static class Inventory
{
    public static Task<Event?> LockEventAsync(AppDbContext db, Guid eventId, CancellationToken ct)
    {
        if (db.Database.CurrentTransaction is null) throw new InvalidOperationException("Inventory locks require an open transaction.");
        return db.Events.FromSqlInterpolated($"SELECT * FROM \"Events\" WHERE \"Id\" = {eventId} FOR UPDATE").SingleOrDefaultAsync(ct);
    }

    // clock_timestamp advances while waiting for a lock; transaction-scoped now() does not.
    public static Task<DateTimeOffset> UtcNowAsync(AppDbContext db, CancellationToken ct) =>
        db.Database.SqlQuery<DateTimeOffset>($"SELECT clock_timestamp() AS \"Value\"").SingleAsync(ct);
}
