using Microsoft.EntityFrameworkCore;
using OrderFlow.Catalog.API.Data;
using OrderFlow.ServiceDefaults;

namespace OrderFlow.Catalog.API.Products;

public static class ProductEndpoints
{
    public static void MapProductEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/products").WithTags("Products");

        group.MapGet("/", async (
            [AsParameters] ProductQuery query, CatalogDbContext db, ProductCache cache, HttpResponse response, CancellationToken ct) =>
        {
            var normalized = query with
            {
                Page = Math.Max(1, query.Page),
                PageSize = Math.Clamp(query.PageSize, 1, 48)
            };
            var (result, hit) = await cache.GetOrCreateAsync($"list:{normalized.CacheKey}",
                () => QueryProducts(db, normalized, ct), ct);
            response.Headers["X-Cache"] = hit ? "HIT" : "MISS";
            return result;
        }).WithSummary("Search, filter, sort and page the catalog (Redis cache-aside)");

        group.MapGet("/categories", async (CatalogDbContext db, ProductCache cache, HttpResponse response, CancellationToken ct) =>
        {
            var (result, hit) = await cache.GetOrCreateAsync("categories", async () =>
            {
                var groups = await db.Products.AsNoTracking()
                    .GroupBy(p => p.Category)
                    .Select(g => new { Name = g.Key, Count = g.Count() })
                    .OrderBy(c => c.Name)
                    .ToListAsync(ct);
                return groups.Select(g => new CategoryDto(g.Name, g.Count)).ToList();
            }, ct);
            response.Headers["X-Cache"] = hit ? "HIT" : "MISS";
            return result;
        }).WithSummary("Product categories with counts");

        group.MapGet("/{id:guid}", async (Guid id, CatalogDbContext db, ProductCache cache, HttpResponse response, CancellationToken ct) =>
        {
            var (result, hit) = await cache.GetOrCreateAsync<ProductDto?>($"product:{id}", async () =>
            {
                var product = await db.Products.AsNoTracking().SingleOrDefaultAsync(p => p.Id == id, ct);
                return product is null ? null : ProductDto.From(product);
            }, ct);
            response.Headers["X-Cache"] = hit ? "HIT" : "MISS";
            return result ?? throw new NotFoundException($"Product {id} was not found.");
        }).WithSummary("Get a product");

        // Used by the Ordering service to read authoritative prices and stock at checkout.
        group.MapPost("/lookup", async (LookupRequest request, CatalogDbContext db, CancellationToken ct) =>
        {
            var ids = request.Ids.Distinct().ToList();
            var products = await db.Products.AsNoTracking().Where(p => ids.Contains(p.Id)).ToListAsync(ct);
            return products.Select(ProductDto.From).ToList();
        }).Validate<LookupRequest>().WithSummary("Bulk lookup by id (service-to-service)");

        var admin = group.MapGroup("").RequireAuthorization(AppPolicies.Admin);

        admin.MapPost("/", async (UpsertProductRequest request, CatalogDbContext db, ProductCache cache, CancellationToken ct) =>
        {
            var product = new Product
            {
                Name = request.Name.Trim(),
                Description = request.Description.Trim(),
                Category = request.Category.Trim(),
                Price = request.Price,
                Stock = request.Stock,
                ImageUrl = string.IsNullOrWhiteSpace(request.ImageUrl) ? null : request.ImageUrl.Trim(),
                Rating = request.Rating ?? 0
            };
            db.Products.Add(product);
            await db.SaveChangesAsync(ct);
            await cache.InvalidateAsync(ct);
            return Results.Created($"/api/products/{product.Id}", ProductDto.From(product));
        }).Validate<UpsertProductRequest>().WithSummary("Create a product (admin)");

        admin.MapPut("/{id:guid}", async (Guid id, UpsertProductRequest request, CatalogDbContext db, ProductCache cache, CancellationToken ct) =>
        {
            var product = await db.Products.SingleOrDefaultAsync(p => p.Id == id, ct)
                ?? throw new NotFoundException($"Product {id} was not found.");
            product.Name = request.Name.Trim();
            product.Description = request.Description.Trim();
            product.Category = request.Category.Trim();
            product.Price = request.Price;
            product.Stock = request.Stock;
            product.ImageUrl = string.IsNullOrWhiteSpace(request.ImageUrl) ? null : request.ImageUrl.Trim();
            product.Rating = request.Rating ?? product.Rating;
            product.UpdatedAtUtc = DateTime.UtcNow;
            await db.SaveChangesAsync(ct);
            await cache.InvalidateAsync(ct);
            return ProductDto.From(product);
        }).Validate<UpsertProductRequest>().WithSummary("Update a product (admin)");

        admin.MapDelete("/{id:guid}", async (Guid id, CatalogDbContext db, ProductCache cache, CancellationToken ct) =>
        {
            var deleted = await db.Products.Where(p => p.Id == id).ExecuteDeleteAsync(ct);
            if (deleted == 0) throw new NotFoundException($"Product {id} was not found.");
            await cache.InvalidateAsync(ct);
            return Results.NoContent();
        }).WithSummary("Delete a product (admin)");
    }

    private static async Task<PagedResult<ProductDto>> QueryProducts(CatalogDbContext db, ProductQuery query, CancellationToken ct)
    {
        var products = db.Products.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var term = $"%{query.Search.Trim()}%";
            products = products.Where(p => EF.Functions.ILike(p.Name, term) || EF.Functions.ILike(p.Description, term));
        }
        if (!string.IsNullOrWhiteSpace(query.Category) && !query.Category.Equals("all", StringComparison.OrdinalIgnoreCase))
            products = products.Where(p => p.Category == query.Category);

        products = query.Sort switch
        {
            "price-asc" => products.OrderBy(p => p.Price),
            "price-desc" => products.OrderByDescending(p => p.Price),
            "rating" => products.OrderByDescending(p => p.Rating),
            "newest" => products.OrderByDescending(p => p.CreatedAtUtc),
            _ => products.OrderBy(p => p.Name)
        };

        var total = await products.CountAsync(ct);
        var items = await products
            .Skip((query.Page - 1) * query.PageSize)
            .Take(query.PageSize)
            .ToListAsync(ct);

        return new PagedResult<ProductDto>(items.Select(ProductDto.From).ToList(), query.Page, query.PageSize, total);
    }
}
