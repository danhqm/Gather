using EventPlatform.Api.Events;
using Microsoft.EntityFrameworkCore;

namespace EventPlatform.Api.Data;

public static class DemoSeeder
{
    public static async Task SeedAsync(AppDbContext db)
    {
        if (await db.Events.AnyAsync()) return;
        // Fictional listings for the portfolio demo, not events accepting real bookings.
        db.Events.AddRange(
            Create(1, "Make something tangible", "workshop", "Kuala Lumpur", "The Clay Room", 7, 2500,
                "Trade your screen for a little clay. A relaxed, hands-on pottery session for first-timers. Materials and firing included."),
            Create(2, "After hours, on vinyl", "music", "Kuala Lumpur", "Studio 03", 13, 3500,
                "An intimate listening night with local selectors, deep cuts and good company. Come for a record. Stay for the whole set."),
            Create(3, "Ideas worth building", "community", "Kuala Lumpur", "Common Ground", 14, 0,
                "Meet the people making things in your city. Short demos, honest stories and time to connect. All backgrounds welcome."),
            Create(4, "A seat at the long table", "food", "George Town", "The Courtyard", 21, 8500,
                "A shared-table supper celebrating seasonal ingredients and the stories behind them. Vegetarian options available."),
            Create(5, "Light, through a new lens", "workshop", "George Town", "Northlight Studio", 22, 6500,
                "A guided photo walk and editing session. Learn to notice light, frame everyday scenes and tell a story with your camera."),
            Create(6, "Small press, big ideas", "arts", "Kuala Lumpur", "Paperhouse", 28, 1000,
                "An afternoon of independent zines, prints and conversations with local artists. Browse, swap and find your next favourite."),
            Create(99, "Unpublished planning session", "workshop", "Kuala Lumpur", "Private", 29, 0, "Draft", false));
        await db.SaveChangesAsync();
    }

    private static Event Create(int id, string title, string category, string city, string venue,
        int day, long price, string description, bool published = true) => new()
    {
        Id = Guid.Parse($"00000000-0000-0000-0000-{id:D12}"), Title = title,
        Category = category, City = city, Venue = venue, Description = description,
        Timezone = "Asia/Kuala_Lumpur", StartsAt = new DateTimeOffset(2026, 11, day, 10, 0, 0, TimeSpan.Zero),
        EndsAt = new DateTimeOffset(2026, 11, day, 13, 0, 0, TimeSpan.Zero),
        PriceMinor = price, Currency = "MYR", Capacity = 60, IsPublished = published
    };
}
