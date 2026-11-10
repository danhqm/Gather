using EventPlatform.Api.Auth;
using EventPlatform.Api.Bookings;
using EventPlatform.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace EventPlatform.Api.Tickets;

public sealed record AdmissionResult(int Status, string Outcome, DateTimeOffset? AdmittedAt = null)
{
    public IResult ToResult() => Status == 403 ? Results.Forbid() : Results.Json(new { outcome = Outcome, admittedAt = AdmittedAt }, statusCode: Status);
}

public sealed class CheckInService(AppDbContext db, CurrentOrganisation permissions, TicketCode codes)
{
    public async Task<AdmissionResult> AdmitAsync(Guid staffUserId, Guid eventId, string? code, CancellationToken ct)
    {
        var item = await db.Events.AsNoTracking().SingleOrDefaultAsync(e => e.Id == eventId, ct);
        if (item is null) return new(404, "invalid");
        if (!await permissions.RequireRoleAsync(staffUserId, item.OrganisationId, OrganisationRoles.CheckInStaff, ct))
            return new(403, "forbidden");
        var ticketId = codes.Verify(code);
        if (ticketId is null) return new(400, "invalid");
        var ticket = await (from t in db.Tickets.AsNoTracking() join b in db.Bookings on t.BookingId equals b.Id
            where t.Id == ticketId && b.State == BookingState.Confirmed select new { t.Id, b.EventId }).SingleOrDefaultAsync(ct);
        if (ticket is null) return new(400, "invalid");
        if (ticket.EventId != eventId) return new(400, "wrong-event");
        var changed = await db.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE "Tickets" SET "AdmittedAt" = clock_timestamp(), "AdmittedBy" = {staffUserId}
            WHERE "Id" = {ticket.Id} AND "AdmittedAt" IS NULL
              AND EXISTS (SELECT 1 FROM "Bookings" WHERE "Bookings"."Id" = "Tickets"."BookingId" AND "State" = 'Confirmed')
            """, ct);
        var admittedAt = await db.Tickets.Where(t => t.Id == ticket.Id).Select(t => t.AdmittedAt).SingleAsync(ct);
        return changed == 1 ? new(200, "admitted", admittedAt) : new(409, "already-used", admittedAt);
    }
}
