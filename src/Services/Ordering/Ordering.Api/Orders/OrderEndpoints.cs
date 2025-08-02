using BuildingBlocks.Http;
using BuildingBlocks.Messaging.Contracts;
using BuildingBlocks.Outbox;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Ordering.Api.Catalog;
using Ordering.Api.Data;
using Ordering.Api.Domain;

namespace Ordering.Api.Orders;

public static class OrderEndpoints
{
    private const int MaxListedOrders = 50;

    public static IEndpointRouteBuilder MapOrderEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var orders = endpoints.MapGroup("/api/orders").WithTags("Orders");

        orders.MapPost("/", PlaceOrder);
        orders.MapGet("/", ListOrders);
        orders.MapGet("/{id:guid}", GetOrder).WithName("GetOrder");

        return endpoints;
    }

    /// <summary>
    /// Prices the items with the catalog, stores the order and its <see cref="OrderSubmitted"/>
    /// message in one transaction and returns 202: the rest of the work happens in the saga.
    /// </summary>
    private static async Task<IResult> PlaceOrder(
        PlaceOrderRequest request,
        [FromHeader(Name = KnownHeaders.CustomerId)] string? customerId,
        OrderingDbContext db,
        IOutbox outbox,
        CatalogClient catalog,
        TimeProvider clock,
        ILoggerFactory loggerFactory,
        CancellationToken cancellationToken)
    {
        var logger = loggerFactory.CreateLogger(typeof(OrderEndpoints));
        if (string.IsNullOrWhiteSpace(customerId))
        {
            return Results.Problem(statusCode: StatusCodes.Status401Unauthorized, title: "Missing customer identity.");
        }

        var errors = request.Validate();
        if (errors.Count > 0)
        {
            return Results.ValidationProblem(errors);
        }

        var productIds = request.Items!.Select(item => item.ProductId).Distinct().ToArray();
        IReadOnlyList<CatalogProduct> products;
        try
        {
            products = await catalog.GetProductsAsync(productIds, cancellationToken);
        }
        catch (Exception ex) when (CatalogClient.IsUnavailable(ex))
        {
            logger.LogWarning(ex, "Catalog unavailable while pricing an order");
            return Results.Problem(statusCode: StatusCodes.Status503ServiceUnavailable, title: "The catalog is unavailable, try again shortly.");
        }

        var byId = products.ToDictionary(p => p.Id);
        var unknown = productIds.Where(id => !byId.ContainsKey(id)).ToArray();
        if (unknown.Length > 0)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["Items.ProductId"] = unknown.Select(id => $"Product {id} does not exist.").ToArray(),
            });
        }

        Order order;
        try
        {
            var currency = byId[productIds[0]].Currency;
            var items = request.Items!.Select(item =>
            {
                var product = byId[item.ProductId];
                return new OrderItem(product.Id, product.Name, product.Price, product.Currency, item.Quantity);
            });
            order = Order.Place(Guid.NewGuid(), customerId, currency, items, clock.GetUtcNow());
        }
        catch (OrderingDomainException ex)
        {
            return Results.Problem(statusCode: StatusCodes.Status422UnprocessableEntity, title: ex.Message);
        }

        db.Orders.Add(order);
        outbox.Enqueue(OrderMessages.Submitted(order));
        await db.SaveChangesAsync(cancellationToken);

        logger.LogInformation("Order {OrderId} placed for {Total} {Currency}", order.Id, order.Total, order.Currency);
        return Results.AcceptedAtRoute("GetOrder", new { id = order.Id }, OrderDto.From(order, saga: null));
    }

    private static async Task<IResult> GetOrder(
        Guid id,
        [FromHeader(Name = KnownHeaders.CustomerId)] string? customerId,
        OrderingDbContext db,
        CancellationToken cancellationToken)
    {
        var order = await db.Orders.AsNoTracking()
            .FirstOrDefaultAsync(o => o.Id == id && o.CustomerId == customerId, cancellationToken);

        // Another customer's order is reported as missing rather than forbidden.
        if (order is null)
        {
            return Results.NotFound();
        }

        var saga = await db.OrderSagas.AsNoTracking().FirstOrDefaultAsync(s => s.OrderId == id, cancellationToken);
        return Results.Ok(OrderDto.From(order, saga));
    }

    private static async Task<IResult> ListOrders(
        [FromHeader(Name = KnownHeaders.CustomerId)] string? customerId,
        OrderingDbContext db,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(customerId))
        {
            return Results.Problem(statusCode: StatusCodes.Status401Unauthorized, title: "Missing customer identity.");
        }

        var orders = await db.Orders.AsNoTracking()
            .Where(o => o.CustomerId == customerId)
            .OrderByDescending(o => o.CreatedAt)
            .Take(MaxListedOrders)
            .ToListAsync(cancellationToken);

        var orderIds = orders.Select(o => o.Id).ToList();
        var sagas = await db.OrderSagas.AsNoTracking()
            .Where(s => orderIds.Contains(s.OrderId))
            .ToDictionaryAsync(s => s.OrderId, cancellationToken);

        return Results.Ok(orders.Select(o => OrderDto.From(o, sagas.GetValueOrDefault(o.Id))));
    }
}
