using Microsoft.EntityFrameworkCore;

namespace OrderFlow.Catalog.API.Data;

public static class CatalogSeeder
{
    public static async Task SeedAsync(CatalogDbContext db)
    {
        if (await db.Products.AnyAsync()) return;

        db.Products.AddRange(
            P("Aurora Wireless Headphones", "Electronics", 179.00m, 42, 4.7, "Adaptive noise cancelling over-ear headphones with 40-hour battery life and multipoint Bluetooth 5.3."),
            P("Pulse Smartwatch S2", "Electronics", 249.00m, 30, 4.5, "AMOLED fitness smartwatch with GPS, heart-rate variability tracking and a 7-day battery."),
            P("Nimbus 4K Webcam", "Electronics", 119.00m, 25, 4.3, "Ultra-HD webcam with auto-framing, dual noise-reducing microphones and a privacy shutter."),
            P("Volt 65W GaN Charger", "Electronics", 49.00m, 120, 4.8, "Compact three-port USB-C charger that powers a laptop, phone and earbuds at the same time."),
            P("Atlas Mechanical Keyboard", "Electronics", 139.00m, 18, 4.6, "Hot-swappable 75% keyboard with gasket mount, PBT keycaps and tri-mode connectivity."),
            P("Lumen Desk Lamp", "Home", 69.00m, 55, 4.4, "Dimmable LED desk lamp with adjustable colour temperature and a wireless charging base."),
            P("Brew Pour-Over Kit", "Home", 54.00m, 40, 4.6, "Borosilicate glass dripper, gooseneck kettle and reusable steel filter for café-quality coffee."),
            P("Cloudweave Throw Blanket", "Home", 79.00m, 35, 4.8, "Ultra-soft organic cotton waffle-knit throw, pre-washed for a lived-in feel."),
            P("Terra Ceramic Planter Set", "Home", 39.00m, 60, 4.2, "Set of three matte stoneware planters with bamboo drainage trays."),
            P("Summit Trail Jacket", "Apparel", 189.00m, 22, 4.7, "Waterproof, breathable three-layer shell built for alpine weather."),
            P("Everyday Merino Tee", "Apparel", 59.00m, 80, 4.5, "Temperature-regulating merino wool t-shirt that resists odour for days."),
            P("Stride Running Shoes", "Apparel", 129.00m, 4, 4.6, "Lightweight daily trainers with a responsive foam midsole and recycled mesh upper."),
            P("Clean Architecture in .NET", "Books", 44.00m, 70, 4.9, "A practical guide to building maintainable, testable enterprise services with modern .NET."),
            P("Designing Event-Driven Systems", "Books", 39.00m, 65, 4.7, "Patterns for messaging, sagas, outboxes and eventual consistency at scale."),
            P("FlexGrip Yoga Mat", "Fitness", 64.00m, 45, 4.5, "6mm natural rubber mat with alignment markings and a non-slip polyurethane top."),
            P("CoreFit Adjustable Dumbbells", "Fitness", 299.00m, 12, 4.6, "Space-saving dumbbells that adjust from 2 kg to 24 kg in seconds."),
            P("Voyager Carry-On Backpack", "Accessories", 149.00m, 28, 4.8, "Weatherproof 35L travel pack with a clamshell opening and padded laptop sleeve."),
            P("Horizon Polarized Sunglasses", "Accessories", 89.00m, 50, 4.3, "Lightweight acetate frames with polarized, UV400-rated lenses."));

        await db.SaveChangesAsync();
    }

    private static Product P(string name, string category, decimal price, int stock, double rating, string description) =>
        new() { Name = name, Category = category, Price = price, Stock = stock, Rating = rating, Description = description };
}
