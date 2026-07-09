using System.Net;
using System.Net.Mail;
using BillOra.Application.Common.Interfaces;
using BillOra.Persistence;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;

namespace BillOra.Infrastructure.Services;

// Sends invoice emails via each store's own configured SMTP account
// (Settings -> Email Configuration). Uses System.Net.Mail so no extra NuGet
// package is required. The stored SMTP password is encrypted at rest with
// ASP.NET Core's Data Protection API (see EmailPasswordProtector).
public class SmtpEmailSender : IEmailSender
{
    private readonly BillOraDbContext _db;
    private readonly IDataProtector _protector;

    public SmtpEmailSender(BillOraDbContext db, IDataProtectionProvider dataProtectionProvider)
    {
        _db = db;
        _protector = dataProtectionProvider.CreateProtector("BillOra.EmailSettings.SmtpPassword");
    }

    public async Task<(bool Success, string? Error)> SendInvoiceEmailAsync(int storeId, string toEmail, string subject, string htmlBody)
    {
        try
        {
            var settings = await _db.EmailSettingsEntries.FirstOrDefaultAsync(e => e.StoreId == storeId);
            if (settings == null || !settings.AutoEmailEnabled)
                return (false, "Automatic email sending is not enabled for this store.");

            if (string.IsNullOrWhiteSpace(settings.SmtpHost) || string.IsNullOrWhiteSpace(settings.SmtpUsername) || string.IsNullOrWhiteSpace(settings.SmtpPasswordEncrypted))
                return (false, "SMTP is not fully configured in Settings.");

            var password = _protector.Unprotect(settings.SmtpPasswordEncrypted);

            using var client = new SmtpClient(settings.SmtpHost, settings.SmtpPort)
            {
                Credentials = new NetworkCredential(settings.SmtpUsername, password),
                EnableSsl = settings.UseSsl
            };

            using var message = new MailMessage
            {
                From = new MailAddress(settings.FromEmail ?? settings.SmtpUsername, settings.FromName ?? "BillOra"),
                Subject = subject,
                Body = htmlBody,
                IsBodyHtml = true
            };
            message.To.Add(toEmail);

            await client.SendMailAsync(message);
            return (true, null);
        }
        catch (Exception ex)
        {
            return (false, ex.Message);
        }
    }

    // Called by SettingsController when saving the SMTP password.
    public string EncryptPassword(string plainText) => _protector.Protect(plainText);
}
