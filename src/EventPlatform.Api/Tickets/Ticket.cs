namespace EventPlatform.Api.Tickets;

public sealed class Ticket
{
    public Guid Id { get; set; }
    public Guid BookingId { get; set; }
    public int Number { get; set; }
    public DateTimeOffset IssuedAt { get; set; }
}
