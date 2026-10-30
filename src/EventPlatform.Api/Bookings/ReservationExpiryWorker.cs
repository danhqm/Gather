using EventPlatform.Api.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace EventPlatform.Api.Bookings;

public sealed class ReservationExpiryWorker(IServiceScopeFactory scopes, IOptions<ReservationOptions> options,
    ILogger<ReservationExpiryWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.Value.RunExpiryWorker) return;
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(30));
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                Guid[] ids;
                await using (var scope = scopes.CreateAsyncScope())
                {
                    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                    var now = await Inventory.UtcNowAsync(db, stoppingToken);
                    ids = await db.Bookings.Where(b => b.State == BookingState.Held && b.ExpiresAt <= now)
                        .OrderBy(b => b.ExpiresAt).Select(b => b.Id).Take(100).ToArrayAsync(stoppingToken);
                }
                foreach (var id in ids)
                {
                    await using var scope = scopes.CreateAsyncScope();
                    await scope.ServiceProvider.GetRequiredService<ReservationService>().ExpireAsync(id, stoppingToken);
                }
            }
            catch (Exception exception) when (!stoppingToken.IsCancellationRequested)
            { logger.LogError(exception, "Reservation expiry cleanup failed; expired holds remain excluded from inventory queries."); }
        }
    }
}
