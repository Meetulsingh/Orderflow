using FluentValidation;
using OrderFlow.Catalog.API.Data;

namespace OrderFlow.Catalog.API.Products;

public sealed record ProductDto(
    Guid Id, string Name, string Description, string Category, decimal Price,
    int Stock, string? ImageUrl, double Rating, DateTime UpdatedAtUtc)
{
    public static ProductDto From(Product p) =>
        new(p.Id, p.Name, p.Description, p.Category, p.Price, p.Stock, p.ImageUrl, p.Rating, p.UpdatedAtUtc);
}

public sealed record PagedResult<T>(IReadOnlyList<T> Items, int Page, int PageSize, int TotalCount);

public sealed record CategoryDto(string Name, int Count);

public sealed record ProductQuery(string? Search, string? Category, string? Sort, int Page = 1, int PageSize = 12)
{
    public string CacheKey =>
        $"q={Search?.Trim().ToLowerInvariant()}|c={Category?.ToLowerInvariant()}|s={Sort}|p={Page}|ps={PageSize}";
}

public sealed record UpsertProductRequest(
    string Name, string Description, string Category, decimal Price, int Stock, string? ImageUrl, double? Rating);

public sealed record LookupRequest(IReadOnlyList<Guid> Ids);

public sealed class UpsertProductRequestValidator : AbstractValidator<UpsertProductRequest>
{
    public UpsertProductRequestValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(120);
        RuleFor(x => x.Description).NotEmpty().MaximumLength(1000);
        RuleFor(x => x.Category).NotEmpty().MaximumLength(60);
        RuleFor(x => x.Price).GreaterThan(0).LessThan(1_000_000);
        RuleFor(x => x.Stock).GreaterThanOrEqualTo(0).LessThan(1_000_000);
        RuleFor(x => x.Rating).InclusiveBetween(0, 5).When(x => x.Rating.HasValue);
        RuleFor(x => x.ImageUrl)
            .Must(u => (u!.StartsWith('/') && !u.StartsWith("//"))
                       || (Uri.TryCreate(u, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps))
            .When(x => !string.IsNullOrWhiteSpace(x.ImageUrl))
            .WithMessage("Image URL must be an https URL or a site path like /products/photo.webp.");
    }
}

public sealed class LookupRequestValidator : AbstractValidator<LookupRequest>
{
    public LookupRequestValidator() => RuleFor(x => x.Ids).NotEmpty().Must(ids => ids.Count <= 100);
}
