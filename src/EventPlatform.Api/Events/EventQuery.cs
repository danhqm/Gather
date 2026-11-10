using EventPlatform.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace EventPlatform.Api.Events;

public sealed record EventFilter(string? Category, string? City, string? Currency,
    long? MaxPriceMinor, DateTimeOffset? From, DateTimeOffset? To, int Page = 1, int PageSize = 12, string? Keywords = null);
public sealed record EventSummary(Guid Id, string Title, string Category, string City,
    string Currency, long PriceMinor, DateTimeOffset StartsAt, string Timezone, string Venue, string? ImageUrl);
public sealed record EventPage(EventSummary[] Items, int Total, int Page, int PageSize);

public sealed class EventQuery(AppDbContext db)
{
    public async Task<EventPage> ListAsync(EventFilter filter, CancellationToken ct)
    {
        var query = db.Events.AsNoTracking().Where(e => e.IsPublished);
        if (filter.Category is not null) query = query.Where(e => e.Category == filter.Category);
        if (filter.City is not null) query = query.Where(e => e.City == filter.City);
        if (filter.Currency is not null) query = query.Where(e => e.Currency == filter.Currency);
        if (filter.MaxPriceMinor.HasValue) query = query.Where(e => e.PriceMinor <= filter.MaxPriceMinor.Value);
        if (filter.From.HasValue) query = query.Where(e => e.StartsAt >= filter.From.Value);
        if (filter.To.HasValue) query = query.Where(e => e.StartsAt < filter.To.Value);
        if (!string.IsNullOrWhiteSpace(filter.Keywords))
        {
            var term = filter.Keywords.Trim().ToLowerInvariant();
            query = query.Where(e => e.Title.ToLower().Contains(term) || e.Description.ToLower().Contains(term)
                || e.Venue.ToLower().Contains(term));
        }
        var total = await query.CountAsync(ct);
        var items = await query.OrderBy(e => e.StartsAt).ThenBy(e => e.Id)
            .Skip((filter.Page - 1) * filter.PageSize).Take(filter.PageSize)
            .Select(e => new EventSummary(e.Id, e.Title, e.Category, e.City, e.Currency,
                e.PriceMinor, e.StartsAt, e.Timezone, e.Venue, e.ImageUrl)).ToArrayAsync(ct);
        return new(items, total, filter.Page, filter.PageSize);
    }
}
