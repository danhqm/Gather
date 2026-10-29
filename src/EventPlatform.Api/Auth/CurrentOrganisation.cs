using EventPlatform.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace EventPlatform.Api.Auth;

public sealed class CurrentOrganisation(AppDbContext db)
{
    public Task<bool> RequireRoleAsync(Guid userId, Guid organisationId, string role, CancellationToken ct) =>
        db.OrganisationMemberships.AnyAsync(m => m.UserId == userId && m.OrganisationId == organisationId &&
            (m.Role == role || role == OrganisationRoles.CheckInStaff && m.Role == OrganisationRoles.Organiser), ct);
}
