using System.Net;

namespace OrderFlow.Notification.Worker.Email;

/// <summary>The email a customer gets right after creating an account (transactional: no unsubscribe link).</summary>
public static class WelcomeEmail
{
    private static readonly (string Title, string Text)[] Highlights =
    [
        ("Browse the catalog", "Search, filter and add products to your cart."),
        ("Check out in three steps", "Cart, shipping, then a simulated payment you can approve, decline or crash."),
        ("Watch your order live", "Its status updates on screen the moment each service finishes its part."),
        ("Get deals every Monday", "A coupon for a few products, sent to this inbox.")
    ];

    public static EmailMessage Build(string toEmail, string fullName, string frontendUrl)
    {
        var firstName = fullName.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? "there";
        var shopUrl = $"{frontendUrl}/products";
        var subject = $"Welcome to OrderFlow, {firstName}!";
        return new EmailMessage(toEmail, fullName, subject, Html(firstName, shopUrl), Text(firstName, shopUrl), null, "welcome");
    }

    private static string Html(string firstName, string shopUrl)
    {
        var h = (string s) => WebUtility.HtmlEncode(s);
        var items = string.Concat(Highlights.Select((x, i) => $"""
            <tr><td style="padding:10px 0;border-top:{(i == 0 ? "0" : "1px solid #eef0f6")}">
              <table role="presentation" cellpadding="0" cellspacing="0"><tr>
                <td valign="top" style="width:28px;font-family:Segoe UI,Arial,sans-serif;font-size:15px;font-weight:700;color:#4f46e5">{i + 1}.</td>
                <td style="font-family:Segoe UI,Arial,sans-serif">
                  <div style="font-size:15px;font-weight:600;color:#0f172a">{h(x.Title)}</div>
                  <div style="margin-top:2px;font-size:14px;line-height:1.5;color:#475569">{h(x.Text)}</div>
                </td>
              </tr></table>
            </td></tr>
            """));

        return $"""
            <!doctype html>
            <html lang="en"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1"><title>Welcome to OrderFlow</title></head>
            <body style="margin:0;padding:0;background:#f5f6fa">
            <div style="display:none;max-height:0;overflow:hidden">Your account is ready. Here's what you can do first.</div>
            <table role="presentation" width="100%" cellpadding="0" cellspacing="0" style="background:#f5f6fa;padding:24px 12px"><tr><td align="center">
              <table role="presentation" width="560" cellpadding="0" cellspacing="0" style="max-width:560px;width:100%;background:#ffffff;border-radius:16px;border:1px solid #e4e7ef">
                <tr><td style="background:#4f46e5;border-radius:16px 16px 0 0;padding:20px 28px;font-family:Segoe UI,Arial,sans-serif;font-size:18px;font-weight:700;color:#ffffff">OrderFlow</td></tr>
                <tr><td style="padding:28px;font-family:Segoe UI,Arial,sans-serif;color:#0f172a">
                  <h1 style="margin:0 0 12px;font-size:24px;line-height:1.25">Welcome, {h(firstName)}!</h1>
                  <p style="margin:0 0 18px;font-size:15px;line-height:1.6;color:#475569">Your OrderFlow account is ready. Here's what you can do first:</p>
                  <table role="presentation" width="100%" cellpadding="0" cellspacing="0">{items}</table>
                  <table role="presentation" cellpadding="0" cellspacing="0" style="margin:24px auto 4px"><tr><td align="center" style="border-radius:10px;background:#4f46e5">
                    <a href="{h(shopUrl)}" style="display:inline-block;padding:14px 28px;font-family:Segoe UI,Arial,sans-serif;font-size:15px;font-weight:700;color:#ffffff;text-decoration:none">Start shopping</a>
                  </td></tr></table>
                </td></tr>
                <tr><td style="padding:18px 28px;border-top:1px solid #eef0f6;font-family:Segoe UI,Arial,sans-serif;font-size:12px;line-height:1.6;color:#94a3b8">
                  You're receiving this because you just created an OrderFlow account. OrderFlow is a portfolio demo store, so no real goods are sold and no real payments are taken.
                </td></tr>
              </table>
            </td></tr></table>
            </body></html>
            """;
    }

    private static string Text(string firstName, string shopUrl) => $"""
        Welcome, {firstName}!

        Your OrderFlow account is ready. Here's what you can do first:

        {string.Join("\n", Highlights.Select((x, i) => $"{i + 1}. {x.Title}: {x.Text}"))}

        Start shopping: {shopUrl}

        OrderFlow is a portfolio demo store, so no real goods are sold and no real payments are taken.
        """;
}
