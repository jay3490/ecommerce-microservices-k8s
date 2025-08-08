using BuildingBlocks.Health;
using BuildingBlocks.Messaging;
using BuildingBlocks.Messaging.Contracts;
using BuildingBlocks.Observability;
using BuildingBlocks.Persistence;
using Microsoft.EntityFrameworkCore;
using Ordering.Api.Catalog;
using Ordering.Api.Data;
using Ordering.Api.Orders;
using Ordering.Api.Saga;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults("ordering-api");

builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddDbContext<OrderingDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("OrderingDb")));

builder.Services.AddMessaging(builder.Configuration, queueName: "ordering")
    .AddEntityFrameworkMessageStore<OrderingDbContext>()
    .AddHandler<BasketCheckedOut, BasketCheckoutHandler>()
    .AddHandler<OrderSubmitted, OrderSagaHandler>()
    .AddHandler<StockReserved, OrderSagaHandler>()
    .AddHandler<StockReservationFailed, OrderSagaHandler>()
    .AddHandler<PaymentSucceeded, OrderSagaHandler>()
    .AddHandler<PaymentFailed, OrderSagaHandler>()
    .AddHandler<ShipmentCreated, OrderSagaHandler>()
    .AddHandler<ShipmentCreationFailed, OrderSagaHandler>()
    .AddHandler<PaymentRefunded, OrderSagaHandler>()
    .AddHandler<StockReleased, OrderSagaHandler>();

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
