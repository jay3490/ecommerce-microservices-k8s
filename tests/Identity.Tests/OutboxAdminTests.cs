using System.Net.Http.Json;
using System.Text.Json;
using BuildingBlocks.Outbox;

namespace Identity.Tests;

public sealed class OutboxAdminTests : IDisposable
{
    private readonly IdentityFactory _factory = new();

    [Fact]
    public async Task Messages_the_dispatcher_gave_up_on_are_listed_and_can_be_retried()
    {
        using var client = _factory.CreateClient();
        var stuckId = Guid.NewGuid();
        await using (var db = _factory.CreateDbContext())
        {
            db.Set<OutboxMessage>().Add(new OutboxMessage
            {
                Id = stuckId,
                Type = "UserRegistered",
                Payload = """{"userId":"u-1","email":"u1@example.com","displayName":"U1"}""",
                OccurredOnUtc = DateTime.UtcNow,
                Attempts = 10,
                LastError = "NO_ROUTE",
            });
            await db.SaveChangesAsync();
        }

        var failed = await client.GetFromJsonAsync<List<StuckOutboxMessage>>("/admin/outbox/failed");
        Assert.Equal("NO_ROUTE", Assert.Single(failed!, m => m.Id == stuckId).LastError);

        var retry = await client.PostAsync("/admin/outbox/failed/retry", null);
        var body = await retry.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(1, body.GetProperty("reset").GetInt32());

        // The dispatcher (polling every 100 ms in the test host) now publishes it.
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (!_factory.Published.Messages.Any(m => m.MessageId == stuckId) && DateTime.UtcNow < deadline)
        {
            await Task.Delay(50);
        }

        Assert.Contains(_factory.Published.Messages, m => m.MessageId == stuckId);
        Assert.Empty((await client.GetFromJsonAsync<List<StuckOutboxMessage>>("/admin/outbox/failed"))!);
    }

    public void Dispose() => _factory.Dispose();
}
