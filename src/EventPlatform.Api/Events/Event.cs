namespace EventPlatform.Api.Events;

public sealed class Event
{
    public Guid Id { get; set; }
    public Guid OrganisationId { get; set; }
    public string Title { get; set; } = "";
    public string Description { get; set; } = "";
    public string Category { get; set; } = "";
    public string City { get; set; } = "";
    public string Venue { get; set; } = "";
    public string Timezone { get; set; } = "";
    public DateTimeOffset StartsAt { get; set; }
    public DateTimeOffset EndsAt { get; set; }
    public string Currency { get; set; } = "MYR";
    public long PriceMinor { get; set; }
    public int Capacity { get; set; }
    public bool IsPublished { get; set; }
    public string? ImageUrl { get; set; }
}
