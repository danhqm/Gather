using EventPlatform.Api.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace EventPlatform.Api.Payments;

public sealed class RefundProcessor(AppDbContext db, IPaymentGateway gateway)
{
    public async Task<bool> ProcessOneAsync(CancellationToken ct)
    {
        RefundJob job;
        await using (var transaction = await db.Database.BeginTransactionAsync(ct))
        {
            var jobs = await db.RefundJobs.FromSqlRaw("""
                SELECT * FROM "RefundJobs"
                WHERE "State" IN ('Pending', 'Running') AND "NextAttemptAt" <= clock_timestamp()
                ORDER BY "NextAttemptAt" LIMIT 1 FOR UPDATE SKIP LOCKED
                """).ToListAsync(ct);
            if (jobs.Count == 0) return false;
            job = jobs[0];
            var now = await Inventory.UtcNowAsync(db, ct);
            // Stripe may prune idempotency keys after 24h. Never risk a fresh refund after an ambiguous day-old response.
            if (job.FirstAttemptAt < now.AddHours(-23))
            {
                job.State = "NeedsReview";
                await db.SaveChangesAsync(ct);
                await transaction.CommitAsync(ct);
                return true;
            }
            job.FirstAttemptAt ??= now;
            job.Attempts++;
            job.State = "Running";
            job.NextAttemptAt = now.AddMinutes(5);
            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
        }
        var attemptNumber = job.Attempts;
        try
        {
            var refund = await gateway.RefundAsync(job.PaymentId, job.IdempotencyKey, ct);
            var state = refund.Status == "succeeded" ? "Succeeded" : "NeedsReview";
            // A lease that elapsed while waiting must not overwrite a newer worker's result.
            await db.RefundJobs.Where(j => j.Id == job.Id && j.Attempts == attemptNumber && j.State == "Running")
                .ExecuteUpdateAsync(update => update.SetProperty(j => j.State, state)
                    .SetProperty(j => j.ProviderRefundId, refund.Id), ct);
        }
        catch (Exception error) when (error is HttpRequestException or Stripe.StripeException or OperationCanceledException)
        {
            if (ct.IsCancellationRequested) throw;
            await db.RefundJobs.Where(j => j.Id == job.Id && j.Attempts == attemptNumber && j.State == "Running")
                .ExecuteUpdateAsync(update => update.SetProperty(j => j.State, "Pending"), ct);
        }
        return true;
    }
}

public sealed class RefundWorker(IServiceScopeFactory scopes, IOptions<PaymentOptions> options,
    ILogger<RefundWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.Value.RunRefundWorker || !options.Value.Enabled) return;
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(30));
        do
        {
            try
            {
                for (var i = 0; i < 20; i++)
                {
                    await using var scope = scopes.CreateAsyncScope();
                    if (!await scope.ServiceProvider.GetRequiredService<RefundProcessor>().ProcessOneAsync(stoppingToken)) break;
                }
            }
            catch (Exception error) when (!stoppingToken.IsCancellationRequested)
            {
                logger.LogError("Refund processing failed ({ErrorType}); durable jobs will be retried.", error.GetType().Name);
            }
        } while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
