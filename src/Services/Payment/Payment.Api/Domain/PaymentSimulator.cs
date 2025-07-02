using Microsoft.Extensions.Options;

namespace Payment.Api.Domain;

public sealed record PaymentDecision(bool Approved, string? DeclineReason)
{
    public static readonly PaymentDecision Approve = new(true, null);

    public static PaymentDecision Decline(string reason) => new(false, reason);
}

/// <summary>
/// Rules for the simulated payment provider. Every rule is deterministic so a given order
/// always gets the same answer, which makes the saga's failure path easy to reproduce.
/// </summary>
public sealed class PaymentSimulationOptions
{
    public const string SectionName = "PaymentSimulation";

    /// <summary>Charges strictly above this amount are declined. Null disables the rule.</summary>
    public decimal? DeclineAmountsAbove { get; set; } = 1000m;

    /// <summary>Customers whose charges are always declined.</summary>
    public string[] DeclinedCustomerIds { get; set; } = [];

    /// <summary>Currencies the provider accepts. Empty accepts any.</summary>
    public string[] SupportedCurrencies { get; set; } = ["USD"];
}

/// <summary>
/// Stands in for a real payment provider. No money moves; the decision depends only on
/// the configured rules and the charge itself.
/// </summary>
public sealed class PaymentSimulator(IOptions<PaymentSimulationOptions> options)
{
    public PaymentDecision Evaluate(string customerId, decimal amount, string currency)
    {
        var rules = options.Value;

        if (amount <= 0)
        {
            return PaymentDecision.Decline("Amount must be positive.");
        }

        if (rules.SupportedCurrencies.Length > 0
            && !rules.SupportedCurrencies.Contains(currency, StringComparer.OrdinalIgnoreCase))
        {
            return PaymentDecision.Decline($"Currency {currency} is not supported.");
        }

        if (rules.DeclinedCustomerIds.Contains(customerId, StringComparer.Ordinal))
        {
            return PaymentDecision.Decline("Card declined by issuer.");
        }

        if (amount > rules.DeclineAmountsAbove)
        {
            return PaymentDecision.Decline($"Amount exceeds the {rules.DeclineAmountsAbove:0.00} {currency} limit.");
        }

        return PaymentDecision.Approve;
    }
}
