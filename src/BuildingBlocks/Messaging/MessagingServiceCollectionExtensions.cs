using BuildingBlocks.Health;
using BuildingBlocks.Inbox;
using BuildingBlocks.Messaging.RabbitMq;
using BuildingBlocks.Outbox;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace BuildingBlocks.Messaging;

public static class MessagingServiceCollectionExtensions
{
    /// <summary>
    /// Registers RabbitMQ publishing and consumption for a service that consumes from
    /// <paramref name="queueName"/>. Chain <see cref="MessagingBuilder.AddHandler{TMessage, THandler}"/>
    /// for each consumed message type and <see cref="MessagingBuilder.AddEntityFrameworkMessageStore{TDbContext}"/>
    /// for the outbox and inbox.
    /// </summary>
    public static MessagingBuilder AddMessaging(this IServiceCollection services, IConfiguration configuration, string queueName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(queueName);

        services.AddOptions<MessagingOptions>()
            .Bind(configuration.GetSection(MessagingOptions.SectionName))
            .Configure(options => options.QueueName = queueName);
        services.AddOptions<OutboxOptions>()
            .Bind(configuration.GetSection(OutboxOptions.SectionName));

        services.TryAddSingleton(TimeProvider.System);
        services.AddSingleton<RabbitMqConnectionProvider>();
        services.AddSingleton<IMessagePublisher, RabbitMqPublisher>();
        services.AddSingleton<MessageProcessor>();
        services.AddSingleton<OutboxSignal>();

        var registry = new MessageHandlerRegistry();
        services.AddSingleton(registry);
        services.AddHostedService<RabbitMqConsumerService>();

        services.AddHealthChecks()
            .AddCheck<RabbitMqHealthCheck>("rabbitmq", tags: [HealthTags.Ready]);

        return new MessagingBuilder(services, registry);
    }
}

public sealed class MessagingBuilder(IServiceCollection services, MessageHandlerRegistry registry)
{
    public IServiceCollection Services => services;

    public MessagingBuilder AddHandler<TMessage, THandler>()
        where TMessage : class, IIntegrationMessage
        where THandler : class, IMessageHandler<TMessage>
    {
        registry.Register<TMessage>();
        services.AddScoped<IMessageHandler<TMessage>, THandler>();
        return this;
    }

    /// <summary>
    /// Stores the outbox and inbox in <typeparamref name="TDbContext"/>, which must call
    /// <c>modelBuilder.AddMessageStore()</c>.
    /// </summary>
    public MessagingBuilder AddEntityFrameworkMessageStore<TDbContext>() where TDbContext : DbContext
    {
        services.AddScoped<IOutbox, EntityFrameworkOutbox<TDbContext>>();
        services.AddScoped<IInbox, EntityFrameworkInbox<TDbContext>>();
        services.AddScoped<OutboxDispatcher<TDbContext>>();
        services.AddHostedService<OutboxDispatcherService<TDbContext>>();
        services.AddHostedService<MessageStoreCleanupService<TDbContext>>();
        return this;
    }
}
