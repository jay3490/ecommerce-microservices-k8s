using System.Text.Json;

namespace BuildingBlocks.Messaging;

/// <summary>
/// Marker for messages exchanged between services. The CLR type name is the wire type and
/// the routing key, so names must be unique across <see cref="Contracts"/>.
/// </summary>
public interface IIntegrationMessage;

public static class MessageType
{
    public static string NameOf<TMessage>() where TMessage : IIntegrationMessage => typeof(TMessage).Name;

    public static string NameOf(IIntegrationMessage message) => message.GetType().Name;
}

public static class MessagingJson
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);
}
