using Microsoft.Extensions.Options;
using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;
using MongoDB.Driver;
using Reviews.Api.Domain;

namespace Reviews.Api.Storage;

public sealed class MongoOptions
{
    public const string SectionName = "Mongo";

    public string Database { get; set; } = "reviews";
}

/// <summary>
/// Reviews in the <c>reviews</c> collection (unique per product and customer) and one
/// <c>rating_summaries</c> document per product, changed with <c>$inc</c> so concurrent
/// reviews of the same product never overwrite each other's counts.
/// </summary>
public sealed class MongoReviewStore : IReviewStore
{
    private readonly IMongoCollection<ReviewDocument> _reviews;
    private readonly IMongoCollection<SummaryDocument> _summaries;

    public MongoReviewStore(IMongoClient client, IOptions<MongoOptions> options)
    {
        var database = client.GetDatabase(options.Value.Database);
        _reviews = database.GetCollection<ReviewDocument>("reviews");
        _summaries = database.GetCollection<SummaryDocument>("rating_summaries");
    }

    public async Task EnsureIndexesAsync(CancellationToken cancellationToken)
    {
        var keys = Builders<ReviewDocument>.IndexKeys;
        await _reviews.Indexes.CreateManyAsync(
            [
                new CreateIndexModel<ReviewDocument>(
                    keys.Ascending(r => r.ProductId).Ascending(r => r.CustomerId),
                    new CreateIndexOptions { Unique = true, Name = "product_customer" }),
                new CreateIndexModel<ReviewDocument>(
                    keys.Ascending(r => r.ProductId).Descending(r => r.CreatedAt),
                    new CreateIndexOptions { Name = "product_newest" }),
            ],
            cancellationToken);
    }

    public async Task<Review?> FindAsync(Guid productId, string customerId, CancellationToken cancellationToken)
    {
        var document = await _reviews.Find(r => r.ProductId == productId && r.CustomerId == customerId)
            .FirstOrDefaultAsync(cancellationToken);
        return document?.ToReview();
    }

    public async Task<IReadOnlyList<Review>> ListAsync(Guid productId, int skip, int take, CancellationToken cancellationToken)
    {
        var documents = await _reviews.Find(r => r.ProductId == productId)
            .SortByDescending(r => r.CreatedAt)
            .Skip(skip)
            .Limit(take)
            .ToListAsync(cancellationToken);
        return documents.Select(d => d.ToReview()).ToList();
    }

    public Task SaveAsync(Review review, CancellationToken cancellationToken) =>
        _reviews.ReplaceOneAsync(
            r => r.ProductId == review.ProductId && r.CustomerId == review.CustomerId,
            ReviewDocument.From(review),
            new ReplaceOptions { IsUpsert = true },
            cancellationToken);

    public async Task<Review?> DeleteAsync(Guid productId, string customerId, CancellationToken cancellationToken)
    {
        var deleted = await _reviews.FindOneAndDeleteAsync(
            r => r.ProductId == productId && r.CustomerId == customerId, cancellationToken: cancellationToken);
        return deleted?.ToReview();
    }

    public async Task<RatingSummary> GetSummaryAsync(Guid productId, CancellationToken cancellationToken)
    {
        var document = await _summaries.Find(s => s.ProductId == productId).FirstOrDefaultAsync(cancellationToken);
        return document?.ToSummary() ?? RatingSummary.Empty(productId);
    }

    public Task ApplyToSummaryAsync(Guid productId, int? removedRating, int? addedRating, CancellationToken cancellationToken)
    {
        // Net change per field. MongoDB rejects two $inc on the same field in one update, and an
        // edit that keeps the rating changes nothing at all.
        var count = (addedRating is null ? 0 : 1) - (removedRating is null ? 0 : 1);
        var total = (long)(addedRating ?? 0) - (removedRating ?? 0);
        var stars = new Dictionary<int, int>();
        if (removedRating is { } removed)
        {
            stars[removed] = stars.GetValueOrDefault(removed) - 1;
        }

        if (addedRating is { } added)
        {
            stars[added] = stars.GetValueOrDefault(added) + 1;
        }

        var update = Builders<SummaryDocument>.Update;
        var changes = new List<UpdateDefinition<SummaryDocument>>();
        if (count != 0)
        {
            changes.Add(update.Inc(s => s.Count, count));
        }

        if (total != 0)
        {
            changes.Add(update.Inc(s => s.Total, total));
        }

        changes.AddRange(stars.Where(star => star.Value != 0).Select(star => update.Inc(SummaryDocument.StarField(star.Key), star.Value)));

        if (changes.Count == 0)
        {
            return Task.CompletedTask;
        }

        return _summaries.UpdateOneAsync(
            s => s.ProductId == productId,
            update.Combine(changes),
            new UpdateOptions { IsUpsert = true },
            cancellationToken);
    }

