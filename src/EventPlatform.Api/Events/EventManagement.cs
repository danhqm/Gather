using EventPlatform.Api.Auth;
using EventPlatform.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace EventPlatform.Api.Events;

public sealed record UpdateEvent(string? Title, string? Description, string? Category, string? City,
    string? Venue, string? Timezone, DateTimeOffset StartsAt, DateTimeOffset EndsAt,
    string? Currency, long PriceMinor, int Capacity, string? ImageUrl);
public sealed record EventUpdateResult(int Status, Event? Value = null, string? Error = null)
{
    public IResult ToResult() => Status switch
    {
        201 => Results.Created($"/api/organiser/events/{Value!.Id}", Value),
        200 => Results.Ok(Value),
        403 => Results.Forbid(),
        404 => Results.NotFound(),
        _ => Results.Problem(Error, statusCode: Status)
    };
}

public sealed class EventManagement(AppDbContext db, CurrentOrganisation permissions)
{
    public async Task<EventUpdateResult> CreateAsync(Guid userId, Guid organisationId, UpdateEvent? command, CancellationToken ct)
    {
        if (!await permissions.RequireRoleAsync(userId, organisationId, OrganisationRoles.Organiser, ct)) return new(403);
        var error = Validate(command);
        if (error is not null) return new(400, Error: error);
        var item = new Event { Id = Guid.NewGuid(), OrganisationId = organisationId };
        Apply(item, command!);
        db.Events.Add(item);
        await db.SaveChangesAsync(ct);
        return new(201, item);
    }

    public async Task<EventUpdateResult> UpdateAsync(Guid userId, Guid eventId, UpdateEvent? command, CancellationToken ct)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var item = await Inventory.LockEventAsync(db, eventId, ct);
        if (item is null) return new(404);
        if (!await permissions.RequireRoleAsync(userId, item.OrganisationId, OrganisationRoles.Organiser, ct)) return new(403);
        var error = Validate(command);
        if (error is not null) return new(400, Error: error);
        if (await db.Bookings.AnyAsync(b => b.EventId == eventId, ct) &&
            (item.PriceMinor != command!.PriceMinor || item.Currency != command.Currency || item.Capacity != command.Capacity || item.StartsAt != command.StartsAt))
            return new(409, Error: "Price, capacity and start time cannot change after a booking exists.");
        if (item.IsPublished && command!.StartsAt <= await Inventory.UtcNowAsync(db, ct))
            return new(400, Error: "Published events must start in the future.");
        Apply(item, command!);
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return new(200, item);
    }

    public async Task<EventUpdateResult> SetPublicationAsync(Guid userId, Guid eventId, bool publish, CancellationToken ct)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var item = await Inventory.LockEventAsync(db, eventId, ct);
        if (item is null) return new(404);
        if (!await permissions.RequireRoleAsync(userId, item.OrganisationId, OrganisationRoles.Organiser, ct)) return new(403);
        if (publish && item.StartsAt <= await Inventory.UtcNowAsync(db, ct)) return new(400, Error: "Only future events can be published.");
        item.IsPublished = publish;
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return new(200, item);
    }

    private static string? Validate(UpdateEvent? value)
    {
        if (value is null) return "Enter the event details.";
        if (string.IsNullOrWhiteSpace(value.Title) || value.Title.Trim().Length > 160
            || string.IsNullOrWhiteSpace(value.Description) || value.Description.Length > 4000
            || string.IsNullOrWhiteSpace(value.Venue) || value.Venue.Trim().Length > 200)
            return "Enter a title (up to 160 characters), description (up to 4,000) and venue (up to 200).";
        if (value.Category is not ("workshop" or "music" or "community" or "food" or "arts")) return "Select a supported category.";
        if (value.City is not ("Kuala Lumpur" or "George Town")) return "This demo supports Kuala Lumpur and George Town.";
        if (value.Timezone != "Asia/Kuala_Lumpur" || !TimeZoneInfo.TryConvertIanaIdToWindowsId(value.Timezone, out _))
            return "Use the event city's timezone: Asia/Kuala_Lumpur.";
        if (value.Currency != "MYR") return "This demo uses MYR prices.";
        if (value.PriceMinor is < 0 or > 100_000_000 || value.Capacity is < 1 or > 1_000_000)
            return "Enter a price between RM 0 and RM 1,000,000 and a capacity between 1 and 1,000,000.";
        if (value.EndsAt <= value.StartsAt) return "The end must be after the start.";
        if (!string.IsNullOrWhiteSpace(value.ImageUrl) && (value.ImageUrl.Length > 2048
            || !Uri.TryCreate(value.ImageUrl, UriKind.Absolute, out var uri) || uri.Scheme != "https" || !string.IsNullOrEmpty(uri.UserInfo)))
            return "Use an HTTPS image URL without embedded credentials, or leave it empty.";
        return null;
    }

    private static void Apply(Event item, UpdateEvent value)
    {
        item.Title = value.Title!.Trim(); item.Description = value.Description!.Trim();
        item.Category = value.Category!; item.City = value.City!; item.Venue = value.Venue!.Trim();
        item.Timezone = value.Timezone!; item.StartsAt = value.StartsAt.ToUniversalTime(); item.EndsAt = value.EndsAt.ToUniversalTime();
        item.Currency = value.Currency!; item.PriceMinor = value.PriceMinor; item.Capacity = value.Capacity;
        item.ImageUrl = string.IsNullOrWhiteSpace(value.ImageUrl) ? null : value.ImageUrl.Trim();
    }
}
