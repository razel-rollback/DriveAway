using System.Text.RegularExpressions;

namespace DriveAway.Services
{
    /// <summary>
    /// Development email service — prints emails directly to the terminal instead of sending them.
    /// Swap ConsoleEmailService for SmtpEmailService in production via Program.cs.
    /// </summary>
    public class ConsoleEmailService : IEmailService
    {
        public Task SendEmailAsync(string? toEmail, string subject, string htmlBody)
        {
            return SendEmailAsync(new[] { toEmail ?? "" }, subject, htmlBody);
        }

        public Task SendEmailAsync(IEnumerable<string> toEmails, string subject, string htmlBody)
        {
            var recipients = toEmails.Where(e => !string.IsNullOrWhiteSpace(e)).ToList();
            var recipientDisplay = recipients.Any()
                ? string.Join(", ", recipients)
                : "[NO RECIPIENT — address was null/empty]";

            // Strip HTML tags for a clean terminal output
            var plainBody = Regex.Replace(htmlBody, "<[^>]+>", " ").Trim();
            plainBody = System.Net.WebUtility.HtmlDecode(plainBody);
            // Collapse whitespace/blank lines
            plainBody = Regex.Replace(plainBody, @"[ \t]+", " ");
            plainBody = Regex.Replace(plainBody, @"\n\s*\n+", "\n").Trim();

            var border = new string('─', 60);

            var original = Console.ForegroundColor;
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine($"\n{border}");
            Console.WriteLine($"[EMAIL] To:      {recipientDisplay}");
            Console.WriteLine($"[EMAIL] Subject: {subject}");
            Console.WriteLine(border);
            Console.ForegroundColor = ConsoleColor.White;
            Console.WriteLine(plainBody);
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine(border);
            Console.ForegroundColor = original;

            return Task.CompletedTask;
        }
    }
}
