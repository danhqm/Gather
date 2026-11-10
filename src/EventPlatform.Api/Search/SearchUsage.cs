using EventPlatform.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace EventPlatform.Api.Search;

public sealed class SearchAttempt
{
    public Guid Id { get; init; }
    public Guid UserId { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
    public long CostMicroUsd { get; set; }
}

public interface ISearchUsage
{
    Task<Guid?> ReserveAsync(Guid userId, CancellationToken ct);
    Task ReconcileAsync(Guid id, long cost, CancellationToken ct);
}

public sealed class SearchUsage(AppDbContext db) : ISearchUsage
{
    private const long Reservation = 10_000;
    private const long Budget = 8_000_000;

    public async Task<Guid?> ReserveAsync(Guid userId, CancellationToken ct)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        // All reservations share one transaction lock, including requests from other app instances.
        await db.Database.ExecuteSqlRawAsync("SELECT pg_advisory_xact_lock(783217710451)", ct);
        var now = await Inventory.UtcNowAsync(db, ct);
        var dayStart = new DateTimeOffset(now.UtcDateTime.Date, TimeSpan.Zero);
        var today = db.SearchAttempts.Where(attempt => attempt.CreatedAt >= dayStart);
        if (await today.CountAsync(ct) >= 100 ||
            await today.CountAsync(attempt => attempt.UserId == userId, ct) >= 20 ||
            await db.SearchAttempts.SumAsync(attempt => attempt.CostMicroUsd, ct) > Budget - Reservation)
            return null;

        var id = Guid.NewGuid();
        db.SearchAttempts.Add(new() { Id = id, UserId = userId, CreatedAt = now, CostMicroUsd = Reservation });
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return id;
    }

    public async Task ReconcileAsync(Guid id, long cost, CancellationToken ct)
    {
        await db.SearchAttempts.Where(attempt => attempt.Id == id)
            .ExecuteUpdateAsync(update => update.SetProperty(attempt => attempt.CostMicroUsd, cost), ct);
    }
}
