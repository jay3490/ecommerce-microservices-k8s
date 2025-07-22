using Basket.Api.Domain;
using BuildingBlocks.Http;
using Microsoft.AspNetCore.Mvc;

namespace Basket.Api.Baskets;

public sealed record SetQuantityRequest(int Quantity);

public sealed record BasketDto(IReadOnlyList<BasketItem> Items, DateTimeOffset? UpdatedAt)
{
    public static BasketDto From(CustomerBasket? basket) =>
        basket is null ? new BasketDto([], null) : new BasketDto(basket.Items, basket.UpdatedAt);
}

public sealed record CheckoutAccepted(Guid OrderId);

/// <summary>The caller's basket. The customer id comes from the gateway's X-Customer-Id header.</summary>
public static class BasketEndpoints
{
    public static IEndpointRouteBuilder MapBasketEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var basket = endpoints.MapGroup("/api/basket").WithTags("Basket");

        basket.MapGet("/", GetBasket);
        basket.MapPut("/items/{productId:guid}", SetQuantity);
        basket.MapDelete("/items/{productId:guid}", RemoveItem);
        basket.MapDelete("/", ClearBasket);
        basket.MapPost("/checkout", Checkout);

        return endpoints;
    }

    private static async Task<IResult> GetBasket(
        [FromHeader(Name = KnownHeaders.CustomerId)] string? customerId, IBasketStore baskets, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(customerId))
        {
            return MissingCustomer();
        }

        return Results.Ok(BasketDto.From(await baskets.GetAsync(customerId, cancellationToken)));
    }

    private static Task<IResult> SetQuantity(
        Guid productId,
        SetQuantityRequest request,
        [FromHeader(Name = KnownHeaders.CustomerId)] string? customerId,
        IBasketStore baskets,
        TimeProvider clock,
        CancellationToken cancellationToken) =>
        ChangeAsync(customerId, baskets, cancellationToken,
            basket => basket.SetQuantity(productId, request.Quantity, clock.GetUtcNow()));

    private static Task<IResult> RemoveItem(
        Guid productId,
        [FromHeader(Name = KnownHeaders.CustomerId)] string? customerId,
        IBasketStore baskets,
        TimeProvider clock,
        CancellationToken cancellationToken) =>
        ChangeAsync(customerId, baskets, cancellationToken,
            basket => basket.SetQuantity(productId, 0, clock.GetUtcNow()));

    private static async Task<IResult> ClearBasket(
        [FromHeader(Name = KnownHeaders.CustomerId)] string? customerId, IBasketStore baskets, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(customerId))
        {
            return MissingCustomer();
        }

        await baskets.DeleteAsync(customerId, cancellationToken);
        return Results.NoContent();
    }

    private static async Task<IResult> Checkout(
        [FromHeader(Name = KnownHeaders.CustomerId)] string? customerId, BasketCheckout checkout, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(customerId))
        {
            return MissingCustomer();
        }

        var result = await checkout.CheckoutAsync(customerId, cancellationToken);
        return result.Status switch
        {
            CheckoutStatus.Accepted =>
                Results.Accepted($"/api/orders/{result.OrderId}", new CheckoutAccepted(result.OrderId!.Value)),
            CheckoutStatus.EmptyBasket =>
                Results.Problem(statusCode: StatusCodes.Status422UnprocessableEntity, title: "The basket is empty."),
            CheckoutStatus.UnknownProducts => Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["Items.ProductId"] = result.UnknownProducts!.Select(id => $"Product {id} does not exist.").ToArray(),
            }),
            _ => Results.Problem(statusCode: StatusCodes.Status503ServiceUnavailable,
                title: "Checkout is unavailable, try again shortly. Your basket has been kept."),
        };
    }

    private static async Task<IResult> ChangeAsync(
        string? customerId, IBasketStore baskets, CancellationToken cancellationToken, Action<CustomerBasket> change)
    {
        if (string.IsNullOrWhiteSpace(customerId))
        {
            return MissingCustomer();
        }

        var basket = await baskets.GetAsync(customerId, cancellationToken) ?? new CustomerBasket(customerId);
        try
        {
            change(basket);
        }
        catch (BasketDomainException ex)
        {
            return Results.Problem(statusCode: StatusCodes.Status422UnprocessableEntity, title: ex.Message);
        }

        await baskets.SaveAsync(basket, cancellationToken);
        return Results.Ok(BasketDto.From(basket));
    }

    private static IResult MissingCustomer() =>
        Results.Problem(statusCode: StatusCodes.Status401Unauthorized, title: "Missing customer identity.");
}
