using BuildingBlocks.Messaging;
using BuildingBlocks.Messaging.Contracts;
using BuildingBlocks.Outbox;
using Microsoft.EntityFrameworkCore;
using Payment.Api.Data;
using Payment.Api.Domain;

namespace Payment.Api.Messaging;

/// <summary>
/// Charges an order through the simulator and replies to the saga. An order is never
/// charged twice: a repeated command answers with the stored outcome.
/// </summary>
public sealed class ProcessPaymentHandler(
    PaymentDbContext db,
    PaymentSimulator simulator,
    IOutbox outbox,
    TimeProvider clock,
    ILogger<ProcessPaymentHandler> logger) : IMessageHandler<ProcessPayment>
{
    public async Task HandleAsync(ProcessPayment message, MessageContext context, CancellationToken cancellationToken)
    {
        var payment = await db.Payments.FirstOrDefaultAsync(p => p.OrderId == message.OrderId, cancellationToken);
        if (payment is null)
        {
            var decision = simulator.Evaluate(message.CustomerId, message.Amount, message.Currency);
            payment = PaymentRecord.From(
                message.OrderId, message.CustomerId, message.Amount, message.Currency, decision, clock.GetUtcNow());
            db.Payments.Add(payment);

            logger.LogInformation("Payment {PaymentId} for order {OrderId}: {Status} {Reason}",
                payment.Id, message.OrderId, payment.Status, payment.DeclineReason);
        }
        else
        {
            logger.LogInformation("Order {OrderId} was already charged as {PaymentId}; repeating the outcome",
                message.OrderId, payment.Id);
        }

        if (payment.Status == PaymentStatus.Succeeded)
        {
            outbox.Enqueue(new PaymentSucceeded(message.OrderId, payment.Id));
        }
        else
        {
            outbox.Enqueue(new PaymentFailed(message.OrderId, payment.DeclineReason ?? "Payment declined."));
        }
    }
}
