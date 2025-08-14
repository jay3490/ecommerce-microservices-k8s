using Reviews.Api.Domain;

namespace Reviews.Api.Storage;

/// <summary>
/// Persistence for reviews and their per-product rating summaries. The summary is updated
/// with deltas (<see cref="ApplyToSummaryAsync"/>) rather than recounted on every read.
/// </summary>
public interface IReviewStore
{
    Task<Review?> FindAsync(Guid productId, string customerId, CancellationToken cancellationToken);

    /// <summary>Newest first.</summary>
    Task<IReadOnlyList<Review>> ListAsync(Guid productId, int skip, int take, CancellationToken cancellationToken);

    /// <summary>Inserts or replaces the customer's review of the product.</summary>
    Task SaveAsync(Review review, CancellationToken cancellationToken);

    /// <returns>The deleted review, or null if there was none.</returns>
    Task<Review?> DeleteAsync(Guid productId, string customerId, CancellationToken cancellationToken);

    Task<RatingSummary> GetSummaryAsync(Guid productId, CancellationToken cancellationToken);

    /// <summary>Applies one rating change to the product's summary atomically.</summary>
    Task ApplyToSummaryAsync(Guid productId, int? removedRating, int? addedRating, CancellationToken cancellationToken);

    /// <summary>Rebuilds the product's summary from its reviews.</summary>
    Task<RatingSummary> RecomputeSummaryAsync(Guid productId, CancellationToken cancellationToken);
}
