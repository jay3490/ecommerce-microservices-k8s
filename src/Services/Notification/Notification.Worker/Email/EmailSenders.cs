using System.Globalization;
using System.Net;
using System.Net.Mail;
using System.Text;
using Microsoft.Extensions.Options;

namespace Notification.Worker.Email;

public sealed record EmailMessage(string To, string Subject, string Body);

public interface IEmailSender
{
    Task SendAsync(EmailMessage message, CancellationToken cancellationToken);
}

public enum EmailSink
{
    /// <summary>Write each email as an .eml file into <see cref="EmailOptions.PickupDirectory"/>.</summary>
    File,

    /// <summary>Send through the SMTP server in <see cref="EmailOptions.Smtp"/>.</summary>
    Smtp,
}

public sealed class EmailOptions
{
    public const string SectionName = "Email";

    public EmailSink Sink { get; set; } = EmailSink.File;

    public string From { get; set; } = "shop@ecommerce.local";

    public string PickupDirectory { get; set; } = "mail";

    public SmtpOptions Smtp { get; set; } = new();
}

public sealed class SmtpOptions
{
    public string Host { get; set; } = "localhost";
    public int Port { get; set; } = 25;
    public bool EnableSsl { get; set; }
    public string? UserName { get; set; }
    public string? Password { get; set; }
}

/// <summary>
/// Writes emails as RFC 5322 files that any mail client opens. Useful locally and in tests,
/// where nothing should leave the machine.
/// </summary>
public sealed class FileEmailSender(IOptions<EmailOptions> options, TimeProvider clock) : IEmailSender
{
    public async Task SendAsync(EmailMessage message, CancellationToken cancellationToken)
    {
        var settings = options.Value;
        Directory.CreateDirectory(settings.PickupDirectory);

        var now = clock.GetUtcNow();
        var fileName = $"{now:yyyyMMddTHHmmssfff}-{Guid.NewGuid():N}.eml";
        var content = new StringBuilder()
            .Append("From: ").Append(settings.From).Append("\r\n")
            .Append("To: ").Append(message.To).Append("\r\n")
            .Append("Subject: ").Append(message.Subject).Append("\r\n")
            .Append("Date: ").Append(now.ToString("r", CultureInfo.InvariantCulture)).Append("\r\n")
            .Append("Content-Type: text/plain; charset=utf-8\r\n")
            .Append("\r\n")
            .Append(message.Body.ReplaceLineEndings("\r\n"))
            .ToString();

        await File.WriteAllTextAsync(Path.Combine(settings.PickupDirectory, fileName), content, Encoding.UTF8, cancellationToken);
    }
}

/// <summary>Sends through an SMTP relay (for example a provider's submission endpoint).</summary>
public sealed class SmtpEmailSender(IOptions<EmailOptions> options) : IEmailSender
{
    public async Task SendAsync(EmailMessage message, CancellationToken cancellationToken)
    {
        var settings = options.Value;
        using var client = new SmtpClient(settings.Smtp.Host, settings.Smtp.Port)
        {
            EnableSsl = settings.Smtp.EnableSsl,
            DeliveryMethod = SmtpDeliveryMethod.Network,
        };

        if (!string.IsNullOrEmpty(settings.Smtp.UserName))
        {
            client.Credentials = new NetworkCredential(settings.Smtp.UserName, settings.Smtp.Password);
        }

        using var mail = new MailMessage(settings.From, message.To, message.Subject, message.Body)
        {
            BodyEncoding = Encoding.UTF8,
            SubjectEncoding = Encoding.UTF8,
        };

        await client.SendMailAsync(mail, cancellationToken);
    }
}

public static class EmailServiceCollectionExtensions
{
    public static IServiceCollection AddEmail(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<EmailOptions>()
            .Bind(configuration.GetSection(EmailOptions.SectionName))
            .Validate(o => MailAddress.TryCreate(o.From, out _), "Email:From must be an email address.")
            .ValidateOnStart();

        var sink = configuration.GetValue(EmailOptions.SectionName + ":Sink", EmailSink.File);
        if (sink == EmailSink.Smtp)
        {
            services.AddSingleton<IEmailSender, SmtpEmailSender>();
        }
        else
        {
            services.AddSingleton<IEmailSender, FileEmailSender>();
        }

        return services;
    }
}
