using BuildingBlocks.Health;
using BuildingBlocks.Messaging;
using BuildingBlocks.Observability;
using BuildingBlocks.Persistence;
using Microsoft.EntityFrameworkCore;
using Ordering.Api.Catalog;
using Ordering.Api.Data;
using Ordering.Api.Orders;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults("ordering-api");

builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddDbContext<OrderingDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("OrderingDb")));

builder.Services.AddMessaging(builder.Configuration, queueName: "ordering")
    .AddEntityFrameworkMessageStore<OrderingDbContext>();

builder.Services.AddCatalogClient(builder.Configuration);

builder.Services.AddProblemDetails();
builder.Services.AddHealthChecks()
    .AddDbContextCheck<OrderingDbContext>("postgres", tags: [HealthTags.Ready]);

var app = builder.Build();

app.UseExceptionHandler();
app.UseServiceDefaults();
app.MapOrderEndpoints();

await app.MigrateDatabaseAsync<OrderingDbContext>();
await app.RunAsync();
