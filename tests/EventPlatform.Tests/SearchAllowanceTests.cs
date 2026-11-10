using System.Net;
using System.Net.Http.Json;
using EventPlatform.Api.Data;
using EventPlatform.Api.Search;
using Microsoft.Extensions.DependencyInjection;

namespace EventPlatform.Tests;

public sealed class SearchAllowanceTests(WebAppFixture fixture) : IClassFixture<WebAppFixture>
{
    [Fact]
    public async Task Search_requires_authentication_and_csrf()
    {
        var body = new { query = "Free workshops", city = "Kuala Lumpur" };
        using var anonymous = await WebAppFixture.SendAsync(fixture.Client, "/api/search/interpret", body);
        Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);
        using var client = await fixture.CustomerClientAsync();
        using var missingCsrf = await client.PostAsJsonAsync("/api/search/interpret", body);
        Assert.Equal(HttpStatusCode.BadRequest, missingCsrf.StatusCode);
    }

    [Fact]
    public async Task Concurrent_reservations_cannot_cross_user_limit_or_spending_cap()
    {
        var user = Guid.NewGuid();
        await using var seedScope = fixture.Services.CreateAsyncScope();
        var db = seedScope.ServiceProvider.GetRequiredService<AppDbContext>();
        var now = await Inventory.UtcNowAsync(db, default);
        var seed = new SearchAttempt { Id = Guid.NewGuid(), UserId = user, CreatedAt = now.AddDays(-1), CostMicroUsd = 7_800_000 };
        db.SearchAttempts.Add(seed);
        await db.SaveChangesAsync();

        async Task<Guid?> Reserve(Guid userId)
        {
            await using var scope = fixture.Services.CreateAsyncScope();
            return await scope.ServiceProvider.GetRequiredService<ISearchUsage>().ReserveAsync(userId, default);
        }

        var results = await Task.WhenAll(Enumerable.Range(0, 22).Select(_ => Reserve(user)));
        Assert.Equal(20, results.Count(id => id.HasValue));
        Assert.Null(await Reserve(Guid.NewGuid()));
        await seedScope.ServiceProvider.GetRequiredService<ISearchUsage>().ReconcileAsync(seed.Id, 7_780_000, default);
        Assert.Null(await Reserve(user));
        Assert.NotNull(await Reserve(Guid.NewGuid()));
    }
}
