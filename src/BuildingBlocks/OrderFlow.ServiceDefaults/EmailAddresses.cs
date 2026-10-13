namespace OrderFlow.ServiceDefaults;

public static class EmailAddresses
{
    // Demo accounts, smoke-test sign-ups and reserved domains must never receive real email.
    private static readonly string[] SkippedDomains = ["orderflow.dev", "example.com", "example.org", "example.net"];
    private static readonly string[] SkippedSuffixes = [".test", ".invalid", ".local", ".example"];

    public static bool IsDeliverable(string email)
    {
        var at = email.LastIndexOf('@');
        if (at <= 0 || at == email.Length - 1) return false;
        var domain = email[(at + 1)..].ToLowerInvariant();
        return !SkippedDomains.Contains(domain) && !SkippedSuffixes.Any(domain.EndsWith);
    }
}
