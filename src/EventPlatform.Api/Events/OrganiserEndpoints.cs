using System.Security.Claims;
using EventPlatform.Api.Auth;
using EventPlatform.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace EventPlatform.Api.Events;

public static class OrganiserEndpoints
{
    public static void MapOrganiserEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/organiser/events").RequireAuthorization().AddEndpointFilter<CsrfFilter>();
        group.MapGet("/", async (Guid organisationId, ClaimsPrincipal user, CurrentOrganisation permissions, AppDbContext db, CancellationToken ct) =>
        {
            if (!await permissions.RequireRoleAsync(UserId(user), organisationId, OrganisationRoles.Organiser, ct)) return Results.Forbid();
            // shortcut: newest 200 events only; add pagination when an organisation can exceed that size.
            return Results.Ok(await db.Events.AsNoTracking().Where(e => e.OrganisationId == organisationId)
                .OrderByDescending(e => e.StartsAt).ThenBy(e => e.Id).Take(200).ToArrayAsync(ct));
        });
        group.MapPost("/", async (CreateEventRequest command, ClaimsPrincipal user, EventManagement events, CancellationToken ct) =>
            (await events.CreateAsync(UserId(user), command.OrganisationId, command.Event, ct)).ToResult());
        group.MapPut("/{eventId:guid}", async (Guid eventId, UpdateEvent command, ClaimsPrincipal user, EventManagement events, CancellationToken ct) =>
            (await events.UpdateAsync(UserId(user), eventId, command, ct)).ToResult());
        group.MapPost("/{eventId:guid}/publish", async (Guid eventId, ClaimsPrincipal user, EventManagement events, CancellationToken ct) =>
            (await events.SetPublicationAsync(UserId(user), eventId, true, ct)).ToResult());
        group.MapPost("/{eventId:guid}/unpublish", async (Guid eventId, ClaimsPrincipal user, EventManagement events, CancellationToken ct) =>
            (await events.SetPublicationAsync(UserId(user), eventId, false, ct)).ToResult());
    }

    private static Guid UserId(ClaimsPrincipal user) => Guid.Parse(user.FindFirstValue(ClaimTypes.NameIdentifier)!);
    public sealed record CreateEventRequest(Guid OrganisationId, UpdateEvent? Event);
}
