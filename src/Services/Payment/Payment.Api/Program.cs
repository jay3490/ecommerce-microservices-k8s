using BuildingBlocks.Health;
using BuildingBlocks.Messaging;
using BuildingBlocks.Messaging.Contracts;
using BuildingBlocks.Observability;
using BuildingBlocks.Persistence;
using Microsoft.EntityFrameworkCore;
using Payment.Api.Data;
using Payment.Api.Domain;
using Payment.Api.Messaging;
using Payment.Api.Payments;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults("payment-api");

builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddDbContext<PaymentDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("PaymentDb")));

builder.Services.Configure<PaymentSimulationOptions>(builder.Configuration.GetSection(PaymentSimulationOptions.SectionName));
builder.Services.AddSingleton<PaymentSimulator>();

builder.Services.AddMessaging(builder.Configuration, queueName: "payment")
    .AddEntityFrameworkMessageStore<PaymentDbContext>()
    .AddHandler<ProcessPayment, ProcessPaymentHandler>()
    .AddHandler<RefundPayment, RefundPaymentHandler>();

builder.Services.AddProblemDetails();
builder.Services.AddHealthChecks()
    .AddDbContextCheck<PaymentDbContext>("postgres", tags: [HealthTags.Ready]);

var app = builder.Build();

app.UseExceptionHandler();
app.UseServiceDefaults();
app.MapPaymentEndpoints();

await app.MigrateDatabaseAsync<PaymentDbContext>();
await app.RunAsync();
