namespace BillOra.Application.Common.Interfaces;

public interface IEmailSender
{
    // Returns (success, errorMessage). Never throws - callers (e.g. POS
    // checkout) shouldn't fail a sale just because an email couldn't send.
    Task<(bool Success, string? Error)> SendInvoiceEmailAsync(int storeId, string toEmail, string subject, string htmlBody);

    // Used by SettingsController to encrypt the SMTP password before saving.
    string EncryptPassword(string plainText);
}
