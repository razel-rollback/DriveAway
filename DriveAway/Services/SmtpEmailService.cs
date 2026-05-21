using System.Net;
using System.Net.Mail;
using Microsoft.AspNetCore.Identity.UI.Services;

namespace DriveAway.Services
{
    public class SmtpEmailService : IEmailService, IEmailSender
    {
        private readonly SmtpSettings _settings;
        private readonly ILogger<SmtpEmailService> _logger;

        public SmtpEmailService(IConfiguration configuration, ILogger<SmtpEmailService> logger)
        {
            // Securely binding configuration section to a typed object
            _settings = configuration.GetSection("Smtp").Get<SmtpSettings>()
                        ?? throw new InvalidOperationException("SMTP settings are not configured. Add an 'Smtp' section to appsettings.json.");
            _logger = logger;
        }

        public async Task SendEmailAsync(string? toEmail, string subject, string htmlBody)
        {
            await SendEmailAsync(new[] { toEmail ?? "" }, subject, htmlBody);
        }

        public async Task SendEmailAsync(IEnumerable<string> toEmails, string subject, string htmlBody)
        {
            var recipients = toEmails.Where(e => !string.IsNullOrWhiteSpace(e)).ToList();
            if (!recipients.Any())
            {
                _logger.LogWarning("Email skipped (no recipient address) — Subject: {Subject}", subject);
                return;
            }

            using var message = new MailMessage
            {
                From = new MailAddress(_settings.FromEmail, _settings.FromName),
                Subject = subject,
                Body = htmlBody,
                IsBodyHtml = true
            };

            foreach (var email in recipients)
                message.To.Add(email);

            using var client = new SmtpClient(_settings.Host, _settings.Port)
            {
                UseDefaultCredentials = false,
                Credentials = new NetworkCredential(_settings.Username, _settings.Password),
                EnableSsl = _settings.EnableSsl
            };

            try
            {
                await client.SendMailAsync(message);
                _logger.LogInformation("Email sent to {Recipients} — Subject: {Subject}", string.Join(", ", toEmails), subject);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to send email to {Recipients} — Subject: {Subject}", string.Join(", ", toEmails), subject);
                throw;
            }
        }
    }

    public class SmtpSettings
    {
        public string Host { get; set; } = string.Empty;
        public int Port { get; set; } = 587;
        public string Username { get; set; } = string.Empty;
        public string Password { get; set; } = string.Empty;
        public string FromEmail { get; set; } = string.Empty;
        public string FromName { get; set; } = "DriveAway";
        public bool EnableSsl { get; set; } = true;
    }
}
