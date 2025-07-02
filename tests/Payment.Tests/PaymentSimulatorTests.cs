using Microsoft.Extensions.Options;
using Payment.Api.Domain;

namespace Payment.Tests;

public sealed class PaymentSimulatorTests
{
    private readonly PaymentSimulationOptions _rules = new()
    {
        DeclineAmountsAbove = 1000m,
        DeclinedCustomerIds = ["blocked-customer"],
        SupportedCurrencies = ["USD"],
    };

    [Theory]
    [InlineData(0.01)]
    [InlineData(999.99)]
    [InlineData(1000)]
    public void Charges_up_to_the_limit_are_approved(decimal amount) =>
        Assert.True(Simulator().Evaluate("customer-1", amount, "USD").Approved);

    [Fact]
    public void Charges_above_the_limit_are_declined()
    {
        var decision = Simulator().Evaluate("customer-1", 1000.01m, "USD");

        Assert.False(decision.Approved);
        Assert.Contains("limit", decision.DeclineReason);
    }

    [Fact]
    public void Configured_customers_are_always_declined() =>
        Assert.False(Simulator().Evaluate("blocked-customer", 10m, "USD").Approved);

    [Fact]
    public void Unsupported_currencies_are_declined() =>
        Assert.False(Simulator().Evaluate("customer-1", 10m, "EUR").Approved);

    [Fact]
    public void The_amount_rule_can_be_disabled()
    {
        _rules.DeclineAmountsAbove = null;

        Assert.True(Simulator().Evaluate("customer-1", 1_000_000m, "USD").Approved);
    }

    [Fact]
    public void The_same_charge_always_gets_the_same_answer()
    {
        var simulator = Simulator();

        var answers = Enumerable.Range(0, 5).Select(_ => simulator.Evaluate("customer-1", 1249m, "USD")).Distinct();

        Assert.Single(answers);
    }

    private PaymentSimulator Simulator() => new(Options.Create(_rules));
}
