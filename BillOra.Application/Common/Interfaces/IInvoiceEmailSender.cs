namespace BillOra.Application.Common.Interfaces;

// Sends the invoice email immediately after a bill is saved, reading the
// store's SMTP configuration (EmailSettings) at send time. Implemented in
// BillOra.Infrastructure using System.Net.Mail so no extra NuGet package
// is required; swap in MailKit later if more deliverability features are needed.
public interface IInvoiceEmailSender
{
    Task<(bool Sent, string? Error)> SendInvoiceEmailAsync(int saleId);
}
