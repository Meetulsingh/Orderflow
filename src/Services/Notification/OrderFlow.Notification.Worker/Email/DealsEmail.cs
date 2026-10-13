using System.Globalization;
using System.Net;
using System.Text;
using OrderFlow.Contracts;

namespace OrderFlow.Notification.Worker.Email;

/// <summary>
/// The weekly deals email: a short intro, the coupon code, the discounted products and a button back to the
/// store. Table-based HTML with inline styles, because that is what Gmail and other mail apps render reliably.
/// </summary>
public static class DealsEmail
{
    private static readonly TimeSpan IndiaOffset = TimeSpan.FromMinutes(330);

    public static EmailMessage Build(WeeklyDealsLaunched deal, string toEmail, string toName, string unsubscribeUrl, string frontendUrl)
    {
        var firstName = toName.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? "there";
        var shopUrl = $"{frontendUrl}/products?coupon={Uri.EscapeDataString(deal.CouponCode)}";
        var endsIst = (deal.ExpiresAtUtc + IndiaOffset).ToString("ddd d MMM, h:mm tt", CultureInfo.InvariantCulture) + " IST";
        var subject = $"{deal.PercentOff}% off {deal.Products.Count} picks this week — code {deal.CouponCode}";

        return new EmailMessage(toEmail, toName, subject,
            Html(deal, firstName, shopUrl, endsIst, unsubscribeUrl, frontendUrl),
            Text(deal, firstName, shopUrl, endsIst, unsubscribeUrl),
            unsubscribeUrl);
    }

    private static string Html(WeeklyDealsLaunched deal, string firstName, string shopUrl, string endsIst, string unsubscribeUrl, string frontendUrl)
    {
        var h = (string s) => WebUtility.HtmlEncode(s);
        var rows = new StringBuilder();
        foreach (var p in deal.Products)
        {
            var image = p.ImageUrl is null ? null
                : p.ImageUrl.StartsWith("http", StringComparison.OrdinalIgnoreCase) ? p.ImageUrl : $"{frontendUrl}{p.ImageUrl}";
            var imageCell = image is null
                ? "<div style=\"width:96px;height:72px;border-radius:10px;background:#eef2ff\"></div>"
                : $"<img src=\"{h(image)}\" width=\"96\" height=\"72\" alt=\"{h(p.Name)}\" style=\"display:block;width:96px;height:72px;object-fit:cover;border-radius:10px;border:0\">";
            rows.Append($"""
                <tr><td style="padding:12px 0;border-top:1px solid #eef0f6">
                  <table role="presentation" width="100%" cellpadding="0" cellspacing="0"><tr>
                    <td width="96" valign="top">{imageCell}</td>
                    <td valign="middle" style="padding-left:16px;font-family:Segoe UI,Arial,sans-serif">
                      <div style="font-size:15px;font-weight:600;color:#0f172a">{h(p.Name)}</div>
                      <div style="margin-top:4px;font-size:14px">
                        <span style="color:#94a3b8;text-decoration:line-through">{Usd(p.Price)}</span>
                        <span style="color:#059669;font-weight:700;margin-left:6px">{Usd(p.DealPrice)}</span>
                      </div>
                    </td>
                  </tr></table>
                </td></tr>
                """);
        }

        return $"""
            <!doctype html>
            <html lang="en"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1"><title>{h(deal.CouponCode)}</title></head>
            <body style="margin:0;padding:0;background:#f5f6fa">
            <div style="display:none;max-height:0;overflow:hidden">{deal.PercentOff}% off {deal.Products.Count} hand-picked products until {h(endsIst)}.</div>
            <table role="presentation" width="100%" cellpadding="0" cellspacing="0" style="background:#f5f6fa;padding:24px 12px"><tr><td align="center">
              <table role="presentation" width="560" cellpadding="0" cellspacing="0" style="max-width:560px;width:100%;background:#ffffff;border-radius:16px;border:1px solid #e4e7ef">
                <tr><td style="background:#4f46e5;border-radius:16px 16px 0 0;padding:20px 28px;font-family:Segoe UI,Arial,sans-serif;font-size:18px;font-weight:700;color:#ffffff">OrderFlow</td></tr>
                <tr><td style="padding:28px;font-family:Segoe UI,Arial,sans-serif;color:#0f172a">
                  <div style="font-size:12px;font-weight:700;letter-spacing:1px;color:#4f46e5;text-transform:uppercase">This week only</div>
                  <h1 style="margin:8px 0 12px;font-size:24px;line-height:1.25">{deal.PercentOff}% off {deal.Products.Count} picks for a limited time</h1>
                  <p style="margin:0 0 20px;font-size:15px;line-height:1.6;color:#475569">Hi {h(firstName)}, we've taken {deal.PercentOff}% off a few products for this week. The offer ends <b>{h(endsIst)}</b>, and only these products are included.</p>
                  <table role="presentation" width="100%" cellpadding="0" cellspacing="0" style="border:2px dashed #a5b4fc;border-radius:12px;background:#eef2ff"><tr><td align="center" style="padding:16px">
                    <div style="font-size:12px;color:#475569">Use this code at checkout</div>
                    <div style="margin-top:4px;font-size:26px;font-weight:800;letter-spacing:3px;color:#312e81;font-family:Consolas,Menlo,monospace">{h(deal.CouponCode)}</div>
                  </td></tr></table>
                  <table role="presentation" width="100%" cellpadding="0" cellspacing="0" style="margin-top:20px">{rows}</table>
                  <table role="presentation" cellpadding="0" cellspacing="0" style="margin:24px auto 4px"><tr><td align="center" style="border-radius:10px;background:#4f46e5">
                    <a href="{h(shopUrl)}" style="display:inline-block;padding:14px 28px;font-family:Segoe UI,Arial,sans-serif;font-size:15px;font-weight:700;color:#ffffff;text-decoration:none">Check out these products</a>
                  </td></tr></table>
                  <p style="margin:12px 0 0;text-align:center;font-size:12px;color:#94a3b8">The code is applied for you when you open the store from this button.</p>
                </td></tr>
                <tr><td style="padding:18px 28px;border-top:1px solid #eef0f6;font-family:Segoe UI,Arial,sans-serif;font-size:12px;line-height:1.6;color:#94a3b8">
                  You're receiving this because you have an OrderFlow account. OrderFlow is a portfolio demo store, so no real goods are sold.
                  <a href="{h(unsubscribeUrl)}" style="color:#64748b">Unsubscribe</a>
                </td></tr>
              </table>
            </td></tr></table>
            </body></html>
            """;
    }

    private static string Text(WeeklyDealsLaunched deal, string firstName, string shopUrl, string endsIst, string unsubscribeUrl)
    {
        var lines = string.Join("\n", deal.Products.Select(p => $"  - {p.Name}: {Usd(p.DealPrice)} (was {Usd(p.Price)})"));
        return $"""
            Hi {firstName},

            This week only: {deal.PercentOff}% off these products until {endsIst}.

            {lines}

            Use code {deal.CouponCode} at checkout.
            Check out these products: {shopUrl}

            Unsubscribe: {unsubscribeUrl}
            """;
    }

    private static string Usd(decimal amount) => "$" + amount.ToString("N2", CultureInfo.InvariantCulture);
}
