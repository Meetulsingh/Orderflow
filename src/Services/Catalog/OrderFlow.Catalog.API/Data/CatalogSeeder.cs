using Microsoft.EntityFrameworkCore;

namespace OrderFlow.Catalog.API.Data;

public static class CatalogSeeder
{
    private sealed record SeedProduct(
        string Name, string Category, decimal Price, int Stock, double Rating, string Image, string Description,
        string? PreviousName = null);

    // Images are served by the frontend's static host (frontend/public/products), so they load from the CDN.
    private static readonly SeedProduct[] Products =
    [
        new("Aurora Wireless Headphones", "Electronics", 179.00m, 42, 4.7, "aurora-headphones", "Adaptive noise cancelling over-ear headphones with 40-hour battery life and multipoint Bluetooth 5.3."),
        new("Pulse Smartwatch S2", "Electronics", 249.00m, 30, 4.5, "pulse-smartwatch", "AMOLED fitness smartwatch with GPS, heart-rate variability tracking and a 7-day battery."),
        new("Nimbus 4K Webcam", "Electronics", 119.00m, 25, 4.3, "nimbus-webcam", "Ultra-HD webcam with auto-framing, dual noise-reducing microphones and a privacy shutter."),
        new("Volt 65W GaN Charger", "Electronics", 49.00m, 120, 4.8, "volt-charger", "Compact three-port USB-C charger that powers a laptop, phone and earbuds at the same time."),
        new("Atlas Mechanical Keyboard", "Electronics", 139.00m, 18, 4.6, "atlas-keyboard", "Hot-swappable 75% keyboard with gasket mount, PBT keycaps and tri-mode connectivity."),
        new("Lumen Desk Lamp", "Home", 69.00m, 55, 4.4, "lumen-lamp", "Dimmable LED desk lamp with adjustable colour temperature and a wireless charging base."),
        new("Brew Pour-Over Kit", "Home", 54.00m, 40, 4.6, "brew-pour-over", "Borosilicate glass dripper, gooseneck kettle and reusable steel filter for café-quality coffee."),
        new("Cloudweave Throw Blanket", "Home", 79.00m, 35, 4.8, "cloudweave-blanket", "Ultra-soft organic cotton waffle-knit throw, pre-washed for a lived-in feel."),
        new("Terra Ceramic Planter Set", "Home", 39.00m, 60, 4.2, "terra-planter", "Set of three matte stoneware planters with bamboo drainage trays."),
        new("Summit Leather Jacket", "Apparel", 189.00m, 22, 4.7, "summit-jacket", "Classic biker jacket in soft full-grain leather with a quilted lining and YKK zips.",
            PreviousName: "Summit Trail Jacket"),
        new("Everyday Merino Tee", "Apparel", 59.00m, 80, 4.5, "merino-tee", "Temperature-regulating merino wool t-shirt that resists odour for days."),
        new("Stride Running Shoes", "Apparel", 129.00m, 4, 4.6, "stride-shoes", "Lightweight daily trainers with a responsive foam midsole and recycled mesh upper."),
        new("Clean Architecture in .NET", "Books", 44.00m, 70, 4.9, "clean-architecture-book", "A practical guide to building maintainable, testable enterprise services with modern .NET."),
        new("Designing Event-Driven Systems", "Books", 39.00m, 65, 4.7, "event-driven-book", "Patterns for messaging, sagas, outboxes and eventual consistency at scale."),
        new("FlexGrip Yoga Mat", "Fitness", 64.00m, 45, 4.5, "flexgrip-yoga-mat", "6mm natural rubber mat with alignment markings and a non-slip polyurethane top."),
        new("CoreFit Adjustable Dumbbells", "Fitness", 299.00m, 12, 4.6, "corefit-dumbbells", "Space-saving dumbbells that adjust from 2 kg to 24 kg in seconds."),
        new("Voyager Carry-On Backpack", "Accessories", 149.00m, 28, 4.8, "voyager-backpack", "Weatherproof 35L travel pack with a clamshell opening and padded laptop sleeve."),
        new("Horizon Polarized Sunglasses", "Accessories", 89.00m, 50, 4.3, "horizon-sunglasses", "Lightweight acetate frames with polarized, UV400-rated lenses.")
    ];

    private static string ImagePath(SeedProduct p) => $"/products/{p.Image}.webp";

    public static async Task SeedAsync(CatalogDbContext db)
    {
        if (!await db.Products.AnyAsync())
        {
            db.Products.AddRange(Products.Select(p => new Product
            {
                Name = p.Name, Category = p.Category, Price = p.Price, Stock = p.Stock, Rating = p.Rating,
                Description = p.Description, ImageUrl = ImagePath(p)
            }));
            await db.SaveChangesAsync();
            return;
        }

        await BackfillSeedImagesAsync(db);
    }

    /// <summary>
    /// Upgrades databases seeded before product photos existed: adds the photo to seed products that have
    /// none (and applies renames). Products created or edited by an admin keep their own image.
    /// </summary>
    private static async Task BackfillSeedImagesAsync(CatalogDbContext db)
    {
        var names = Products.SelectMany(p => new[] { p.Name, p.PreviousName }).OfType<string>().ToList();
        var existing = await db.Products.Where(p => p.ImageUrl == null && names.Contains(p.Name)).ToListAsync();
        if (existing.Count == 0) return;

        foreach (var product in existing)
        {
            var seed = Products.First(s => s.Name == product.Name || s.PreviousName == product.Name);
            product.ImageUrl = ImagePath(seed);
            if (seed.PreviousName == product.Name)
            {
                product.Name = seed.Name;
                product.Description = seed.Description;
            }
            product.UpdatedAtUtc = DateTime.UtcNow;
        }
        await db.SaveChangesAsync();
    }
}
