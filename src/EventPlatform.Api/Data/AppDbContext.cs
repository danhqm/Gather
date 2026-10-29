using EventPlatform.Api.Events;
using Microsoft.EntityFrameworkCore;

namespace EventPlatform.Api.Data;

public sealed class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<Event> Events => Set<Event>();

    protected override void OnModelCreating(ModelBuilder model)
    {
        var events = model.Entity<Event>();
        events.Property(e => e.Title).HasMaxLength(160);
        events.Property(e => e.Description).HasMaxLength(4000);
        events.Property(e => e.Category).HasMaxLength(40);
        events.Property(e => e.City).HasMaxLength(100);
        events.Property(e => e.Venue).HasMaxLength(200);
        events.Property(e => e.Timezone).HasMaxLength(100);
        events.Property(e => e.Currency).HasMaxLength(3);
        events.HasIndex(e => new { e.IsPublished, e.StartsAt, e.Id });
        events.ToTable(t =>
        {
            t.HasCheckConstraint("CK_Event_Capacity", "\"Capacity\" > 0");
            t.HasCheckConstraint("CK_Event_Price", "\"PriceMinor\" >= 0");
            t.HasCheckConstraint("CK_Event_Dates", "\"EndsAt\" > \"StartsAt\"");
        });
    }
}
