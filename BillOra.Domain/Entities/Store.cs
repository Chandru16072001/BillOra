using BillOra.Domain.Common;

namespace BillOra.Domain.Entities;

public class Store : BaseEntity
{
    public int CompanyId { get; set; }
    public Company? Company { get; set; }

    public string Name { get; set; } = string.Empty;
    public string? GstNumber { get; set; }
    public string? Phone { get; set; }
    public string? Email { get; set; }
    public string? Address { get; set; }
    public string? LogoPath { get; set; }
    public string InvoicePrefix { get; set; } = "INV";
    public string? DefaultPrinter { get; set; }
    public string Currency { get; set; } = "INR";
    public string Timezone { get; set; } = "Asia/Kolkata";
    public bool GstEnabled { get; set; } = true;
}
