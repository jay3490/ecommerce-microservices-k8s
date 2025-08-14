using BuildingBlocks.Http;
using Microsoft.AspNetCore.Mvc;
using Reviews.Api.Catalog;
using Reviews.Api.Domain;
using Reviews.Api.Storage;

namespace Reviews.Api.Reviews;

public sealed record ReviewDto(Guid Id, Guid ProductId, string AuthorName, int Rating, string Title, string Body,
    DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt)
{
    public static ReviewDto From(Review review) => new(
        review.Id, review.ProductId, review.AuthorName, review.Rating, review.Title, review.Body, review.CreatedAt, review.UpdatedAt);
}

public sealed record RatingSummaryDto(Guid ProductId, int Count, decimal Average, IReadOnlyDictionary<int, int> Stars)
{
    public static RatingSummaryDto From(RatingSummary summary) => new(
        summary.ProductId,
        summary.Count,
        summary.Average,
        Enumerable.Range(Review.MinRating, Review.MaxRating).ToDictionary(star => star, star => summary.Distribution[star - 1]));
}

/// <summary>
/// Reading reviews is public; writing needs the customer id the gateway sets from the token,
/// and recomputing a summary is an admin operation (enforced by the gateway route).
/// </summary>
public static class ReviewEndpoints
{
    private const int MaxPageSize = 50;

    public static IEndpointRouteBuilder MapReviewEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var reviews = endpoints.MapGroup("/api/reviews/products/{productId:guid}").WithTags("Reviews");

        reviews.MapGet("/", ListReviews);
        reviews.MapGet("/summary", GetSummary);
        reviews.MapPut("/", SaveReview);
        reviews.MapDelete("/", DeleteReview);
        reviews.MapPost("/summary/recompute", RecomputeSummary);

        return endpoints;
    }

    private static async Task<IResult> ListReviews(
        Guid productId, IReviewStore store, CancellationToken cancellationToken, int page = 1, int pageSize = 20)
    {
        page = Math.Max(page, 1);
        pageSize = Math.Clamp(pageSize, 1, MaxPageSize);
        var reviews = await store.ListAsync(productId, (page - 1) * pageSize, pageSize, cancellationToken);
        return Results.Ok(reviews.Select(ReviewDto.From));
    }

    private static async Task<IResult> GetSummary(Guid productId, IReviewStore store, CancellationToken cancellationToken) =>
        Results.Ok(RatingSummaryDto.From(await store.GetSummaryAsync(productId, cancellationToken)));

    private static async Task<IResult> SaveReview(
        Guid productId,
        ReviewInput input,
        [FromHeader(Name = KnownHeaders.CustomerId)] string? customerId,
        IProductCatalog catalog,
        ReviewService reviews,
        ILoggerFactory loggerFactory,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(customerId))
        {
            return Results.Problem(statusCode: StatusCodes.Status401Unauthorized, title: "Missing customer identity.");
        }

        var errors = Review.Validate(input.Rating, input.Title, input.Body);
        if (errors.Count > 0)
        {
            return Results.ValidationProblem(errors);
        }

        try
        {
            if (!await catalog.ExistsAsync(productId, cancellationToken))
            {
                return Results.NotFound();
            }
        }
        catch (Exception ex) when (CatalogClient.IsUnavailable(ex))
        {
            loggerFactory.CreateLogger(typeof(ReviewEndpoints)).LogWarning(ex, "Catalog unavailable while saving a review");
            return Results.Problem(statusCode: StatusCodes.Status503ServiceUnavailable, title: "The catalog is unavailable, try again shortly.");
        }

        var (review, created) = await reviews.SaveAsync(productId, customerId, input, cancellationToken);
        return created
            ? Results.Created($"/api/reviews/products/{productId}", ReviewDto.From(review))
            : Results.Ok(ReviewDto.From(review));
    }

    private static async Task<IResult> DeleteReview(
        Guid productId,
        [FromHeader(Name = KnownHeaders.CustomerId)] string? customerId,
        ReviewService reviews,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(customerId))
        {
            return Results.Problem(statusCode: StatusCodes.Status401Unauthorized, title: "Missing customer identity.");
        }

        return await reviews.DeleteAsync(productId, customerId, cancellationToken) ? Results.NoContent() : Results.NotFound();
    }

    private static async Task<IResult> RecomputeSummary(Guid productId, IReviewStore store, CancellationToken cancellationToken) =>
        Results.Ok(RatingSummaryDto.From(await store.RecomputeSummaryAsync(productId, cancellationToken)));
}
