using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using EventPlatform.Api.Events;

namespace EventPlatform.Api.Search;

public sealed record SearchContext(Guid UserId, string? City, string Currency, string Timezone, DateOnly LocalDate);
public sealed record ExtractionResult(string Status, EventFilter? Filter = null, DateOnly? From = null, DateOnly? Through = null);

public static class SearchFilterValidator
{
    internal static readonly string[] Categories = ["music", "workshop", "arts", "food", "community"];
    internal static readonly string[] Cities = ["Kuala Lumpur", "George Town"];
    internal static readonly JsonElement Schema = JsonSerializer.Deserialize<JsonElement>("""
        {
          "type": "object",
          "additionalProperties": false,
          "properties": {
            "relevant": {"type":"boolean"},
            "category": {"type":["string","null"]},
            "city": {"type":["string","null"]},
            "from": {"type":["string","null"]},
            "to": {"type":["string","null"]},
            "maxPriceMinor": {"type":["integer","null"]},
            "keywords": {"type":["string","null"]}
          },
          "required":["relevant","category","city","from","to","maxPriceMinor","keywords"]
        }
        """);
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        NumberHandling = JsonNumberHandling.Strict
    };

    internal static ExtractionResult Parse(JsonElement root, SearchContext context)
    {
        if (root.GetProperty("status").GetString() != "completed") return new("unavailable");
        string? outputText = null;
        foreach (var item in root.GetProperty("output").EnumerateArray())
        {
            if (item.GetProperty("type").GetString() != "message") continue;
            foreach (var content in item.GetProperty("content").EnumerateArray())
            {
                var type = content.GetProperty("type").GetString();
                if (type == "refusal") return new("unavailable");
                if (type != "output_text" || outputText is not null) return new("unavailable");
                outputText = content.GetProperty("text").GetString();
            }
        }
        if (outputText is null) return new("unavailable");
        var fields = JsonSerializer.Deserialize<Fields>(outputText, Json);
        if (fields is null || !fields.Relevant ||
            fields.Category is not null && !Categories.Contains(fields.Category) ||
            fields.City is not null && !Cities.Contains(fields.City) ||
            fields.MaxPriceMinor is < 0 or > 100_000_000 ||
            fields.Keywords is { } keywords && (keywords.Length > 80 || keywords.Any(char.IsControl)) ||
            !TryDate(fields.From, out var from) || !TryDate(fields.To, out var to) || from >= to)
            return new("unsupported");

        try
        {
            var filter = new EventFilter(fields.Category, fields.City ?? context.City, context.Currency, fields.MaxPriceMinor,
                from.HasValue ? DateWindow.StartOfDayUtc(from.Value, context.Timezone) : null,
                to.HasValue ? DateWindow.StartOfDayUtc(to.Value, context.Timezone) : null,
                Keywords: string.IsNullOrWhiteSpace(fields.Keywords) ? null : fields.Keywords.Trim());
            return new("ok", filter, from, to?.AddDays(-1));
        }
        catch (Exception ex) when (ex is ArgumentException or TimeZoneNotFoundException or InvalidTimeZoneException)
        {
            return new("unsupported");
        }
    }

    private static bool TryDate(string? text, out DateOnly? date)
    {
        date = null;
        if (text is null) return true;
        if (!DateOnly.TryParseExact(text, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed)
            || parsed.Year is < 2000 or > 2100) return false;
        date = parsed;
        return true;
    }

    private sealed record Fields(
        [property: JsonRequired] bool Relevant, [property: JsonRequired] string? Category,
        [property: JsonRequired] string? City, [property: JsonRequired] string? From,
        [property: JsonRequired] string? To, [property: JsonRequired] long? MaxPriceMinor,
        [property: JsonRequired] string? Keywords);
}
