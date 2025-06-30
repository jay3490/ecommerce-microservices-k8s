using Inventory.Api.Domain;

namespace Inventory.Api.Data;

/// <summary>
/// Stock for the catalog's demo products (same ids as the catalog seed). The headset starts
/// out of stock so the "reservation failed" path can be tried without any setup.
/// </summary>
internal static class StockSeed
{
    private static readonly DateTimeOffset SeededAt = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    public static readonly StockItem[] Items =
    [
        StockItem.Create(Guid.Parse("8f3c2a4e-1b6d-4c1a-9e57-0a1f5b2c3d01"), 50, SeededAt),
        StockItem.Create(Guid.Parse("8f3c2a4e-1b6d-4c1a-9e57-0a1f5b2c3d02"), 100, SeededAt),
        StockItem.Create(Guid.Parse("8f3c2a4e-1b6d-4c1a-9e57-0a1f5b2c3d03"), 10, SeededAt),
        StockItem.Create(Guid.Parse("8f3c2a4e-1b6d-4c1a-9e57-0a1f5b2c3d04"), 0, SeededAt),
        StockItem.Create(Guid.Parse("8f3c2a4e-1b6d-4c1a-9e57-0a1f5b2c3d05"), 25, SeededAt),
    ];
}
