using Reviews.Api.Domain;
using Reviews.Api.Storage;

namespace Reviews.Tests;

/// <summary>
/// A store with the same contract as the MongoDB one: one review per product and customer,
/// and summaries changed only through deltas.
/// </summary>
public sealed class InMemoryReviewStore : IReviewStore
{
    private readonly object _gate = new();
    private readonly Dictionary<(Guid, string), Review> _reviews = [];
    private readonly Dictionary<Guid, RatingSummary> _summaries = [];

    public Task<Review?> FindAsync(Guid productId, string customerId, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            return Task.FromResult(_reviews.GetValueOrDefault((productId, customerId)));
        }
    }

    public Task<IReadOnlyList<Review>> ListAsync(Guid productId, int skip, int take, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            IReadOnlyList<Review> page = _reviews.Values
                .Where(r => r.ProductId == productId)
                .OrderByDescending(r => r.CreatedAt)
                .Skip(skip)
                .Take(take)
                .ToList();
            return Task.FromResult(page);
        }
    }

    public Task SaveAsync(Review review, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            _reviews[(review.ProductId, review.CustomerId)] = review;
        }

        return Task.CompletedTask;
    }

    public Task<Review?> DeleteAsync(Guid productId, string customerId, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            return Task.FromResult(_reviews.Remove((productId, customerId), out var removed) ? removed : null);
        }
    }

    public Task<RatingSummary> GetSummaryAsync(Guid productId, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            return Task.FromResult(_summaries.GetValueOrDefault(productId) ?? RatingSummary.Empty(productId));
        }
    }

    public Task ApplyToSummaryAsync(Guid productId, int? removedRating, int? addedRating, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            var current = _summaries.GetValueOrDefault(productId) ?? RatingSummary.Empty(productId);
            _summaries[productId] = current.Apply(removedRating, addedRating);
        }

        return Task.CompletedTask;
    }

    public Task<RatingSummary> RecomputeSummaryAsync(Guid productId, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            var summary = RatingSummary.From(productId, _reviews.Values.Where(r => r.ProductId == productId).Select(r => r.Rating));
            _summaries[productId] = summary;
            return Task.FromResult(summary);
        }
    }

    /// <summary>Simulates a summary that drifted, for example after a crash between two writes.</summary>
    public void CorruptSummary(Guid productId)
    {
        lock (_gate)
        {
            _summaries[productId] = RatingSummary.Empty(productId).Apply(null, 1).Apply(null, 1);
        }
    }
}
