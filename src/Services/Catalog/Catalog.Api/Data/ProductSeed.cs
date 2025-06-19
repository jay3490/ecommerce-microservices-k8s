using Catalog.Api.Domain;

namespace Catalog.Api.Data;

/// <summary>
/// Demo products created by the initial migration. The inventory service seeds stock for
/// the same ids, so an order can be placed right after <c>docker compose up</c>.
/// </summary>
internal static class ProductSeed
{
    private static readonly DateTimeOffset SeededAt = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    public static readonly Product[] Products =
    [
        Product.Create(Guid.Parse("8f3c2a4e-1b6d-4c1a-9e57-0a1f5b2c3d01"), "KB-MECH-01", "Mechanical keyboard",
            "Tenkeyless keyboard with hot-swappable switches.", 89.00m, "USD", SeededAt),
        Product.Create(Guid.Parse("8f3c2a4e-1b6d-4c1a-9e57-0a1f5b2c3d02"), "MS-WL-02", "Wireless mouse",
            "Ergonomic mouse with a 2.4 GHz receiver.", 39.50m, "USD", SeededAt),
        Product.Create(Guid.Parse("8f3c2a4e-1b6d-4c1a-9e57-0a1f5b2c3d03"), "MN-27-4K", "27\" 4K monitor",
            "IPS panel, USB-C with 65 W power delivery.", 329.00m, "USD", SeededAt),
        Product.Create(Guid.Parse("8f3c2a4e-1b6d-4c1a-9e57-0a1f5b2c3d04"), "HS-USB-04", "USB headset",
            "Closed-back headset with a detachable microphone.", 59.99m, "USD", SeededAt),
        Product.Create(Guid.Parse("8f3c2a4e-1b6d-4c1a-9e57-0a1f5b2c3d05"), "DK-TB4-05", "Thunderbolt dock",
            "Dual display dock with 2.5 GbE and 96 W charging.", 1249.00m, "USD", SeededAt),
    ];
}
