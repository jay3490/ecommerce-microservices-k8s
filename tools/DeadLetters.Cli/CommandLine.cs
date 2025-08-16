namespace DeadLetters.Cli;

public enum DeadLetterCommand
{
    List,
    Replay,
    Discard,
}

/// <summary>Parsed arguments: <c>&lt;command&gt; &lt;service queue&gt; [--id ID]... [--all] [--limit N]</c>.</summary>
public sealed record CommandLine(DeadLetterCommand Command, string Queue, IReadOnlySet<string>? MessageIds, int Limit)
{
    public const string Usage = """
        Usage:
          deadletters list    <queue> [--limit N]
          deadletters replay  <queue> (--all | --id <message id> [--id ...]) [--limit N]
          deadletters discard <queue> --id <message id> [--id ...] [--limit N]

        <queue> is the service's queue (ordering, inventory, payment, shipping, notification);
        its dead letters are in <queue>.dead-letter. Replayed messages go back to <queue> only.
        The broker comes from RabbitMq__Host, RabbitMq__Port, RabbitMq__UserName, RabbitMq__Password.
        """;

    public static CommandLine Parse(IReadOnlyList<string> args)
    {
        if (args.Count < 2 || !Enum.TryParse<DeadLetterCommand>(args[0], ignoreCase: true, out var command))
        {
            throw new ArgumentException("Expected a command and a queue name.");
        }

        var queue = args[1];
        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var all = false;
        var limit = 100;

        for (var i = 2; i < args.Count; i++)
        {
            switch (args[i])
            {
                case "--all":
                    all = true;
                    break;
                case "--id" when i + 1 < args.Count:
                    ids.Add(args[++i]);
                    break;
                case "--limit" when i + 1 < args.Count && int.TryParse(args[i + 1], out var parsed) && parsed > 0:
                    limit = parsed;
                    i++;
                    break;
                default:
                    throw new ArgumentException($"Unexpected argument '{args[i]}'.");
            }
        }

        switch (command)
        {
            case DeadLetterCommand.Replay when all == (ids.Count > 0):
                throw new ArgumentException("Replay needs either --all or one or more --id, not both.");
            case DeadLetterCommand.Discard when ids.Count == 0:
                throw new ArgumentException("Discard needs one or more --id.");
            case DeadLetterCommand.List when all || ids.Count > 0:
                throw new ArgumentException("List takes only --limit.");
        }

        return new CommandLine(command, queue, ids.Count > 0 ? ids : null, limit);
    }
}
