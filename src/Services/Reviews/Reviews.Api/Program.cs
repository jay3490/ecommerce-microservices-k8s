using BuildingBlocks.Health;
using BuildingBlocks.Observability;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using MongoDB.Bson;
using MongoDB.Driver;
using Reviews.Api.Catalog;
using Reviews.Api.Reviews;
using Reviews.Api.Storage;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults("reviews-api");

builder.Services.AddSingleton(TimeProvider.System);
builder.Services.Configure<MongoOptions>(builder.Configuration.GetSection(MongoOptions.SectionName));

var mongoConnectionString = builder.Configuration.GetConnectionString("ReviewsDb")
                            ?? throw new InvalidOperationException("ConnectionStrings:ReviewsDb is not configured.");
builder.Services.AddSingleton<IMongoClient>(_ => new MongoClient(mongoConnectionString));
builder.Services.AddSingleton<MongoReviewStore>();
builder.Services.AddSingleton<IReviewStore>(services => services.GetRequiredService<MongoReviewStore>());
builder.Services.AddHostedService<MongoIndexInitializer>();
builder.Services.AddScoped<ReviewService>();
builder.Services.AddProductCatalog(builder.Configuration);

builder.Services.AddProblemDetails();
builder.Services.AddHealthChecks()
    .AddCheck<MongoHealthCheck>("mongodb", tags: [HealthTags.Ready]);

var app = builder.Build();

app.UseExceptionHandler();
app.UseServiceDefaults();
app.MapReviewEndpoints();

await app.RunAsync();

public partial class Program;

/// <summary>Creates the collection indexes at startup, retrying until MongoDB is reachable.</summary>
internal sealed class MongoIndexInitializer(MongoReviewStore store, ILogger<MongoIndexInitializer> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var delay = TimeSpan.FromSeconds(1);
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await store.EnsureIndexesAsync(stoppingToken);
                logger.LogInformation("MongoDB indexes are in place");
                return;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogWarning(ex, "Could not create MongoDB indexes yet, retrying in {Delay}", delay);
                await Task.Delay(delay, stoppingToken);
                delay = TimeSpan.FromSeconds(Math.Min(delay.TotalSeconds * 2, 30));
            }
        }
    }
}

internal sealed class MongoHealthCheck(IMongoClient client) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            await client.GetDatabase("admin").RunCommandAsync<BsonDocument>(new BsonDocument("ping", 1), cancellationToken: cancellationToken);
            return HealthCheckResult.Healthy();
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy("MongoDB is unreachable", ex);
        }
    }
}
