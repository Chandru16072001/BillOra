using BillOra.Domain.Common;

namespace BillOra.Domain.Entities;

// One row per store. AccessTokenEncrypted is protected at rest with
// ASP.NET Core's Data Protection API - same pattern as EmailSettings'
// SmtpPasswordEncrypted - and is never sent back to the browser once saved.
public class WhatsAppSettings : TenantEntity
{
    public string? PhoneNumberId { get; set; }              // from Meta -> WhatsApp -> API Setup
    public string? WhatsAppBusinessAccountId { get; set; }   // WABA ID, for reference / future webhook use
    public string? AccessTokenEncrypted { get; set; }        // permanent System User token
    public string TemplateName { get; set; } = "invoice_notification";
    public string TemplateLanguageCode { get; set; } = "en_US";
    public string DefaultCountryCode { get; set; } = "91";   // prefixed onto 10-digit numbers with no country code
    public bool IsEnabled { get; set; }
}

// Audit trail of every send attempt - lets you see delivery failures
// (bad number, template not approved yet, token expired, etc.) without
// digging through server logs, and prevents accidental double-sends.
public class WhatsAppMessageLog : TenantEntity
{
    public int SaleId { get; set; }
    public Sale? Sale { get; set; }
    public string PhoneNumber { get; set; } = string.Empty;
    public bool Success { get; set; }
    public string? WhatsAppMessageId { get; set; } // Meta's wamid.* if successful
    public string? ErrorMessage { get; set; }
    public DateTime SentAt { get; set; } = DateTime.UtcNow;
}
