namespace BuildingBlocks.Messaging.Contracts;

/// <summary>
/// A customer account was created. <paramref name="UserId"/> is the <c>sub</c> claim of the
/// user's tokens, which is also the customer id the other services see.
/// </summary>
public sealed record UserRegistered(string UserId, string Email, string DisplayName) : IIntegrationMessage;
