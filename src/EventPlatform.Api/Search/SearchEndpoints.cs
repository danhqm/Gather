using System.Security.Claims;
using EventPlatform.Api.Auth;

namespace EventPlatform.Api.Search;

public static class SearchEndpoints
{
    private const string Timezone = "Asia/Kuala_Lumpur";

    public static void MapSearchEndpoints(this WebApplication app)
    {
        app.MapPost("/api/search/interpret", async (SearchRequest request, ClaimsPrincipal user,
            OpenAiEventSearch search, CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(request.Query) || request.Query.Length > 512 ||
                request.City is not null && !SearchFilterValidator.Cities.Contains(request.City))
                return Results.Problem("Enter a search of 1 to 512 characters and select a supported city.", statusCode: 400);

            var userId = Guid.Parse(user.FindFirstValue(ClaimTypes.NameIdentifier)!);
            var localTime = TimeZoneInfo.ConvertTimeBySystemTimeZoneId(DateTimeOffset.UtcNow, Timezone);
            var context = new SearchContext(userId, request.City, "MYR", Timezone, DateOnly.FromDateTime(localTime.DateTime));
            return Results.Ok(await search.ExtractAsync(request.Query, context, ct));
        }).RequireAuthorization().AddEndpointFilter<CsrfFilter>();
    }

    public sealed record SearchRequest(string? Query, string? City);
}
