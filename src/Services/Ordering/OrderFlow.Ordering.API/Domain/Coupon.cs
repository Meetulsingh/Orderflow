using System.Security.Cryptography;
using OrderFlow.ServiceDefaults;

namespace OrderFlow.Ordering.API.Domain;

/// <summary>
/// A time-limited coupon from a deals campaign: <see cref="PercentOff"/> off the listed products only,
/// valid until <see cref="ExpiresAtUtc"/>. One campaign (e.g. one ISO week) creates exactly one coupon.
/// </summary>
public sealed class Coupon
{
    public Guid Id { get; private init; } = Guid.CreateVersion7();
    public string Code { get; private init; } = string.Empty;
    public string CampaignKey { get; private init; } = string.Empty;
    public int PercentOff { get; private init; }
    public List<Guid> ProductIds { get; private init; } = [];
    public DateTime StartsAtUtc { get; private init; }
    public DateTime ExpiresAtUtc { get; private init; }

    private Coupon() { }

    public static Coupon Create(string campaignKey, int percentOff, IReadOnlyCollection<Guid> productIds, DateTime nowUtc, TimeSpan duration)
    {
        if (percentOff is < 1 or > 90) throw new BusinessRuleException("A coupon must take 1-90% off.");
        if (productIds.Count == 0) throw new BusinessRuleException("A coupon must apply to at least one product.");
        return new Coupon
        {
            Code = GenerateCode(),
            CampaignKey = campaignKey,
            PercentOff = percentOff,
            ProductIds = productIds.Distinct().ToList(),
            StartsAtUtc = nowUtc,
            ExpiresAtUtc = nowUtc.Add(duration)
        };
    }

    public bool IsActive(DateTime nowUtc) => nowUtc >= StartsAtUtc && nowUtc < ExpiresAtUtc;

    public bool AppliesTo(Guid productId) => ProductIds.Contains(productId);

    /// <summary>The coupon's price for one unit, rounded to cents.</summary>
    public decimal DealPrice(decimal price) => Math.Round(price * (100 - PercentOff) / 100m, 2, MidpointRounding.AwayFromZero);

    public static string Normalize(string code) => code.Trim().ToUpperInvariant();

    // No 0/O or 1/I so a code read from an email is easy to type.
    private static string GenerateCode()
    {
        const string alphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";
        Span<char> suffix = stackalloc char[5];
        for (var i = 0; i < suffix.Length; i++) suffix[i] = alphabet[RandomNumberGenerator.GetInt32(alphabet.Length)];
        return $"DEALS-{suffix}";
    }
}
