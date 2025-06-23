using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.DependencyInjection;

namespace BuildingBlocks.Messaging;

public sealed record HandlerRegistration(
    string MessageType,
    Type ClrType,
    Func<IServiceProvider, object, MessageContext, CancellationToken, Task> Invoke);

/// <summary>
/// Message types this service consumes. The consumer binds its queue to exactly these
/// routing keys and uses the registry to deserialize and dispatch each delivery.
/// </summary>
public sealed class MessageHandlerRegistry
{
    private readonly Dictionary<string, HandlerRegistration> _registrations = new(StringComparer.Ordinal);

    public IReadOnlyCollection<string> MessageTypes => _registrations.Keys;

    public void Register<TMessage>() where TMessage : class, IIntegrationMessage
    {
        var name = MessageType.NameOf<TMessage>();
        if (_registrations.ContainsKey(name))
        {
            throw new InvalidOperationException($"A handler for '{name}' is already registered.");
        }

        _registrations[name] = new HandlerRegistration(
            name,
            typeof(TMessage),
            static (services, message, context, cancellationToken) => services
                .GetRequiredService<IMessageHandler<TMessage>>()
                .HandleAsync((TMessage)message, context, cancellationToken));
    }

    public bool TryGet(string messageType, [NotNullWhen(true)] out HandlerRegistration? registration) =>
        _registrations.TryGetValue(messageType, out registration);
}
