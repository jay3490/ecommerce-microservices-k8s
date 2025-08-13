using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Notification.Worker.Email;

namespace Notification.Tests;

public sealed class FileEmailSenderTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "notification-tests-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task Emails_are_written_as_eml_files_with_headers_and_body()
    {
        var sender = new FileEmailSender(
            Options.Create(new EmailOptions { From = "shop@test.local", PickupDirectory = _directory }),
            new FixedClock(new DateTimeOffset(2026, 4, 2, 10, 0, 0, TimeSpan.Zero)));

        await sender.SendAsync(new EmailMessage("alice@example.com", "Hello", "Line one\nLine two"), CancellationToken.None);

        var file = Assert.Single(Directory.GetFiles(_directory, "*.eml"));
        var content = await File.ReadAllTextAsync(file);
        Assert.StartsWith("From: shop@test.local\r\nTo: alice@example.com\r\nSubject: Hello\r\n", content);
        Assert.Contains("Date: Thu, 02 Apr 2026 10:00:00 GMT", content);
        Assert.EndsWith("\r\n\r\nLine one\r\nLine two", content);
    }

    [Theory]
    [InlineData("File", typeof(FileEmailSender))]
    [InlineData("Smtp", typeof(SmtpEmailSender))]
    public void The_configured_sink_decides_the_sender(string sink, Type expected)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Email:Sink"] = sink })
            .Build();
        using var services = new ServiceCollection()
            .AddSingleton(TimeProvider.System)
            .AddEmail(configuration)
            .BuildServiceProvider();

        Assert.IsType(expected, services.GetRequiredService<IEmailSender>());
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }
}
