namespace DriveAway.Services
{
    public interface IEmailService
    {
        Task SendEmailAsync(string? toEmail, string subject, string htmlBody);
        Task SendEmailAsync(IEnumerable<string> toEmails, string subject, string htmlBody);
    }
}
