namespace BillOra.Application.Common.Interfaces;

public interface IWhatsAppService
{
    // Generates the invoice PDF, uploads it to WhatsApp's media endpoint,
    // and sends the store's configured template message with that PDF as
    // the document header. Logs the attempt (success or failure) either way.
    Task<(bool Success, string? Error)> SendInvoiceAsync(int storeId, int saleId);
}
