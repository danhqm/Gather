namespace EventPlatform.Api.Events;

public static class DateWindow
{
    public static (DateTimeOffset From, DateTimeOffset To) ToUtc(DateOnly fromInclusive,
        DateOnly toExclusive, string timezoneId)
    {
        if (fromInclusive >= toExclusive) throw new ArgumentException("The end date must follow the start date.");
        return (StartOfDayUtc(fromInclusive, timezoneId), StartOfDayUtc(toExclusive, timezoneId));
    }

    public static DateTimeOffset StartOfDayUtc(DateOnly day, string timezoneId)
    {
        if (!TimeZoneInfo.TryConvertIanaIdToWindowsId(timezoneId, out _))
            throw new ArgumentException("Use a valid IANA timezone.");
        var zone = TimeZoneInfo.FindSystemTimeZoneById(timezoneId);
        var midnight = day.ToDateTime(TimeOnly.MinValue, DateTimeKind.Unspecified);
        // Reject midnight transitions rather than silently selecting the wrong instant.
        if (zone.IsInvalidTime(midnight) || zone.IsAmbiguousTime(midnight))
            throw new ArgumentException("This timezone has an ambiguous or missing midnight on the selected dates.");
        return TimeZoneInfo.ConvertTimeToUtc(midnight, zone);
    }
}
