using System.Security.Claims;
using EventPlatform.Api.Auth;
using EventPlatform.Api.Bookings;
using EventPlatform.Api.Data;
using Microsoft.EntityFrameworkCore;
using QRCoder;

namespace EventPlatform.Api.Tickets;

public static class TicketEndpoints
{
    public static void MapTicketEndpoints(this WebApplication app)
    {
        var tickets = app.MapGroup("/api/me/tickets").RequireAuthorization();
        tickets.MapGet("/", async (ClaimsPrincipal user, HttpResponse response, AppDbContext db, CancellationToken ct) =>
        {
            response.Headers.CacheControl = "no-store";
            var userId = UserId(user);
            return Results.Ok(await (from t in db.Tickets.AsNoTracking() join b in db.Bookings on t.BookingId equals b.Id
                join e in db.Events on b.EventId equals e.Id where b.CustomerId == userId && b.State == BookingState.Confirmed
                orderby e.StartsAt, t.Number select new {
                    t.Id, t.BookingId, t.Number, t.AdmittedAt, eventId = e.Id, e.Title, e.Venue, e.City, e.StartsAt, e.Timezone
                }).Take(200).ToArrayAsync(ct));
        });
        tickets.MapGet("/{ticketId:guid}/qr", async (Guid ticketId, ClaimsPrincipal user, HttpResponse response,
            AppDbContext db, TicketCode codes, CancellationToken ct) =>
        {
            response.Headers.CacheControl = "no-store";
            var ticket = await (from t in db.Tickets.AsNoTracking() join b in db.Bookings on t.BookingId equals b.Id
                where t.Id == ticketId select new { b.CustomerId, b.State }).SingleOrDefaultAsync(ct);
            if (ticket is null) return Results.NotFound();
            if (ticket.CustomerId != UserId(user)) return Results.Forbid();
            if (ticket.State != BookingState.Confirmed) return Results.Conflict();
            var code = codes.Sign(ticketId);
            using var qr = QRCodeGenerator.GenerateQrCode(code, QRCodeGenerator.ECCLevel.M);
            using var png = new PngByteQRCode(qr);
            return Results.Ok(new { code, image = "data:image/png;base64," + Convert.ToBase64String(png.GetGraphic(6)) });
        });
        app.MapGet("/api/check-in/events", async (ClaimsPrincipal user, AppDbContext db, CancellationToken ct) =>
        {
            var userId = UserId(user);
            return Results.Ok(await db.Events.AsNoTracking().Where(e => db.OrganisationMemberships.Any(m =>
                m.OrganisationId == e.OrganisationId && m.UserId == userId &&
                (m.Role == OrganisationRoles.CheckInStaff || m.Role == OrganisationRoles.Organiser)))
                .OrderByDescending(e => e.StartsAt).ThenBy(e => e.Id).Take(200)
                .Select(e => new { e.Id, e.Title, e.StartsAt, e.Timezone }).ToArrayAsync(ct));
        }).RequireAuthorization();
        app.MapPost("/api/organiser/events/{eventId:guid}/check-ins", async (Guid eventId, ScanRequest request,
            ClaimsPrincipal user, CheckInService checkIn, CancellationToken ct) =>
            (await checkIn.AdmitAsync(UserId(user), eventId, request.Code, ct)).ToResult())
            .RequireAuthorization().AddEndpointFilter<CsrfFilter>();
    }
    private static Guid UserId(ClaimsPrincipal user) => Guid.Parse(user.FindFirstValue(ClaimTypes.NameIdentifier)!);
    public sealed record ScanRequest(string? Code);
}
