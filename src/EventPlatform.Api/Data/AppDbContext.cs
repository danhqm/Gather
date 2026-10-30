using EventPlatform.Api.Events;
using EventPlatform.Api.Auth;
using EventPlatform.Api.Bookings;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace EventPlatform.Api.Data;

public sealed class AppDbContext(DbContextOptions<AppDbContext> options) : IdentityDbContext<ApplicationUser, IdentityRole<Guid>, Guid>(options)
{
    public DbSet<Event> Events => Set<Event>();
    public DbSet<Booking> Bookings => Set<Booking>();
    public DbSet<Organisation> Organisations => Set<Organisation>();
    public DbSet<OrganisationMembership> OrganisationMemberships => Set<OrganisationMembership>();

    protected override void OnModelCreating(ModelBuilder model)
    {
        base.OnModelCreating(model);
        var booking = model.Entity<Booking>();
        booking.HasOne<Event>().WithMany().HasForeignKey(b => b.EventId).OnDelete(DeleteBehavior.Restrict);
        booking.HasOne<ApplicationUser>().WithMany().HasForeignKey(b => b.CustomerId).OnDelete(DeleteBehavior.Restrict);
        booking.HasIndex(b => new { b.CustomerId, b.RequestId }).IsUnique();
        booking.HasIndex(b => new { b.EventId, b.State, b.ExpiresAt });
        booking.Property(b => b.Currency).HasMaxLength(3);
        booking.Property(b => b.State).HasConversion<string>().HasMaxLength(24);
        booking.ToTable(t =>
        {
            t.HasCheckConstraint("CK_Booking_Quantity", "\"Quantity\" BETWEEN 1 AND 4");
            t.HasCheckConstraint("CK_Booking_Total", "\"UnitPriceMinor\" >= 0 AND \"TotalMinor\" = \"UnitPriceMinor\" * \"Quantity\"");
            t.HasCheckConstraint("CK_Booking_Expiry", "\"ExpiresAt\" > \"CreatedAt\"");
        });
        model.Entity<ApplicationUser>().Property(u => u.DisplayName).HasMaxLength(80);
        model.Entity<Organisation>().Property(o => o.Name).HasMaxLength(120);
        var membership = model.Entity<OrganisationMembership>();
        membership.HasKey(m => new { m.UserId, m.OrganisationId });
        membership.HasOne<ApplicationUser>().WithMany().HasForeignKey(m => m.UserId);
        membership.HasOne(m => m.Organisation).WithMany().HasForeignKey(m => m.OrganisationId);
        membership.Property(m => m.Role).HasMaxLength(30);
        membership.ToTable(t => t.HasCheckConstraint("CK_Membership_Role", "\"Role\" IN ('Organiser', 'CheckInStaff')"));
        var events = model.Entity<Event>();
        events.HasOne<Organisation>().WithMany().HasForeignKey(e => e.OrganisationId).OnDelete(DeleteBehavior.Restrict);
        events.Property(e => e.ImageUrl).HasMaxLength(2048);
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
