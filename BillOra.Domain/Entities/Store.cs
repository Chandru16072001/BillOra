using BillOra.Domain.Common;

namespace BillOra.Domain.Entities;

public class Store : BaseEntity
{
    public int CompanyId { get; set; }
    public Company? Company { get; set; }

    public string Name { get; set; } = string.Empty;
    public string? OwnerName { get; set; }
    public string? GstNumber { get; set; }
    public string? PanNumber { get; set; }
    public string? BusinessType { get; set; } // e.g. Retail, Wholesale, Restaurant, Pharmacy...
    public string? FssaiNumber { get; set; }   // required for food businesses

    public string? Phone { get; set; }
    public string? AlternatePhone { get; set; }
    public string? Email { get; set; }

    public string? Address { get; set; }
    public string? District { get; set; }
    public string? Taluk { get; set; }
    public string? State { get; set; } // drives CGST+SGST vs IGST against the customer's state
    public string? Country { get; set; } = "India";
    public string? PinCode { get; set; }

    public string? LogoPath { get; set; }
    public string InvoicePrefix { get; set; } = "INV";
    public string? DefaultPrinter { get; set; }
    public string Currency { get; set; } = "INR";
    public string Timezone { get; set; } = "Asia/Kolkata";

    public bool GstEnabled { get; set; } = true;

    // Billing behavior toggles (SRS: configurable via Settings)
    public bool StockValidationEnabled { get; set; } = true;
    public bool BatchTrackingEnabled { get; set; } = false;
}
