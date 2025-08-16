using BuildingBlocks.Health;
using BuildingBlocks.Messaging;
using BuildingBlocks.Messaging.Contracts;
using BuildingBlocks.Messaging.DeadLetters;
using BuildingBlocks.Observability;
using BuildingBlocks.Outbox;
using BuildingBlocks.Persistence;
using Inventory.Api.Data;
using Inventory.Api.Messaging;
using Inventory.Api.Stock;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults("inventory-api");

builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddDbContext<InventoryDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("InventoryDb")));

builder.Services.AddMessaging(builder.Configuration, queueName: "inventory")
    .AddEntityFrameworkMessageStore<InventoryDbContext>()
    .AddHandler<ReserveStock, StockHandlers>()
    .AddHandler<ReleaseStock, StockHandlers>()
    .AddHandler<OrderConfirmed, StockHandlers>();

builder.Services.AddProblemDetails();
builder.Services.AddHealthChecks()
    .AddDbContextCheck<InventoryDbContext>("postgres", tags: [HealthTags.Ready]);

var app = builder.Build();

app.UseExceptionHandler();
app.UseServiceDefaults();
app.MapStockEndpoints();
app.MapDeadLetterEndpoints();
app.MapOutboxAdminEndpoints<InventoryDbContext>();

await app.MigrateDatabaseAsync<InventoryDbContext>();
await app.RunAsync();
