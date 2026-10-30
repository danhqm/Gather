using System.Security.Claims;
using EventPlatform.Api.Auth;

namespace EventPlatform.Api.Bookings;

public static class BookingEndpoints
{
    public static void MapBookingEndpoints(this WebApplication app)
    {
        app.MapPost("/api/events/{eventId:guid}/reservations", async (Guid eventId, ReserveRequest request,
            ClaimsPrincipal user, ReservationService reservations, CancellationToken ct) =>
            (await reservations.ReserveAsync(UserId(user), eventId, request.Quantity, request.RequestId, ct)).ToResult())
            .RequireAuthorization().AddEndpointFilter<CsrfFilter>();
        var bookings = app.MapGroup("/api/bookings").RequireAuthorization().AddEndpointFilter<CsrfFilter>();
        bookings.MapGet("/{bookingId:guid}", async (Guid bookingId, ClaimsPrincipal user, ReservationService reservations, CancellationToken ct) =>
            (await reservations.GetAsync(UserId(user), bookingId, ct)).ToResult());
        bookings.MapPost("/{bookingId:guid}/cancel", async (Guid bookingId, ClaimsPrincipal user, ReservationService reservations, CancellationToken ct) =>
            (await reservations.CancelAsync(UserId(user), bookingId, ct)).ToResult());
    }

    private static Guid UserId(ClaimsPrincipal user) => Guid.Parse(user.FindFirstValue(ClaimTypes.NameIdentifier)!);
    public sealed record ReserveRequest(int Quantity, Guid RequestId);
}
