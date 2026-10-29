using Microsoft.AspNetCore.Identity;

namespace EventPlatform.Api.Auth;

public sealed class ApplicationUser : IdentityUser<Guid>
{
    public string DisplayName { get; set; } = "";
}

public sealed class Organisation
{
    public Guid Id { get; set; }
    public string Name { get; set; } = "";
}

public sealed class OrganisationMembership
{
    public Guid UserId { get; set; }
    public Guid OrganisationId { get; set; }
    public string Role { get; set; } = "";
    public Organisation Organisation { get; set; } = null!;
}

public static class OrganisationRoles
{
    public const string Organiser = "Organiser";
    public const string CheckInStaff = "CheckInStaff";
}