    public async Task<RatingSummary> RecomputeSummaryAsync(Guid productId, CancellationToken cancellationToken)
    {
        var ratings = await _reviews.Find(r => r.ProductId == productId)
            .Project(r => r.Rating)
            .ToListAsync(cancellationToken);
        var summary = RatingSummary.From(productId, ratings);

        await _summaries.ReplaceOneAsync(
            s => s.ProductId == productId,
            SummaryDocument.From(summary),
            new ReplaceOptions { IsUpsert = true },
            cancellationToken);
        return summary;
    }
}

internal sealed class ReviewDocument
{
    [BsonId]
    [BsonGuidRepresentation(GuidRepresentation.Standard)]
    public Guid Id { get; set; }

    [BsonElement("productId")]
    [BsonGuidRepresentation(GuidRepresentation.Standard)]
    public Guid ProductId { get; set; }

    [BsonElement("customerId")]
    public string CustomerId { get; set; } = string.Empty;

    [BsonElement("authorName")]
    public string AuthorName { get; set; } = string.Empty;

    [BsonElement("rating")]
    public int Rating { get; set; }

    [BsonElement("title")]
    public string Title { get; set; } = string.Empty;

    [BsonElement("body")]
    public string Body { get; set; } = string.Empty;

    [BsonElement("createdAt")]
    public DateTime CreatedAt { get; set; }

    [BsonElement("updatedAt")]
    public DateTime UpdatedAt { get; set; }

    public static ReviewDocument From(Review review) => new()
    {
        Id = review.Id,
        ProductId = review.ProductId,
        CustomerId = review.CustomerId,
        AuthorName = review.AuthorName,
        Rating = review.Rating,
        Title = review.Title,
        Body = review.Body,
        CreatedAt = review.CreatedAt.UtcDateTime,
        UpdatedAt = review.UpdatedAt.UtcDateTime,
    };

    public Review ToReview() => new(
        Id, ProductId, CustomerId, AuthorName, Rating, Title, Body,
        new DateTimeOffset(DateTime.SpecifyKind(CreatedAt, DateTimeKind.Utc)),
        new DateTimeOffset(DateTime.SpecifyKind(UpdatedAt, DateTimeKind.Utc)));
}

internal sealed class SummaryDocument
{
    [BsonId]
    [BsonGuidRepresentation(GuidRepresentation.Standard)]
    public Guid ProductId { get; set; }

    [BsonElement("count")]
    public int Count { get; set; }

    [BsonElement("total")]
    public long Total { get; set; }

    [BsonElement("r1")]
    public int OneStar { get; set; }

    [BsonElement("r2")]
    public int TwoStars { get; set; }

    [BsonElement("r3")]
    public int ThreeStars { get; set; }

    [BsonElement("r4")]
    public int FourStars { get; set; }

    [BsonElement("r5")]
    public int FiveStars { get; set; }

    public static string StarField(int rating) => $"r{rating}";

    public static SummaryDocument From(RatingSummary summary) => new()
    {
        ProductId = summary.ProductId,
        Count = summary.Count,
        Total = summary.Total,
        OneStar = summary.Distribution[0],
        TwoStars = summary.Distribution[1],
        ThreeStars = summary.Distribution[2],
        FourStars = summary.Distribution[3],
        FiveStars = summary.Distribution[4],
    };

    public RatingSummary ToSummary() =>
        new(ProductId, Count, Total, [OneStar, TwoStars, ThreeStars, FourStars, FiveStars]);
}
