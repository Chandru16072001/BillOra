using BillOra.Domain.Common;

namespace BillOra.Domain.Entities;

// Per-store SMTP configuration for automatic invoice emailing.
// SmtpPassword is stored encrypted at rest (see EmailSettingsController/
// SettingsController using IDataProtector) - never returned to any view in plain text.
public class EmailSettings : TenantEntity
{
    public bool AutoEmailEnabled { get; set; }
    public string? SmtpHost { get; set; }
    public int SmtpPort { get; set; } = 587;
    public string? SmtpUsername { get; set; }
    public string? SmtpPasswordEncrypted { get; set; }
    public bool UseSsl { get; set; } = true;
    public string? FromEmail { get; set; }
    public string? FromName { get; set; }
}
