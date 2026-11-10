using EventPlatform.Api.Events;
using EventPlatform.Api.Auth;
using EventPlatform.Api.Bookings;
using EventPlatform.Api.Payments;
using EventPlatform.Api.Tickets;
using EventPlatform.Api.Search;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace EventPlatform.Api.Data;

public sealed class AppDbContext(DbContextOptions<AppDbContext> options) : IdentityDbContext<ApplicationUser, IdentityRole<Guid>, Guid>(options)
{
    public DbSet<Event> Events => Set<Event>();
    public DbSet<Booking> Bookings => Set<Booking>();
    public DbSet<PaymentAttempt> PaymentAttempts => Set<PaymentAttempt>();
    public DbSet<ProcessedWebhook> ProcessedWebhooks => Set<ProcessedWebhook>();
    public DbSet<RefundJob> RefundJobs => Set<RefundJob>();
    public DbSet<Ticket> Tickets => Set<Ticket>();
    public DbSet<SearchAttempt> SearchAttempts => Set<SearchAttempt>();
    public DbSet<Organisation> Organisations => Set<Organisation>();
    public DbSet<OrganisationMembership> OrganisationMemberships => Set<OrganisationMembership>();

    protected override void OnModelCreating(ModelBuilder model)
    {
        base.OnModelCreating(model);
        model.Entity<SearchAttempt>().HasIndex(attempt => attempt.CreatedAt);
        model.Entity<SearchAttempt>().HasIndex(attempt => new { attempt.UserId, attempt.CreatedAt });
        var attempt = model.Entity<PaymentAttempt>();
        attempt.HasKey(p => p.BookingId);
        attempt.HasOne<Booking>().WithOne().HasForeignKey<PaymentAttempt>(p => p.BookingId).OnDelete(DeleteBehavior.Restrict);
        attempt.HasIndex(p => p.SessionId).IsUnique();
        attempt.HasIndex(p => p.PaymentId).IsUnique();
        attempt.Property(p => p.SessionId).HasMaxLength(255);
        attempt.Property(p => p.PaymentId).HasMaxLength(255);
        attempt.Property(p => p.IdempotencyKey).HasMaxLength(255);
        attempt.Property(p => p.CheckoutUrl).HasMaxLength(4096);
        var webhook = model.Entity<ProcessedWebhook>();
        webhook.Property(p => p.Id).HasMaxLength(255);
        webhook.HasOne<Booking>().WithMany().HasForeignKey(p => p.BookingId).OnDelete(DeleteBehavior.Restrict);
        var refund = model.Entity<RefundJob>();
        refund.HasOne<Booking>().WithMany().HasForeignKey(p => p.BookingId).OnDelete(DeleteBehavior.Restrict);
        refund.HasIndex(p => p.PaymentId).IsUnique();
        refund.HasIndex(p => new { p.State, p.NextAttemptAt });
        refund.Property(p => p.PaymentId).HasMaxLength(255);
        refund.Property(p => p.IdempotencyKey).HasMaxLength(255);
        refund.Property(p => p.ProviderRefundId).HasMaxLength(255);
        refund.Property(p => p.State).HasMaxLength(24);
        var ticket = model.Entity<Ticket>();
        ticket.HasOne<Booking>().WithMany().HasForeignKey(t => t.BookingId).OnDelete(DeleteBehavior.Restrict);
        ticket.HasOne<ApplicationUser>().WithMany().HasForeignKey(t => t.AdmittedBy).OnDelete(DeleteBehavior.Restrict);
        ticket.HasIndex(t => new { t.BookingId, t.Number }).IsUnique();
        ticket.ToTable(t => t.HasCheckConstraint("CK_Ticket_Number", "\"Number\" BETWEEN 1 AND 4"));
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
