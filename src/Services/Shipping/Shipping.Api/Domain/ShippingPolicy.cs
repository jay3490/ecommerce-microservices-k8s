using System.Security.Cryptography;
using Microsoft.Extensions.Options;

namespace Shipping.Api.Domain;

/// <summary>
/// Rules of the simulated carrier. Like the payment simulator they are deterministic, so the
/// saga's shipping failure path can be reproduced on purpose.
/// </summary>
public sealed class ShippingRulesOptions
{
    public const string SectionName = "ShippingRules";

    public string Carrier { get; set; } = "Simulated Post";

    /// <summary>Orders with more units than this do not fit one parcel and are rejected.</summary>
    public int MaxUnitsPerShipment { get; set; } = 50;

    /// <summary>Customers the carrier cannot deliver to.</summary>
    public string[] UnserviceableCustomerIds { get; set; } = [];
}

public sealed record ShippingDecision(bool Accepted, string? Reason);

public sealed class ShippingPolicy(IOptions<ShippingRulesOptions> options)
{
    public string Carrier => options.Value.Carrier;

    public ShippingDecision Evaluate(string customerId, IReadOnlyCollection<ShipmentLine> lines)
    {
        var rules = options.Value;
        if (lines.Count == 0)
        {
            return new ShippingDecision(false, "The shipment has no lines.");
        }

        if (rules.UnserviceableCustomerIds.Contains(customerId, StringComparer.Ordinal))
        {
            return new ShippingDecision(false, "The carrier does not deliver to this customer's address.");
        }

        var units = lines.Sum(line => line.Quantity);
        if (units > rules.MaxUnitsPerShipment)
        {
            return new ShippingDecision(false, $"{units} units exceed the {rules.MaxUnitsPerShipment}-unit parcel limit.");
        }

        return new ShippingDecision(true, null);
    }

    /// <summary>A carrier-style tracking number: a prefix and ten random digits.</summary>
    public static string NewTrackingNumber() => $"SP{RandomNumberGenerator.GetInt32(0, int.MaxValue):D10}";
}
