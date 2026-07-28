using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using BillOra.Application.Common.Interfaces;
using BillOra.Domain.Entities;
using BillOra.Persistence;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using System.Net.Http;

namespace BillOra.Infrastructure.Services;

// Talks directly to Meta's WhatsApp Cloud API (graph.facebook.com) - no
// third-party BSP/SaaS in between. Two calls per send:
//   1. Upload the invoice PDF to Meta's Media endpoint -> get a media id
//   2. Send the store's approved message TEMPLATE, with that media id as
//      the document header (WhatsApp requires an approved template for any
//      business-initiated message outside the 24h customer-service window,
//      which sending an invoice right after checkout always is).
public class WhatsAppCloudApiService : IWhatsAppService
{
    private const string GraphApiBaseUrl = "https://graph.facebook.com/v20.0";

    private readonly BillOraDbContext _db;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IDataProtector _protector;

    public WhatsAppCloudApiService(BillOraDbContext db, IHttpClientFactory httpClientFactory, IDataProtectionProvider dataProtectionProvider)
    {
        _db = db;
        _httpClientFactory = httpClientFactory;
        _protector = dataProtectionProvider.CreateProtector("BillOra.WhatsAppSettings.AccessToken");
    }

    public async Task<(bool Success, string? Error)> SendInvoiceAsync(int storeId, int saleId)
    {
        var settings = await _db.WhatsAppSettingsEntries.FirstOrDefaultAsync(w => w.StoreId == storeId);
        var sale = await _db.Sales.Include(s => s.Customer).Include(s => s.SaleItems).ThenInclude(si => si.Item)
            .FirstOrDefaultAsync(s => s.Id == saleId);
        var store = await _db.Stores.FindAsync(storeId);

        if (settings == null || !settings.IsEnabled)
            return await LogAndReturn(storeId, saleId, sale?.Customer?.Phone, false, null, "WhatsApp is not configured/enabled for this store. Set it up under Settings -> WhatsApp.");

        if (sale == null || store == null)
            return await LogAndReturn(storeId, saleId, null, false, null, "Sale or store not found.");

        var rawPhone = sale.Customer?.Phone;
        if (string.IsNullOrWhiteSpace(rawPhone))
            return await LogAndReturn(storeId, saleId, rawPhone, false, null, "This sale has no customer phone number on file.");

        if (string.IsNullOrWhiteSpace(settings.PhoneNumberId) || string.IsNullOrWhiteSpace(settings.AccessTokenEncrypted))
            return await LogAndReturn(storeId, saleId, rawPhone, false, null, "WhatsApp Phone Number ID or Access Token is missing in Settings.");

        var phone = NormalizePhoneNumber(rawPhone, settings.DefaultCountryCode);
        string accessToken;
        try { accessToken = _protector.Unprotect(settings.AccessTokenEncrypted); }
        catch { return await LogAndReturn(storeId, saleId, phone, false, null, "Could not decrypt the stored access token - please re-enter it in Settings."); }

        var client = _httpClientFactory.CreateClient(nameof(WhatsAppCloudApiService));
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        try
        {
            var pdfBytes = InvoicePdfGenerator.Generate(store, sale, sale.SaleItems);

            var mediaId = await UploadMediaAsync(client, settings.PhoneNumberId, pdfBytes, $"Invoice-{sale.InvoiceNumber}.pdf");
            if (mediaId == null)
                return await LogAndReturn(storeId, saleId, phone, false, null, "Could not upload the invoice PDF to WhatsApp (media upload failed).");

            var (messageId, error) = await SendTemplateMessageAsync(client, settings, phone, mediaId, sale);
            if (messageId == null)
                return await LogAndReturn(storeId, saleId, phone, false, null, error ?? "WhatsApp rejected the message.");

            return await LogAndReturn(storeId, saleId, phone, true, messageId, null);
        }
        catch (Exception ex)
        {
            return await LogAndReturn(storeId, saleId, phone, false, null, ex.Message);
        }
    }

    private static async Task<string?> UploadMediaAsync(HttpClient client, string phoneNumberId, byte[] pdfBytes, string filename)
    {
        using var content = new MultipartFormDataContent();
        content.Add(new StringContent("whatsapp"), "messaging_product");
        content.Add(new StringContent("application/pdf"), "type");

        var fileContent = new ByteArrayContent(pdfBytes);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue("application/pdf");
        content.Add(fileContent, "file", filename);

        var response = await client.PostAsync($"{GraphApiBaseUrl}/{phoneNumberId}/media", content);
        var body = await response.Content.ReadAsStringAsync();
        if (!response.IsSuccessStatusCode) return null;

        using var doc = JsonDocument.Parse(body);
        return doc.RootElement.TryGetProperty("id", out var idProp) ? idProp.GetString() : null;
    }

    private static async Task<(string? MessageId, string? Error)> SendTemplateMessageAsync(
        HttpClient client, WhatsAppSettings settings, string phone, string mediaId, Sale sale)
    {
        var payload = new
        {
            messaging_product = "whatsapp",
            to = phone,
            type = "template",
            template = new
            {
                name = settings.TemplateName,
                language = new { code = settings.TemplateLanguageCode },
                components = new object[]
                {
                    new
                    {
                        type = "header",
                        parameters = new object[]
                        {
                            new { type = "document", document = new { id = mediaId, filename = $"Invoice-{sale.InvoiceNumber}.pdf" } }
                        }
                    },
                    new
                    {
                        type = "body",
                        parameters = new object[]
                        {
                            new { type = "text", text = sale.Customer?.Name ?? "Customer" },
                            new { type = "text", text = sale.InvoiceNumber },
                            new { type = "text", text = sale.GrandTotal.ToString("N2") }
                        }
                    }
                }
            }
        };

        var response = await client.PostAsJsonAsync($"{GraphApiBaseUrl}/{settings.PhoneNumberId}/messages", payload);
        var body = await response.Content.ReadAsStringAsync();

        if (!response.IsSuccessStatusCode)
        {
            // Surface Meta's actual error message (e.g. "template not approved",
            // "re-engagement message", invalid token) rather than a generic failure.
            try
            {
                using var errDoc = JsonDocument.Parse(body);
                var message = errDoc.RootElement.GetProperty("error").GetProperty("message").GetString();
                return (null, message ?? body);
            }
            catch { return (null, body); }
        }

        using var doc = JsonDocument.Parse(body);
        var messageId = doc.RootElement.GetProperty("messages")[0].GetProperty("id").GetString();
        return (messageId, null);
    }

    // WhatsApp expects digits only, no "+", no leading zeros, country code
    // included (e.g. "919876543210"). Falls back to DefaultCountryCode for
    // plain 10-digit local numbers, which is how most Customer records
    // will have been entered.
    private static string NormalizePhoneNumber(string rawPhone, string defaultCountryCode)
    {
        var digits = new string(rawPhone.Where(char.IsDigit).ToArray());
        if (digits.Length == 10) return defaultCountryCode + digits;
        return digits.TrimStart('0');
    }

    private async Task<(bool, string?)> LogAndReturn(int storeId, int saleId, string? phone, bool success, string? messageId, string? error)
    {
        _db.WhatsAppMessageLogs.Add(new WhatsAppMessageLog
        {
            StoreId = storeId,
            SaleId = saleId,
            PhoneNumber = phone ?? "",
            Success = success,
            WhatsAppMessageId = messageId,
            ErrorMessage = error
        });
        await _db.SaveChangesAsync();
        return (success, error);
    }
}
