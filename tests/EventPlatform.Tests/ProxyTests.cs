using System.Net;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;

namespace EventPlatform.Tests;

public sealed class ProxyTests
{
    [Theory]
    [InlineData("172.30.246.2", 200)]
    [InlineData("172.30.246.3", 500)]
    public async Task Production_csrf_accepts_https_forwarding_only_from_the_configured_proxy(string remoteIp, int status)
    {
        await using var app = new WebApplicationFactory<Program>().WithWebHostBuilder(builder => builder
            .UseEnvironment("Production")
            .ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ReverseProxy:Address"] = "172.30.246.2",
                ["Database:Initialize"] = "false",
                ["Reservations:RunExpiryWorker"] = "false",
                ["Payments:RunRefundWorker"] = "false",
                ["DataProtection:KeyPath"] = Path.Combine(AppContext.BaseDirectory, ".keys")
            })));

        var response = await app.Server.SendAsync(context =>
        {
            context.Connection.RemoteIpAddress = IPAddress.Parse(remoteIp);
            context.Request.Scheme = "http";
            context.Request.Path = "/api/auth/csrf";
            context.Request.Headers["X-Forwarded-Proto"] = "https";
            context.Request.Headers["X-Forwarded-For"] = "192.0.2.10";
        });

        Assert.Equal(status, response.Response.StatusCode);
        if (status == 200)
            Assert.Contains("secure", response.Response.Headers.SetCookie.ToString(), StringComparison.OrdinalIgnoreCase);
    }
}
