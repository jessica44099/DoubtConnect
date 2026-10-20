
using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Configuration;
using MimeKit;

namespace DoubtConnect.Api.Services;

public class EmailService
{
    private readonly IConfiguration _configuration;

    public EmailService(IConfiguration configuration)
    {
        _configuration = configuration;
    }

    public async Task SendEmailAsync(
        string toEmail,
        string subject,
        string htmlBody)
    {
        var smtpHost = _configuration["Brevo:SmtpHost"]
            ?? throw new InvalidOperationException("Brevo SMTP host is missing.");

        var smtpPort = int.Parse(
            _configuration["Brevo:SmtpPort"] ?? "587");

        var smtpUsername = _configuration["Brevo:SmtpUsername"]
            ?? throw new InvalidOperationException("Brevo SMTP username is missing.");

        var smtpPassword = _configuration["Brevo:SmtpPassword"]
            ?? throw new InvalidOperationException("Brevo SMTP password is missing.");

        var senderEmail = _configuration["Brevo:SenderEmail"]
            ?? throw new InvalidOperationException("Brevo sender email is missing.");

        var senderName = _configuration["Brevo:SenderName"]
            ?? "DoubtConnect";

        var message = new MimeMessage();
        message.From.Add(new MailboxAddress(senderName, senderEmail));
        message.To.Add(MailboxAddress.Parse(toEmail));
        message.Subject = subject;

        var body = new BodyBuilder
        {
            HtmlBody = htmlBody
        };

        message.Body = body.ToMessageBody();

        using var client = new SmtpClient();

        await client.ConnectAsync(
            smtpHost,
            smtpPort,
            SecureSocketOptions.StartTls);

        await client.AuthenticateAsync(smtpUsername, smtpPassword);
        await client.SendAsync(message);
        await client.DisconnectAsync(true);
    }
}
