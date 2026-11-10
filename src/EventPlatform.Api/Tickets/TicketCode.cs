using System.Security.Cryptography;
using Microsoft.AspNetCore.DataProtection;

namespace EventPlatform.Api.Tickets;

public sealed class TicketCode(IDataProtectionProvider provider)
{
    private readonly IDataProtector protector = provider.CreateProtector("Gather.AdmissionTicket.v1");
    public string Sign(Guid ticketId) => protector.Protect(ticketId.ToString("N"));
    public Guid? Verify(string? code)
    {
        if (string.IsNullOrWhiteSpace(code) || code.Length > 1024) return null;
        try { return Guid.TryParseExact(protector.Unprotect(code), "N", out var id) ? id : null; }
        catch (Exception error) when (error is CryptographicException or FormatException) { return null; }
    }
}
