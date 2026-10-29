using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using EventPlatform.Api.Data;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace EventPlatform.Api.Auth;

public static class AuthEndpoints
{
    public static void MapAuthEndpoints(this WebApplication app)
    {
        var auth = app.MapGroup("/api/auth").AddEndpointFilter<CsrfFilter>();
        auth.MapGet("/csrf", (HttpContext context, IAntiforgery antiforgery) =>
        {
            context.Response.Headers.CacheControl = "no-store";
            return Results.Ok(new { token = antiforgery.GetAndStoreTokens(context).RequestToken });
        });
        auth.MapPost("/register", async (RegisterRequest request, UserManager<ApplicationUser> users,
            SignInManager<ApplicationUser> signIn, AppDbContext db, CancellationToken ct) =>
        {
            var email = request.Email?.Trim();
            var name = request.DisplayName?.Trim();
            if (email is null || email.Length > 254 || !new EmailAddressAttribute().IsValid(email)
                || string.IsNullOrWhiteSpace(name) || name.Length > 80 || request.Password is null || request.Password.Length > 128)
                return Results.Problem("Enter a valid email, name and password.", statusCode: 400);
            var user = new ApplicationUser { Id = Guid.NewGuid(), UserName = email, Email = email, DisplayName = name };
            var result = await users.CreateAsync(user, request.Password);
            if (!result.Succeeded)
                return Results.Problem("Account could not be created. Use a new email and a password of 12–128 characters with uppercase, lowercase, a number and a symbol.", statusCode: 400);
            await signIn.SignInAsync(user, isPersistent: false);
            return Results.Created("/api/auth/me", await Profile(user, db, ct));
        }).RequireRateLimiting("authentication");
        auth.MapPost("/login", async (LoginRequest request, UserManager<ApplicationUser> users,
            SignInManager<ApplicationUser> signIn, AppDbContext db, CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(request.Email) || request.Email.Length > 254
                || request.Password is null || request.Password.Length > 128)
                return Results.Unauthorized();
            var user = await users.FindByEmailAsync(request.Email.Trim());
            if (user is null || !(await signIn.PasswordSignInAsync(user, request.Password, false, lockoutOnFailure: true)).Succeeded)
                return Results.Unauthorized();
            return Results.Ok(await Profile(user, db, ct));
        }).RequireRateLimiting("authentication");
        auth.MapGet("/me", async (HttpContext context, UserManager<ApplicationUser> users, AppDbContext db, CancellationToken ct) =>
        {
            context.Response.Headers.CacheControl = "no-store";
            var user = await users.GetUserAsync(context.User);
            return user is null ? Results.Unauthorized() : Results.Ok(await Profile(user, db, ct));
        }).RequireAuthorization();
        auth.MapPost("/logout", async (SignInManager<ApplicationUser> signIn) =>
        {
            await signIn.SignOutAsync();
            return Results.NoContent();
        }).RequireAuthorization();

        app.MapGet("/api/organiser/organisations/{organisationId:guid}", async (Guid organisationId, ClaimsPrincipal principal,
            CurrentOrganisation permissions, AppDbContext db, CancellationToken ct) =>
        {
            var userId = Guid.Parse(principal.FindFirstValue(ClaimTypes.NameIdentifier)!);
            if (!await permissions.RequireRoleAsync(userId, organisationId, OrganisationRoles.Organiser, ct)) return Results.Forbid();
            return Results.Ok(await db.Organisations.AsNoTracking().Where(o => o.Id == organisationId)
                .Select(o => new { o.Id, o.Name }).SingleAsync(ct));
        }).RequireAuthorization();
    }

    private static async Task<UserProfile> Profile(ApplicationUser user, AppDbContext db, CancellationToken ct) =>
        new(user.Id, user.DisplayName, user.Email!, await db.OrganisationMemberships.AsNoTracking()
            .Where(m => m.UserId == user.Id).Select(m => new MembershipProfile(m.OrganisationId, m.Organisation.Name, m.Role)).ToArrayAsync(ct));

    public sealed record RegisterRequest(string? Email, string? Password, string? DisplayName);
    public sealed record LoginRequest(string? Email, string? Password);
    public sealed record UserProfile(Guid Id, string DisplayName, string Email, MembershipProfile[] Memberships);
    public sealed record MembershipProfile(Guid OrganisationId, string OrganisationName, string Role);
}
