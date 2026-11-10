using System.Net;
using System.Text.Json;
using EventPlatform.Api.Search;
using Microsoft.Extensions.Options;

namespace EventPlatform.Tests;

public sealed class AiSearchTests
{
    private const string Valid = """{"relevant":true,"category":"workshop","city":null,"from":"2026-11-07","to":"2026-11-09","maxPriceMinor":0,"keywords":null}""";
    private static readonly SearchContext Context = new(Guid.NewGuid(), "Kuala Lumpur", "MYR", "Asia/Kuala_Lumpur", new(2026, 11, 6));

    [Fact]
    public async Task Uses_strict_schema_and_caches_validated_local_dates()
    {
        var provider = new Provider(_ => Reply(Valid));
        var usage = new Usage();
        using var cache = new SearchCache();
        var search = Make(provider, usage, cache);
        var result = await search.ExtractAsync("Free workshops this weekend", Context, default);
        Assert.Equal("workshop", result.Filter!.Category);
        Assert.Equal("Kuala Lumpur", result.Filter.City);
        Assert.Equal(0L, result.Filter.MaxPriceMinor);
        Assert.Equal(DateTimeOffset.Parse("2026-11-06T16:00:00Z"), result.Filter.From);
        Assert.Equal(new DateOnly(2026, 11, 8), result.Through);
        await search.ExtractAsync("  Free workshops this weekend ", Context, default);
        Assert.Equal(1, provider.Calls);
        Assert.Equal(20, usage.Charged);
        using var request = JsonDocument.Parse(provider.Body!);
        Assert.Equal("gpt-6-luna", request.RootElement.GetProperty("model").GetString());
        Assert.False(request.RootElement.GetProperty("store").GetBoolean());
        Assert.True(request.RootElement.GetProperty("text").GetProperty("format").GetProperty("strict").GetBoolean());
    }

    [Theory]
    [InlineData("refusal")]
    [InlineData("incomplete")]
    [InlineData("category")]
    [InlineData("negative")]
    [InlineData("reversed")]
    [InlineData("unrelated")]
    public async Task Unusable_output_preserves_manual_search(string failure)
    {
        var data = failure switch
        {
            "category" => Valid.Replace("workshop", "invalid"),
            "negative" => Valid.Replace(":0", ":-1"),
            "reversed" => Valid.Replace("11-09", "11-06"),
            "unrelated" => Valid.Replace("true", "false"),
            _ => Valid
        };
        var provider = new Provider(_ => Reply(data, failure == "incomplete" ? "incomplete" : "completed",
            failure == "refusal" ? "refusal" : "output_text"));
        using var cache = new SearchCache();
        var result = await Make(provider, new Usage(), cache).ExtractAsync("test", Context, default);
        Assert.Null(result.Filter);
        Assert.Equal(1, provider.Calls);
    }

    [Theory]
    [InlineData(429)]
    [InlineData(503)]
    public async Task Transient_failure_uses_at_most_two_reserved_attempts(int status)
    {
        var provider = new Provider(_ => new((HttpStatusCode)status));
        var usage = new Usage();
        using var cache = new SearchCache();
        Assert.Null((await Make(provider, usage, cache).ExtractAsync("test", Context, default)).Filter);
        Assert.Equal(2, usage.Attempts);
        Assert.Equal(2, provider.Calls);
        Assert.Null(usage.Charged);
    }

    [Fact]
    public async Task Missing_key_or_exhausted_allowance_prevents_provider_calls()
    {
        var provider = new Provider(_ => Reply(Valid));
        using var cache = new SearchCache();
        Assert.Null((await Make(provider, new Usage(), cache, "").ExtractAsync("test", Context, default)).Filter);
        Assert.Null((await Make(provider, new Usage { Deny = true }, cache).ExtractAsync("test", Context, default)).Filter);
        Assert.Equal(0, provider.Calls);
    }

    [Fact]
    public async Task Daylight_saving_boundary_uses_each_local_midnight()
    {
        var payload = Valid.Replace("2026-11-07", "2026-03-08").Replace("2026-11-09", "2026-03-09");
        var provider = new Provider(_ => Reply(payload));
        using var cache = new SearchCache();
        var result = await Make(provider, new Usage(), cache).ExtractAsync("Workshops tomorrow",
            Context with { Timezone = "America/New_York", LocalDate = new(2026, 3, 7) }, default);
        Assert.Equal(TimeSpan.FromHours(23), result.Filter!.To - result.Filter.From);
    }

    [Fact]
    public async Task Timeout_retries_once_and_keeps_unknown_charges_reserved()
    {
        var provider = new Provider(_ => throw new TaskCanceledException());
        var usage = new Usage();
        using var cache = new SearchCache();
        Assert.Null((await Make(provider, usage, cache).ExtractAsync("test", Context, default)).Filter);
        Assert.Equal(2, provider.Calls);
        Assert.Null(usage.Charged);
    }

    private static OpenAiEventSearch Make(Provider provider, Usage usage, SearchCache cache, string key = "offline-test-key")
        => new(provider, Options.Create(new SearchOptions { ApiKey = key, Enabled = true }), usage, cache);

    private static HttpResponseMessage Reply(string data, string status = "completed", string type = "output_text") =>
        new(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(new
        {
            status, output = new[] { new { type = "message", content = new[] { new { type, text = data } } } },
            usage = new { input_tokens = 100, output_tokens = 20 }
        })) };

    private sealed class Provider(Func<int, HttpResponseMessage> reply) : HttpMessageHandler, IHttpClientFactory
    {
        public int Calls { get; private set; }
        public string? Body { get; private set; }
        public HttpClient CreateClient(string name) => new(this, disposeHandler: false);
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Body = await request.Content!.ReadAsStringAsync(ct);
            return reply(++Calls);
        }
    }

    private sealed class Usage : ISearchUsage
    {
        public bool Deny { get; init; }
        public int Attempts { get; private set; }
        public long? Charged { get; private set; }
        public Task<Guid?> ReserveAsync(Guid userId, CancellationToken ct)
        {
            Attempts++;
            return Task.FromResult<Guid?>(Deny ? null : Guid.NewGuid());
        }
        public Task ReconcileAsync(Guid id, long cost, CancellationToken ct)
        {
            Charged = cost;
            return Task.CompletedTask;
        }
    }
}
