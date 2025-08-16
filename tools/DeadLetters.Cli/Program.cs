using BuildingBlocks.Messaging;
using BuildingBlocks.Messaging.DeadLetters;
using BuildingBlocks.Messaging.RabbitMq;
using DeadLetters.Cli;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

// Operator tool for dead-lettered messages that talks to RabbitMQ directly, for when the
// services' admin endpoints are not reachable (or not running).
CommandLine commandLine;
try
{
    commandLine = CommandLine.Parse(args);
}
catch (ArgumentException ex)
{
    Console.Error.WriteLine(ex.Message);
    Console.Error.WriteLine(CommandLine.Usage);
    return 2;
}

var configuration = new ConfigurationBuilder()
    .AddInMemoryCollection(new Dictionary<string, string?>
    {
        ["RabbitMq:UserName"] = "ecommerce",
        ["RabbitMq:Password"] = "ecommerce",
    })
    .AddEnvironmentVariables()
    .Build();

var settings = configuration.GetSection(MessagingOptions.SectionName).Get<MessagingOptions>() ?? new MessagingOptions();
settings.QueueName = commandLine.Queue;
var options = Options.Create(settings);

await using var connections = new RabbitMqConnectionProvider(options, NullLogger<RabbitMqConnectionProvider>.Instance);
var service = new DeadLetterService(new RabbitMqDeadLetterQueue(connections, options));
using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(2));

switch (commandLine.Command)
{
    case DeadLetterCommand.List:
        var messages = await service.ListAsync(commandLine.Limit, timeout.Token);
        Console.WriteLine($"{messages.Count} message(s) in {service.QueueName}");
        foreach (var message in messages)
        {
            Console.WriteLine($"{message.MessageId}  {message.MessageType}  deaths={message.DeathCount} replays={message.ReplayCount}  {message.Reason}  {message.DeadLetteredAt:u}");
            Console.WriteLine($"    {message.BodyPreview}");
        }

        return 0;

    case DeadLetterCommand.Replay:
        var replayed = await service.ReplayAsync(commandLine.MessageIds, commandLine.Limit, timeout.Token);
        Console.WriteLine($"Replayed {replayed.Processed} of {replayed.Matched} matching message(s); {replayed.Failed} failed and stay dead-lettered.");
        return replayed.Failed == 0 ? 0 : 1;

    default:
        var discarded = await service.DiscardAsync(commandLine.MessageIds!, commandLine.Limit, timeout.Token);
        Console.WriteLine($"Discarded {discarded.Processed} of {discarded.Matched} matching message(s).");
        return discarded.Failed == 0 ? 0 : 1;
}
