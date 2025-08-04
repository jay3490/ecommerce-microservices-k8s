using Microsoft.EntityFrameworkCore;
using Payment.Api.Data;
using Payment.Api.Domain;

namespace Payment.Api.Payments;

public sealed record PaymentDto(
    Guid Id,
    Guid OrderId,
    decimal Amount,
    string Currency,
    string Status,
    string? DeclineReason,
    DateTimeOffset ProcessedAt,
    DateTimeOffset? RefundedAt,
    string? RefundReason)
{
    public static PaymentDto From(PaymentRecord payment) => new(
        payment.Id, payment.OrderId, payment.Amount, payment.Currency, payment.Status.ToString(),
        payment.DeclineReason, payment.ProcessedAt, payment.RefundedAt, payment.RefundReason);
}

public static class PaymentEndpoints
{
    public static IEndpointRouteBuilder MapPaymentEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/payments/{orderId:guid}", GetPayment).WithTags("Payments");
        return endpoints;
    }

    private static async Task<IResult> GetPayment(Guid orderId, PaymentDbContext db, CancellationToken cancellationToken)
    {
        var payment = await db.Payments.AsNoTracking().FirstOrDefaultAsync(p => p.OrderId == orderId, cancellationToken);
        return payment is null ? Results.NotFound() : Results.Ok(PaymentDto.From(payment));
    }
}
