using System.ComponentModel;

namespace OrderFlow.Assistant.Catalog;

/// <summary>
/// The functions the model may call during one chat turn. They only read the catalog; the shopper adds
/// products to the cart themselves from the cards that <see cref="ShowProducts"/> collects.
/// </summary>
public sealed class ShoppingTools(IReadOnlyList<CatalogProduct> catalog)
{
    public const int MaxShown = 4;
    private const int MaxResults = 8;

    private static readonly HashSet<string> StopWords =
        ["a", "an", "and", "any", "for", "the", "with", "some", "something", "me", "my", "i", "need", "want", "looking", "good", "best", "under", "below", "cheap"];

    private readonly List<CatalogProduct> _shown = [];

    public IReadOnlyList<CatalogProduct> Shown => _shown;

    [Description("Search the store catalog. Returns up to 8 products with id, name, category, price in USD, stock and rating.")]
    public object SearchProducts(
        [Description("Keywords such as 'running shoes' or 'coffee'. Use an empty string to browse everything.")] string query = "",
        [Description("Exact category name, or an empty string for all categories.")] string category = "",
        [Description("Maximum price in USD, or 0 for no limit.")] double maxPrice = 0,
        [Description("Sort order: 'relevance', 'price-asc', 'price-desc' or 'rating'.")] string sort = "relevance")
    {
        var terms = Terms(query);
        var matches = catalog
            .Where(p => category.Length == 0 || p.Category.Equals(category, StringComparison.OrdinalIgnoreCase))
            .Where(p => maxPrice <= 0 || p.Price <= (decimal)maxPrice)
            .Select(p => (Product: p, Score: Score(p, terms)))
            .Where(m => terms.Count == 0 || m.Score > 0);

        var ordered = sort switch
        {
            "price-asc" => matches.OrderBy(m => m.Product.Price),
            "price-desc" => matches.OrderByDescending(m => m.Product.Price),
            "rating" => matches.OrderByDescending(m => m.Product.Rating),
            _ => matches.OrderByDescending(m => m.Score).ThenByDescending(m => m.Product.Rating)
        };

        var results = ordered.Take(MaxResults).Select(m => new
        {
            id = m.Product.Id,
            name = m.Product.Name,
            category = m.Product.Category,
            price = m.Product.Price,
            stock = m.Product.Stock,
            rating = m.Product.Rating,
            description = m.Product.Description
        }).ToList();

        return results.Count > 0
            ? results
            : new { results, note = "No matches. Try broader keywords, another category or a higher price limit." };
    }

    [Description("Show product cards with an 'Add to cart' button to the shopper. Call it with the ids of the products you recommend, best first.")]
    public string ShowProducts([Description("Product ids from search_products (at most 4).")] string[] productIds)
    {
        foreach (var raw in productIds)
        {
            if (_shown.Count >= MaxShown) break;
            if (!Guid.TryParse(raw, out var id) || _shown.Any(p => p.Id == id)) continue;
            var product = catalog.FirstOrDefault(p => p.Id == id);
            if (product is not null) _shown.Add(product);
        }
        return _shown.Count > 0
            ? $"Showing {_shown.Count} product card(s) to the shopper. Refer to them by name; don't repeat ids."
            : "None of those ids exist. Use ids returned by search_products.";
    }

    private static List<string> Terms(string query) =>
        query.ToLowerInvariant()
            .Split([' ', ',', '.', '-', '/', '?', '!', '\'', '"'], StringSplitOptions.RemoveEmptyEntries)
            .Where(t => t.Length > 1 && !StopWords.Contains(t))
            .Select(t => t.Length > 3 && t.EndsWith('s') ? t[..^1] : t)
            .Distinct()
            .ToList();

    private static int Score(CatalogProduct product, List<string> terms)
    {
        var text = $"{product.Name} {product.Category} {product.Description}".ToLowerInvariant();
        return terms.Count(text.Contains);
    }
}
