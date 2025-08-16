using BuildingBlocks.Health;
using BuildingBlocks.Messaging;
using BuildingBlocks.Messaging.Contracts;
using BuildingBlocks.Messaging.DeadLetters;
using BuildingBlocks.Observability;
using BuildingBlocks.Outbox;
using BuildingBlocks.Persistence;
using Microsoft.EntityFrameworkCore;
using Shipping.Api.Data;
using Shipping.Api.Domain;
using Shipping.Api.Messaging;
using Shipping.Api.Shipments;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults("shipping-api");

builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddDbContext<ShippingDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("ShippingDb")));

builder.Services.Configure<ShippingRulesOptions>(builder.Configuration.GetSection(ShippingRulesOptions.SectionName));
builder.Services.AddSingleton<ShippingPolicy>();

builder.Services.AddMessaging(builder.Configuration, queueName: "shipping")
    .AddEntityFrameworkMessageStore<ShippingDbContext>()
    .AddHandler<CreateShipment, ShipmentHandlers>()
    .AddHandler<CancelShipment, ShipmentHandlers>();

builder.Services.AddProblemDetails();
builder.Services.AddHealthChecks()
    .AddDbContextCheck<ShippingDbContext>("postgres", tags: [HealthTags.Ready]);

var app = builder.Build();

app.UseExceptionHandler();
app.UseServiceDefaults();
app.MapShipmentEndpoints();
app.MapDeadLetterEndpoints();
app.MapOutboxAdminEndpoints<ShippingDbContext>();

await app.MigrateDatabaseAsync<ShippingDbContext>();
await app.RunAsync();

public partial class Program;
