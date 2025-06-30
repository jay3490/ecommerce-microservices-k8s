using Inventory.Api.Data;
using Inventory.Api.Domain;
using Microsoft.EntityFrameworkCore;

namespace Inventory.Api.Stock;

public sealed record StockDto(Guid ProductId, int Available, int Reserved, DateTimeOffset UpdatedAt)
{
    public static StockDto From(StockItem item) => new(item.ProductId, item.Available, item.Reserved, item.UpdatedAt);
}

public sealed record SetStockRequest(int Available);

public static class StockEndpoints
{
    public static IEndpointRouteBuilder MapStockEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var stock = endpoints.MapGroup("/api/inventory").WithTags("Inventory");

        stock.MapGet("/{productId:guid}", GetStock);
        stock.MapPut("/{productId:guid}", SetStock);

        return endpoints;
    }

    private static async Task<IResult> GetStock(Guid productId, InventoryDbContext db, CancellationToken cancellationToken)
    {
        var item = await db.StockItems.AsNoTracking().FirstOrDefaultAsync(s => s.ProductId == productId, cancellationToken);
        return item is null ? Results.NotFound() : Results.Ok(StockDto.From(item));
    }

    /// <summary>
    /// Sets the available quantity (for example after a stock count), creating the stock row
    /// for a new product. Reserved quantities are not touched.
    /// </summary>
    private static async Task<IResult> SetStock(
        Guid productId,
        SetStockRequest request,
        InventoryDbContext db,
        TimeProvider clock,
        CancellationToken cancellationToken)
    {
        if (request.Available < 0)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                [nameof(request.Available)] = ["Available stock cannot be negative."],
            });
        }

        var item = await db.StockItems.FirstOrDefaultAsync(s => s.ProductId == productId, cancellationToken);
        if (item is null)
        {
            item = StockItem.Create(productId, request.Available, clock.GetUtcNow());
            db.StockItems.Add(item);
        }
        else
        {
            item.SetAvailable(request.Available, clock.GetUtcNow());
        }

        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            return Results.Problem(statusCode: StatusCodes.Status409Conflict, title: "Stock changed concurrently, retry the update.");
        }

        return Results.Ok(StockDto.From(item));
    }
}
