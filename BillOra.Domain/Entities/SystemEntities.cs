using BillOra.Domain.Common;

namespace BillOra.Domain.Entities;

// Developer-portal audit trail, scoped by Company rather than Store.
public class ActivityLog
{
    public int Id { get; set; }
    public int? CompanyId { get; set; }
    public string? UserId { get; set; }
    public string Action { get; set; } = string.Empty;
    public string? Details { get; set; }
    public string? IpAddress { get; set; }
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
}

public class ApplicationSetting : TenantEntity
{
    public string Key { get; set; } = string.Empty;
    public string? Value { get; set; }
}

public class PrinterSetting : TenantEntity
{
    public Enums.PrinterType Type { get; set; }
    public string? PaperSize { get; set; }
    public int Copies { get; set; } = 1;
    public string? FooterMessage { get; set; }
    public bool IsDefault { get; set; }
}
