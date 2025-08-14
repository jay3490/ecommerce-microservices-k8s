using Reviews.Api.Domain;
using Reviews.Api.Reviews;

namespace Reviews.Tests;

public sealed class RatingSummaryTests
{
    private static readonly Guid Product = Guid.NewGuid();

    [Fact]
    public void An_empty_summary_has_no_average()
    {
        var summary = RatingSummary.Empty(Product);

        Assert.Equal(0, summary.Count);
        Assert.Equal(0m, summary.Average);
        Assert.Equal([0, 0, 0, 0, 0], summary.Distribution);
    }

    [Fact]
    public void Adding_ratings_updates_count_average_and_distribution()
    {
        var summary = RatingSummary.From(Product, [5, 4, 4, 1]);

        Assert.Equal(4, summary.Count);
        Assert.Equal(3.5m, summary.Average);
        Assert.Equal([1, 0, 0, 2, 1], summary.Distribution);
    }

    [Fact]
    public void Averages_are_rounded_to_two_decimals()
    {
        Assert.Equal(4.67m, RatingSummary.From(Product, [5, 5, 4]).Average);
    }

    [Fact]
    public void Editing_a_rating_moves_it_between_stars_without_changing_the_count()
    {
        var summary = RatingSummary.From(Product, [2, 5]).Apply(removed: 2, added: 4);

        Assert.Equal(2, summary.Count);
        Assert.Equal(4.5m, summary.Average);
        Assert.Equal([0, 0, 0, 1, 1], summary.Distribution);
    }

    [Fact]
    public void Removing_a_rating_that_was_never_counted_is_refused()
    {
        Assert.Throws<InvalidOperationException>(() => RatingSummary.Empty(Product).Apply(removed: 3, added: null));
    }

    [Theory]
    [InlineData(0, "Great", "Rating")]
    [InlineData(6, "Great", "Rating")]
    [InlineData(3, "", "Title")]
    public void Invalid_reviews_are_reported_per_field(int rating, string title, string field)
    {
        Assert.True(Review.Validate(rating, title, null).ContainsKey(field));
    }

    [Fact]
    public async Task The_service_keeps_the_summary_in_step_with_creates_edits_and_deletes()
    {
        var store = new InMemoryReviewStore();
        var service = new ReviewService(store, TimeProvider.System);

        var (_, created) = await service.SaveAsync(Product, "alice", new ReviewInput(5, "Great", null, "Alice"), CancellationToken.None);
        await service.SaveAsync(Product, "bob", new ReviewInput(3, "Fine", null, "Bob"), CancellationToken.None);
        var (edited, createdAgain) = await service.SaveAsync(Product, "bob", new ReviewInput(4, "Better", "Grew on me", "Bob"), CancellationToken.None);
        await service.DeleteAsync(Product, "alice", CancellationToken.None);

        Assert.True(created);
        Assert.False(createdAgain);
        Assert.Equal("Better", edited.Title);
        var summary = await store.GetSummaryAsync(Product, CancellationToken.None);
        Assert.Equal((1, 4m), (summary.Count, summary.Average));
        Assert.Equal([0, 0, 0, 1, 0], summary.Distribution);
    }

    [Fact]
    public async Task Editing_keeps_the_original_creation_time()
    {
        var store = new InMemoryReviewStore();
        var clock = new StepClock(new DateTimeOffset(2026, 4, 2, 10, 0, 0, TimeSpan.Zero));
        var service = new ReviewService(store, clock);

        var (first, _) = await service.SaveAsync(Product, "alice", new ReviewInput(5, "Great", null, null), CancellationToken.None);
        var (second, _) = await service.SaveAsync(Product, "alice", new ReviewInput(4, "Good", null, null), CancellationToken.None);

        Assert.Equal(first.Id, second.Id);
        Assert.Equal(first.CreatedAt, second.CreatedAt);
        Assert.True(second.UpdatedAt > first.UpdatedAt);
        Assert.Equal("Customer", second.AuthorName);
    }

    private sealed class StepClock(DateTimeOffset start) : TimeProvider
    {
        private DateTimeOffset _now = start;

        public override DateTimeOffset GetUtcNow() => _now = _now.AddMinutes(1);
    }
}
