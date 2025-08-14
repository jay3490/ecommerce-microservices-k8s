using Reviews.Api.Domain;
using Reviews.Api.Storage;

namespace Reviews.Api.Reviews;

public sealed record ReviewInput(int Rating, string? Title, string? Body, string? AuthorName);

/// <summary>
/// Writes reviews and keeps the product's rating summary in step. The review and the summary
/// are two writes without a transaction (MongoDB transactions need a replica set); if the
/// process dies between them the summary is off by one review until
/// <see cref="IReviewStore.RecomputeSummaryAsync"/> repairs it.
/// </summary>
public sealed class ReviewService(IReviewStore store, TimeProvider clock)
{
    /// <returns>The saved review and whether it was new.</returns>
    public async Task<(Review Review, bool Created)> SaveAsync(
        Guid productId, string customerId, ReviewInput input, CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow();
        var existing = await store.FindAsync(productId, customerId, cancellationToken);
        var review = new Review(
            existing?.Id ?? Guid.NewGuid(),
            productId,
            customerId,
            string.IsNullOrWhiteSpace(input.AuthorName) ? "Customer" : input.AuthorName.Trim(),
            input.Rating,
            input.Title!.Trim(),
            input.Body?.Trim() ?? string.Empty,
            existing?.CreatedAt ?? now,
            now);

        await store.SaveAsync(review, cancellationToken);
        await store.ApplyToSummaryAsync(productId, existing?.Rating, review.Rating, cancellationToken);
        return (review, existing is null);
    }

    public async Task<bool> DeleteAsync(Guid productId, string customerId, CancellationToken cancellationToken)
    {
        var deleted = await store.DeleteAsync(productId, customerId, cancellationToken);
        if (deleted is null)
        {
            return false;
        }

        await store.ApplyToSummaryAsync(productId, deleted.Rating, null, cancellationToken);
        return true;
    }
}
