namespace Reviews.Api.Domain;

/// <summary>A customer's review of a product. Each customer has at most one per product.</summary>
public sealed record Review(
    Guid Id,
    Guid ProductId,
    string CustomerId,
    string AuthorName,
    int Rating,
    string Title,
    string Body,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt)
{
    public const int MinRating = 1;
    public const int MaxRating = 5;
    public const int MaxTitleLength = 120;
    public const int MaxBodyLength = 4000;

    public static Dictionary<string, string[]> Validate(int rating, string? title, string? body)
    {
        var errors = new Dictionary<string, string[]>();
        if (rating is < MinRating or > MaxRating)
        {
            errors[nameof(Rating)] = [$"A rating is a whole number from {MinRating} to {MaxRating}."];
        }

        if (string.IsNullOrWhiteSpace(title) || title.Trim().Length > MaxTitleLength)
        {
            errors[nameof(Title)] = [$"A title of at most {MaxTitleLength} characters is required."];
        }

        if (body is { Length: > MaxBodyLength })
        {
            errors[nameof(Body)] = [$"The review can be at most {MaxBodyLength} characters."];
        }

        return errors;
    }
}

/// <summary>
/// Aggregate of a product's ratings: how many there are, their sum and how many of each
/// star. Kept up to date incrementally as reviews are added, changed and removed.
/// </summary>
public sealed record RatingSummary(Guid ProductId, int Count, long Total, IReadOnlyList<int> Distribution)
{
    public static RatingSummary Empty(Guid productId) => new(productId, 0, 0, new int[Review.MaxRating]);

    /// <summary>Mean rating to two decimals; 0 when there are no reviews.</summary>
    public decimal Average => Count == 0 ? 0m : Math.Round((decimal)Total / Count, 2, MidpointRounding.AwayFromZero);

    /// <summary>
    /// Applies a change: a new review (<paramref name="removed"/> null), an edited rating
    /// (both set) or a deleted review (<paramref name="added"/> null).
    /// </summary>
    public RatingSummary Apply(int? removed, int? added)
    {
        var distribution = Distribution.ToArray();
        var count = Count;
        var total = Total;

        if (removed is { } old)
        {
            distribution[old - 1]--;
            count--;
            total -= old;
        }

        if (added is { } rating)
        {
            distribution[rating - 1]++;
            count++;
            total += rating;
        }

        if (count < 0 || distribution.Any(n => n < 0))
        {
            throw new InvalidOperationException($"The rating summary of product {ProductId} would become negative.");
        }

        return this with { Count = count, Total = total, Distribution = distribution };
    }

    /// <summary>Builds a summary from scratch, used to repair one that drifted.</summary>
    public static RatingSummary From(Guid productId, IEnumerable<int> ratings) =>
        ratings.Aggregate(Empty(productId), (summary, rating) => summary.Apply(null, rating));
}
