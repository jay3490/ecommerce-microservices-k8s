using BuildingBlocks.Health;
using BuildingBlocks.Messaging;
using BuildingBlocks.Messaging.Contracts;
using BuildingBlocks.Messaging.DeadLetters;
using BuildingBlocks.Observability;
using BuildingBlocks.Outbox;
using BuildingBlocks.Persistence;
using Microsoft.EntityFrameworkCore;
using Notification.Worker.Data;
using Notification.Worker.Email;
using Notification.Worker.Messaging;

// A background worker: it consumes events and sends email. The web host is only there for
// the health endpoints the container probes use.
var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults("notification-worker");

builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddDbContext<NotificationDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("NotificationDb")));

builder.Services.AddEmail(builder.Configuration);

builder.Services.AddMessaging(builder.Configuration, queueName: "notification")
    .AddEntityFrameworkMessageStore<NotificationDbContext>()
    .AddHandler<UserRegistered, NotificationHandlers>()
    .AddHandler<OrderConfirmed, NotificationHandlers>()
    .AddHandler<OrderCancelled, NotificationHandlers>()
    .AddHandler<ShipmentDispatched, NotificationHandlers>()
    .AddHandler<ShipmentDelivered, NotificationHandlers>();

builder.Services.AddHealthChecks()
    .AddDbContextCheck<NotificationDbContext>("postgres", tags: [HealthTags.Ready]);

var app = builder.Build();

app.UseServiceDefaults();
app.MapDeadLetterEndpoints();
app.MapOutboxAdminEndpoints<NotificationDbContext>();

await app.MigrateDatabaseAsync<NotificationDbContext>();
await app.RunAsync();
