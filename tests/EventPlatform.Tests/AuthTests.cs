using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using EventPlatform.Api.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace EventPlatform.Tests;

public sealed class AuthTests(WebAppFixture fixture) : IClassFixture<WebAppFixture>, IDisposable
{
    private readonly HttpClient client = fixture.CreateClient();
    private const string Organisation = "00000000-0000-0000-0000-000000000101";

    private async Task<HttpResponseMessage> Post(string path, object body, HttpClient? target = null)
    {
        target ??= client;
        var csrfResponse = await target.GetAsync("/api/auth/csrf");
        Assert.Equal(HttpStatusCode.OK, csrfResponse.StatusCode);
        using var csrf = JsonDocument.Parse(await csrfResponse.Content.ReadAsStringAsync());
        target.DefaultRequestHeaders.Remove("X-CSRF-TOKEN");
        target.DefaultRequestHeaders.Add("X-CSRF-TOKEN", csrf.RootElement.GetProperty("token").GetString());
        return await target.PostAsJsonAsync(path, body);
    }

    [Fact]
    public async Task Registration_cannot_assign_organiser_role()
    {
        var response = await Post("/api/auth/register", new { email = $"person-{Guid.NewGuid():N}@example.test",
            displayName = "A new attendee", password = "Local-Test-Password123!", role = "Organiser", organisationId = Organisation });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        using var user = JsonDocument.Parse(await client.GetStringAsync("/api/auth/me"));
        Assert.Empty(user.RootElement.GetProperty("memberships").EnumerateArray());
        await using var scope = fixture.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var userId = user.RootElement.GetProperty("id").GetGuid();
        Assert.False(await db.OrganisationMemberships.AnyAsync(m => m.UserId == userId));
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync($"/api/organiser/organisations/{Organisation}")).StatusCode);
    }

    [Fact]
    public async Task Mutation_without_csrf_is_rejected()
    {
        var email = $"no-csrf-{Guid.NewGuid():N}@example.test";
        var response = await client.PostAsJsonAsync("/api/auth/register", new {
            email, displayName = "Attendee", password = "Local-Test-Password123!" });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        await using var scope = fixture.Services.CreateAsyncScope();
        Assert.False(await scope.ServiceProvider.GetRequiredService<AppDbContext>().Users.AnyAsync(u => u.Email == email));
    }

    [Fact]
    public async Task Anonymous_protected_request_returns_401()
    {
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/auth/me")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync($"/api/organiser/organisations/{Organisation}")).StatusCode);
    }

    [Fact]
    public async Task Cross_organisation_role_is_rejected()
    {
        var otherId = Guid.NewGuid();
        await using var scope = fixture.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.Organisations.Add(new() { Id = otherId, Name = "Another organisation" });
        await db.SaveChangesAsync();
        Assert.Equal(HttpStatusCode.OK, (await Post("/api/auth/login", new {
            email = "organiser@gather.test", password = "Local-Test-Password123!" })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync($"/api/organiser/organisations/{Organisation}")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync($"/api/organiser/organisations/{otherId}")).StatusCode);
    }

    [Fact]
    public async Task Login_refresh_logout_and_wrong_password_use_cookie_authentication()
    {
        Assert.Equal(HttpStatusCode.Unauthorized, (await Post("/api/auth/login", new {
            email = "organiser@gather.test", password = "Incorrect-Password123!" })).StatusCode);
        var login = await Post("/api/auth/login", new { email = "organiser@gather.test", password = "Local-Test-Password123!" });
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        Assert.Contains(login.Headers.GetValues("Set-Cookie"), cookie => cookie.Contains("gather.session") && cookie.Contains("httponly", StringComparison.OrdinalIgnoreCase));
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/auth/me")).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await Post("/api/auth/logout", new { })).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/auth/me")).StatusCode);
    }

    public void Dispose() => client.Dispose();

    [Fact]
    public async Task Production_login_cookie_requires_HTTPS()
    {
        await using var production = fixture.WithEnvironment("Production");
        using var secureClient = production.CreateClient(new() { BaseAddress = new Uri("https://localhost"), AllowAutoRedirect = false });
        var response = await Post("/api/auth/login", new { email = "organiser@gather.test", password = "Local-Test-Password123!" }, secureClient);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains(response.Headers.GetValues("Set-Cookie"), value =>
            value.Contains("gather.session") && value.Contains("; secure", StringComparison.OrdinalIgnoreCase)
            && value.Contains("httponly", StringComparison.OrdinalIgnoreCase));
    }
}
