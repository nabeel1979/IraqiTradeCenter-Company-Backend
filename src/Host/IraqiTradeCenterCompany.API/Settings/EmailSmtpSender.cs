using MailKit.Net.Smtp;
using MailKit.Security;
using MimeKit;

namespace IraqiTradeCenterCompany.API.Settings;

public interface IEmailSmtpSender
{
    Task<(bool Success, string Message, string? Detail)> SendAsync(
        EmailSettings settings,
        string toEmail,
        string subject,
        string htmlBody,
        string? plainTextBody = null,
        CancellationToken ct = default);
}

public class EmailSmtpSender : IEmailSmtpSender
{
    public async Task<(bool Success, string Message, string? Detail)> SendAsync(
        EmailSettings settings,
        string toEmail,
        string subject,
        string htmlBody,
        string? plainTextBody = null,
        CancellationToken ct = default)
    {
        if (!settings.IsEnabled)
            return (false, "البريد الإلكتروني غير مفعّل في الإعدادات", null);

        var host = settings.SmtpHost?.Trim();
        var user = settings.Username?.Trim();
        var pass = settings.AppPassword;
        var from = settings.FromEmail?.Trim() ?? user;
        if (string.IsNullOrWhiteSpace(host) || string.IsNullOrWhiteSpace(user) || string.IsNullOrWhiteSpace(pass))
            return (false, "إعدادات SMTP غير مكتملة (الخادم، المستخدم، كلمة مرور التطبيق)", null);
        if (string.IsNullOrWhiteSpace(from))
            return (false, "عنوان المرسل (From) مطلوب", null);
        if (string.IsNullOrWhiteSpace(toEmail))
            return (false, "عنوان المستلم مطلوب", null);

        var port = settings.SmtpPort is > 0 and <= 65535 ? settings.SmtpPort : 587;
        var secure = NormalizeSecurity(settings.SecurityMode, port);

        var message = new MimeMessage();
        message.From.Add(new MailboxAddress(settings.FromDisplayName?.Trim() ?? from, from));
        message.To.Add(MailboxAddress.Parse(toEmail.Trim()));
        message.Subject = subject;
        if (!string.IsNullOrWhiteSpace(settings.ReplyToEmail))
            message.ReplyTo.Add(MailboxAddress.Parse(settings.ReplyToEmail.Trim()));
        if (!string.IsNullOrWhiteSpace(plainTextBody))
        {
            var alternative = new MultipartAlternative();
            alternative.Add(new TextPart("plain") { Text = plainTextBody });
            alternative.Add(new TextPart("html") { Text = htmlBody });
            message.Body = alternative;
        }
        else
        {
            message.Body = new TextPart("html") { Text = htmlBody };
        }

        try
        {
            using var client = new SmtpClient { Timeout = 60_000 };
            await client.ConnectAsync(host, port, secure, ct);
            await client.AuthenticateAsync(user, pass, ct);
            await client.SendAsync(message, ct);
            await client.DisconnectAsync(true, ct);
            return (true, "تم إرسال الرسالة بنجاح", null);
        }
        catch (Exception ex)
        {
            var detail = ex.InnerException?.Message ?? ex.Message;
            return (false, "فشل إرسال البريد عبر SMTP", detail);
        }
    }

    private static SecureSocketOptions NormalizeSecurity(string? mode, int port)
    {
        var m = (mode ?? "").Trim();
        if (m.Equals("Ssl", StringComparison.OrdinalIgnoreCase) || port == 465)
            return SecureSocketOptions.SslOnConnect;
        if (m.Equals("StartTls", StringComparison.OrdinalIgnoreCase) || port == 587)
            return SecureSocketOptions.StartTls;
        return SecureSocketOptions.Auto;
    }
}
