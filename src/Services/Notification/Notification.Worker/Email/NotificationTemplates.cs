using System.Globalization;
using BuildingBlocks.Messaging.Contracts;
using Notification.Worker.Data;

namespace Notification.Worker.Email;

/// <summary>Plain-text emails for the events customers hear about.</summary>
public static class NotificationTemplates
{
    public static EmailMessage Welcome(Recipient to) => new(
        to.Email,
        "Welcome to the shop",
        $"""
        Hello {to.DisplayName},

        Your account has been created. You can now place orders and follow them from your account.
        """);

    public static EmailMessage OrderConfirmed(Recipient to, OrderConfirmed order) => new(
        to.Email,
        $"Order {ShortId(order.OrderId)} confirmed",
        $"""
        Hello {to.DisplayName},

        Thank you for your order {order.OrderId}. Your payment of {Money(order.Total, order.Currency)} was received
        and the order has been booked for shipping. We will let you know when it is on its way.
        """);

    public static EmailMessage OrderCancelled(Recipient to, OrderCancelled order) => new(
        to.Email,
        $"Order {ShortId(order.OrderId)} cancelled",
        $"""
        Hello {to.DisplayName},

        We are sorry, your order {order.OrderId} could not be completed and has been cancelled.
        Reason: {order.Reason}

        If a payment was taken it has been refunded.
        """);

    public static EmailMessage ShipmentDispatched(Recipient to, ShipmentDispatched shipment) => new(
        to.Email,
        $"Order {ShortId(shipment.OrderId)} is on its way",
        $"""
        Hello {to.DisplayName},

        Your order {shipment.OrderId} has been handed to {shipment.Carrier}.
        Tracking number: {shipment.TrackingNumber}
        """);

    public static EmailMessage ShipmentDelivered(Recipient to, ShipmentDelivered shipment) => new(
        to.Email,
        $"Order {ShortId(shipment.OrderId)} delivered",
        $"""
        Hello {to.DisplayName},

        Your order {shipment.OrderId} has been delivered. We hope you enjoy it.
        """);

    private static string ShortId(Guid id) => id.ToString("N")[..8].ToUpperInvariant();

    private static string Money(decimal amount, string currency) =>
        string.Create(CultureInfo.InvariantCulture, $"{amount:0.00} {currency}");
}
