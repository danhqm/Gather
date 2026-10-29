using EventPlatform.Api.Auth;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace EventPlatform.Api.Data;

public static class DemoAccounts
{
    public static readonly Guid OrganisationId = Guid.Parse("00000000-0000-0000-0000-000000000101");

    public static async Task SeedAsync(IServiceProvider services, IConfiguration config)
    {
        var db = services.GetRequiredService<AppDbContext>();
        if (!await db.Organisations.AnyAsync(o => o.Id == OrganisationId))
        {
            db.Organisations.Add(new() { Id = OrganisationId, Name = "Gather Studio" });
            await db.SaveChangesAsync();
        }
        var password = config["Demo:AccountPassword"];
        if (string.IsNullOrWhiteSpace(password)) return;
        var users = services.GetRequiredService<UserManager<ApplicationUser>>();
        foreach (var account in new[] {
            (Id: 201, Email: "organiser@gather.test", Name: "Gather Organiser", Role: OrganisationRoles.Organiser),
            (Id: 202, Email: "staff@gather.test", Name: "Gather Check-in", Role: OrganisationRoles.CheckInStaff) })
        {
            var expectedId = Guid.Parse($"00000000-0000-0000-0000-{account.Id:D12}");
            var user = await users.FindByEmailAsync(account.Email);
            if (user is null)
            {
                user = new() { Id = expectedId, Email = account.Email, UserName = account.Email, DisplayName = account.Name };
                var result = await users.CreateAsync(user, password);
                if (!result.Succeeded) throw new InvalidOperationException("The configured demo password does not meet the account password requirements.");
            }
            if (user.Id != expectedId) throw new InvalidOperationException("A demo account email is already owned by a different user; refusing to grant permissions.");
            if (!await db.OrganisationMemberships.AnyAsync(m => m.UserId == user.Id && m.OrganisationId == OrganisationId))
            {
                db.OrganisationMemberships.Add(new() { UserId = user.Id, OrganisationId = OrganisationId, Role = account.Role });
                await db.SaveChangesAsync();
            }
        }
    }
}
