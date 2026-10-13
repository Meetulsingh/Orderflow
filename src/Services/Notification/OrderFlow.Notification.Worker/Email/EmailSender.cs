using System.Net.Http.Json;
using System.Text.Json;

namespace OrderFlow.Notification.Worker.Email;

/// <param name="Tag">Shown in Brevo's statistics to tell email types apart (e.g. "weekly-deals", "welcome").</param>
public sealed record EmailMessage(string ToEmail, string ToName, string Subject, string Html, string Text, string? UnsubscribeUrl, string Tag);

public sealed record EmailResult(bool Delivered, string Status, string? ProviderMessageId = null, string? Error = null);

public sealed class EmailOptions
{
    public string? BrevoApiKey { get; init; }
    public string FromEmail { get; init; } = "no-reply@orderflow.dev";
    public string FromName { get; init; } = "OrderFlow";
    public string FrontendUrl { get; init; } = "http://localhost:4200";

    public bool IsLive => !string.IsNullOrWhiteSpace(BrevoApiKey);

    public static EmailOptions From(IConfiguration config) => new()
    {
        BrevoApiKey = config["BREVO_API_KEY"],
        FromEmail = config["MAIL_FROM_EMAIL"] ?? "no-reply@orderflow.dev",
        FromName = config["MAIL_FROM_NAME"] ?? "OrderFlow",
        FrontendUrl = (config["FRONTEND_URL"] ?? "http://localhost:4200").TrimEnd('/')
    };
}

public interface IEmailSender
{
    Task<EmailResult> SendAsync(EmailMessage message, CancellationToken ct);
}

/// <summary>
/// Sends through Brevo's HTTPS API (free: 300 emails a day). Render's free tier blocks SMTP ports, so an
/// HTTP email API is the only way to deliver real mail from it.
/// </summary>
public sealed class BrevoEmailSender(HttpClient http, EmailOptions options, ILogger<BrevoEmailSender> logger) : IEmailSender
{
    public async Task<EmailResult> SendAsync(EmailMessage message, CancellationToken ct)
    {
        var headers = new Dictionary<string, string>();
        if (message.UnsubscribeUrl is not null)
        {
            // Lets Gmail show its own "Unsubscribe" button (one-click, RFC 8058).
            headers["List-Unsubscribe"] = $"<{message.UnsubscribeUrl}>";
            headers["List-Unsubscribe-Post"] = "List-Unsubscribe=One-Click";
        }

        using var request = new HttpRequestMessage(HttpMethod.Post, "https://api.brevo.com/v3/smtp/email")
        {
            Content = JsonContent.Create(new
            {
                sender = new { name = options.FromName, email = options.FromEmail },
                to = new[] { new { email = message.ToEmail, name = message.ToName } },
                subject = message.Subject,
                htmlContent = message.Html,
                textContent = message.Text,
                headers,
                tags = new[] { message.Tag }
            })
        };
        request.Headers.Add("api-key", options.BrevoApiKey);

        using var response = await http.SendAsync(request, ct);
        var body = await response.Content.ReadAsStringAsync(ct);
        if (response.IsSuccessStatusCode)
        {
            string? id = null;
            try { id = JsonDocument.Parse(body).RootElement.GetProperty("messageId").GetString(); } catch { /* id is optional */ }
            return new EmailResult(true, "sent", id);
        }

        logger.LogWarning("Brevo rejected email to {Recipient}: {Status} {Body}", message.ToEmail, (int)response.StatusCode, body);
        // 5xx and 429 are worth retrying later; 4xx (bad address, unverified sender) are not.
        if ((int)response.StatusCode >= 500 || (int)response.StatusCode == 429)
            throw new HttpRequestException($"Brevo returned {(int)response.StatusCode}");
        return new EmailResult(false, "rejected", Error: $"{(int)response.StatusCode}: {body}");
    }
}

/// <summary>
/// Used when no provider key is configured: the email is recorded and logged, and in Development its HTML
/// is also written to a temp folder so it can be previewed in a browser.
/// </summary>
public sealed class SimulatedEmailSender(IHostEnvironment environment, ILogger<SimulatedEmailSender> logger) : IEmailSender
{
    public async Task<EmailResult> SendAsync(EmailMessage message, CancellationToken ct)
    {
        if (environment.IsDevelopment())
        {
            var folder = Path.Combine(Path.GetTempPath(), "orderflow-emails");
            Directory.CreateDirectory(folder);
            var safeName = string.Concat(message.ToEmail.Select(c => char.IsLetterOrDigit(c) ? c : '_'));
            var file = Path.Combine(folder, $"{DateTime.UtcNow:yyyyMMdd-HHmmss}-{safeName}.html");
            await File.WriteAllTextAsync(file, message.Html, ct);
            logger.LogInformation("📧 (simulated) {Subject} → {Recipient}, preview: {File}", message.Subject, message.ToEmail, file);
        }
        else
        {
            logger.LogInformation("📧 (simulated) {Subject} → {Recipient}", message.Subject, message.ToEmail);
        }
        return new EmailResult(true, "simulated");
    }
}
