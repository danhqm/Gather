using EventPlatform.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace EventPlatform.Api.Events;

public static class EventEndpoints
{
    public static void MapEventEndpoints(this WebApplication app)
    {
        app.MapGet("/api/events", async (string? category, string? city, string? currency,
            long? maxPriceMinor, DateTimeOffset? from, DateTimeOffset? to, int? page, int? pageSize, string? keywords,
            EventQuery query, CancellationToken ct) =>
        {
            if (page is < 1 or > 10000 || pageSize is < 1 or > 100 || maxPriceMinor is < 0
                || category?.Length > 40 || city?.Length > 100 || currency is not null && currency.Length != 3
                || maxPriceMinor.HasValue && currency is null || from.HasValue && to.HasValue && from >= to
                || keywords?.Length > 80 || keywords is not null && keywords.Any(char.IsControl))
                return Results.Problem("Invalid filters: check price, currency, date range and pagination.", statusCode: 400);
            return Results.Ok(await query.ListAsync(new(category, city, currency?.ToUpperInvariant(),
                maxPriceMinor, from?.ToUniversalTime(), to?.ToUniversalTime(), page ?? 1, pageSize ?? 12, keywords), ct));
        });
        app.MapGet("/api/events/{eventId:guid}", async (Guid eventId, AppDbContext db, CancellationToken ct) =>
        {
            var item = await db.Events.AsNoTracking().Where(e => e.IsPublished && e.Id == eventId)
                .Select(e => new { e.Id, e.Title, e.Description, e.Category, e.City, e.Venue,
                    e.StartsAt, e.EndsAt, e.Timezone, e.Currency, e.PriceMinor, e.Capacity, e.ImageUrl }).SingleOrDefaultAsync(ct);
            return item is null ? Results.NotFound() : Results.Ok(item);
        });
        app.MapGet("/api/date-window", (DateOnly? from, DateOnly? to, string timezone) =>
        {
            try
            {
                if (from is null && to is null || from.HasValue && to.HasValue && from >= to)
                    throw new ArgumentException("Select a valid date range.");
                DateTimeOffset? fromUtc = from.HasValue ? DateWindow.StartOfDayUtc(from.Value, timezone) : null;
                DateTimeOffset? toUtc = to.HasValue ? DateWindow.StartOfDayUtc(to.Value, timezone) : null;
                return Results.Ok(new { From = fromUtc, To = toUtc });
            }
            catch (Exception ex) when (ex is ArgumentException or TimeZoneNotFoundException or InvalidTimeZoneException)
            {
                return Results.Problem("Select valid dates and an IANA timezone.", statusCode: 400);
            }
        });
    }
}
