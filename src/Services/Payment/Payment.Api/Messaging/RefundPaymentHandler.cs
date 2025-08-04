using BuildingBlocks.Messaging;
using BuildingBlocks.Messaging.Contracts;
using BuildingBlocks.Outbox;
using Microsoft.EntityFrameworkCore;
using Payment.Api.Data;
using Payment.Api.Domain;

namespace Payment.Api.Messaging;

/// <summary>
/// Gives an order's money back for the saga's compensation. It always confirms with
/// <see cref="PaymentRefunded"/>, whether a refund happened, had happened already, or there
/// was never a charge; in the last case a voided record stops a late charge for the order.
/// </summary>
public sealed class RefundPaymentHandler(
    PaymentDbContext db,
    IOutbox outbox,
    TimeProvider clock,
    ILogger<RefundPaymentHandler> logger) : IMessageHandler<RefundPayment>
{
    public async Task HandleAsync(RefundPayment message, MessageContext context, CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow();
        var payment = await db.Payments.FirstOrDefaultAsync(p => p.OrderId == message.OrderId, cancellationToken);

        if (payment is null)
        {
            payment = PaymentRecord.Void(message.OrderId, message.CustomerId, message.Amount, message.Currency, message.Reason, now);
            db.Payments.Add(payment);
            logger.LogInformation("Voided the payment of order {OrderId} before it was charged: {Reason}",
                message.OrderId, message.Reason);
            outbox.Enqueue(new PaymentRefunded(message.OrderId, null));
            return;
        }

        if (payment.Refund(message.Reason, now))
        {
            logger.LogInformation("Refunded payment {PaymentId} of {Amount} {Currency} for order {OrderId}: {Reason}",
                payment.Id, payment.Amount, payment.Currency, message.OrderId, message.Reason);
        }
        else
        {
            logger.LogInformation("Payment {PaymentId} for order {OrderId} is {Status}; nothing to refund",
                payment.Id, message.OrderId, payment.Status);
        }

        outbox.Enqueue(new PaymentRefunded(message.OrderId, payment.Status == PaymentStatus.Refunded ? payment.Id : null));
    }
}
