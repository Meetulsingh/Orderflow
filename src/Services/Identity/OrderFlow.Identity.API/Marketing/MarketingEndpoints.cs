using System.Net;
using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using OrderFlow.Identity.API.Data;
using OrderFlow.ServiceDefaults;

namespace OrderFlow.Identity.API.Marketing;

public sealed record MarketingRecipient(Guid UserId, string Email, string FullName, string UnsubscribeUrl);

/// <summary>
/// Identity owns who may receive marketing email: the recipient list for campaigns (internal only) and
/// the signed one-click unsubscribe link that every deals email carries.
/// </summary>
public static class MarketingEndpoints
{
    // Demo, test and reserved domains never receive real email.
    private static readonly string[] SkippedDomains = ["orderflow.dev", "example.com", "example.org", "example.net"];
    private static readonly string[] SkippedSuffixes = [".test", ".invalid", ".local", ".example"];

    public static void MapMarketingEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/internal/marketing/recipients", async (HttpContext http, IdentityDbContext db, JwtOptions jwt, CancellationToken ct) =>
            {
                var users = await db.Users.AsNoTracking()
                    .Where(u => !u.MarketingOptOut)
                    .OrderBy(u => u.CreatedAtUtc)
                    .Select(u => new { u.Id, u.Email, u.FullName })
                    .ToListAsync(ct);

                var baseUrl = $"{http.Request.Scheme}://{http.Request.Host}";
                return users
                    .Where(u => IsDeliverable(u.Email))
                    .Select(u => new MarketingRecipient(u.Id, u.Email, u.FullName,
                        $"{baseUrl}/api/auth/unsubscribe?u={u.Id}&t={Token(jwt, u.Id)}"))
                    .ToList();
            })
            .RequireInternalKey()
            .WithTags("Marketing")
            .WithSummary("Customers who receive deals emails (internal: Notification service only)");

        // GET for the link in the email body; POST for mail apps' one-click unsubscribe (RFC 8058).
        app.MapMethods("/api/auth/unsubscribe", ["GET", "POST"], async (Guid u, string t, IdentityDbContext db, JwtOptions jwt, CancellationToken ct) =>
            {
                if (!CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(t), Encoding.ASCII.GetBytes(Token(jwt, u))))
                    return Page("This unsubscribe link isn't valid.", "It may have been copied incompletely.", StatusCodes.Status400BadRequest);

                var user = await db.Users.SingleOrDefaultAsync(x => x.Id == u, ct);
                if (user is not null && !user.MarketingOptOut)
                {
                    user.MarketingOptOut = true;
                    await db.SaveChangesAsync(ct);
                }
                return Page("You're unsubscribed.", "You won't receive OrderFlow deals emails anymore. Order emails are not affected.", 200);
            })
            .AllowAnonymous()
            .WithTags("Marketing")
            .WithSummary("Stop deals emails for one customer (signed link from the email)");
    }

    public static bool IsDeliverable(string email)
    {
        var at = email.LastIndexOf('@');
        if (at <= 0) return false;
        var domain = email[(at + 1)..].ToLowerInvariant();
        return !SkippedDomains.Contains(domain) && !SkippedSuffixes.Any(domain.EndsWith);
    }

    /// <summary>A signature over the user id, so nobody can unsubscribe someone else by guessing ids.</summary>
    private static string Token(JwtOptions jwt, Guid userId) =>
        Convert.ToHexString(HMACSHA256.HashData(Encoding.UTF8.GetBytes(jwt.Key), Encoding.UTF8.GetBytes($"unsubscribe:{userId}")))
            [..32].ToLowerInvariant();

    private static IResult Page(string title, string message, int status) => Results.Content($$"""
        <!doctype html><html lang="en"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1">
        <title>{{WebUtility.HtmlEncode(title)}}</title></head>
        <body style="margin:0;font-family:Segoe UI,Arial,sans-serif;background:#f5f6fa;color:#0f172a;display:grid;place-items:center;min-height:100vh">
        <div style="background:#fff;border:1px solid #e4e7ef;border-radius:16px;padding:32px 36px;max-width:420px;text-align:center">
        <h1 style="font-size:22px;margin:0 0 10px">{{WebUtility.HtmlEncode(title)}}</h1>
        <p style="color:#475569;margin:0">{{WebUtility.HtmlEncode(message)}}</p></div></body></html>
        """, "text/html", Encoding.UTF8, status);
}
