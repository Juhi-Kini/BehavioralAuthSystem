using System.Net;
using System.Net.Mail;

namespace BehavioralAuthSystem.Services;

public class SmtpSettings
{
    public string Host { get; set; } = string.Empty;
    public int Port { get; set; } = 587;
    public string Username { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public string FromEmail { get; set; } = string.Empty;
    public string FromName { get; set; } = "BehavioralAuth";
    public bool EnableSsl { get; set; } = true;
}

public interface IEmailService
{
    Task<(bool Success, string Message)> SendPasswordResetCodeAsync(
        string recipientEmail,
        string username,
        string code);
}

public sealed class EmailService : IEmailService
{
    private readonly SmtpSettings _settings;
    private readonly ILogger<EmailService> _logger;

    public EmailService(IConfiguration configuration, ILogger<EmailService> logger)
    {
        var section = configuration.GetSection("Smtp");

        _settings = new SmtpSettings
        {
            Host = section["Host"] ?? string.Empty,
            Port = int.TryParse(section["Port"], out var port) ? port : 587,
            Username = section["Username"] ?? string.Empty,
            Password = section["Password"] ?? string.Empty,
            FromEmail = section["FromEmail"] ?? string.Empty,
            FromName = section["FromName"] ?? "BehavioralAuth",
            EnableSsl = !bool.TryParse(section["EnableSsl"], out var ssl) || ssl
        };

        _logger = logger;
    }

    public async Task<(bool Success, string Message)> SendPasswordResetCodeAsync(
        string recipientEmail,
        string username,
        string code)
    {
        if (string.IsNullOrWhiteSpace(_settings.Host) ||
            string.IsNullOrWhiteSpace(_settings.Username) ||
            string.IsNullOrWhiteSpace(_settings.Password) ||
            string.IsNullOrWhiteSpace(_settings.FromEmail))
        {
            return (false, "Email service is not configured. Add your SMTP settings in appsettings.json.");
        }

        try
        {
            using var message = new MailMessage
            {
                From = new MailAddress(_settings.FromEmail, _settings.FromName),
                Subject = "BehavioralAuth password reset code",
                IsBodyHtml = true,
                Body = $"""
                    <div style="font-family:Arial,sans-serif;line-height:1.6">
                        <h2>BehavioralAuth Password Reset</h2>
                        <p>Hello {WebUtility.HtmlEncode(username)},</p>
                        <p>Your password reset verification code is:</p>
                        <div style="font-size:28px;font-weight:700;letter-spacing:6px;padding:12px 18px;background:#f1f5f9;display:inline-block;border-radius:8px;">
                            {code}
                        </div>
                        <p>This code expires in <strong>10 minutes</strong>.</p>
                        <p>If you did not request a password reset, you can ignore this email.</p>
                    </div>
                    """
            };

            message.To.Add(recipientEmail);

            using var client = new SmtpClient(_settings.Host, _settings.Port)
            {
                EnableSsl = _settings.EnableSsl,
                Credentials = new NetworkCredential(_settings.Username, _settings.Password)
            };
            _logger.LogInformation(
    "Sending password reset OTP to {Recipient} using SMTP server {Host}:{Port} from {FromEmail}",
    recipientEmail,
    _settings.Host,
    _settings.Port,
    _settings.FromEmail);
            await client.SendMailAsync(message);
            _logger.LogInformation(
    "Password reset OTP email successfully handed to SMTP server for {Recipient}",
    recipientEmail);
            return (true, "A verification code has been sent to your email address.");
        }
        catch (SmtpException ex)
        {
            _logger.LogError(
                ex,
                "SMTP error while sending password reset email to {Recipient}",
                recipientEmail);

            return (
                false,
                $"SMTP error: {ex.Message}"
            );
        }

        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Unexpected error while sending password reset email.");

            return (
                false,
                $"Email error: {ex.Message}"
            );
        }
    }
}
