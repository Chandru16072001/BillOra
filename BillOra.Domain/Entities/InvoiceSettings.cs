using BillOra.Domain.Common;
using BillOra.Domain.Enums;

namespace BillOra.Domain.Entities;

// Sales Invoice Configuration - controls exactly what appears on printed
// invoices (both A4 and Thermal), one row per store.
public class InvoiceSettings : TenantEntity
{
    public string InvoiceHeading { get; set; } = "Tax Invoice"; // editable: "Tax Invoice" / "GST Invoice" / "Invoice" / "Retail Invoice" / custom

    // Store details to show
    public bool ShowStoreName { get; set; } = true;
    public bool ShowStoreAddress { get; set; } = true;
    public bool ShowStorePhone { get; set; } = true;
    public bool ShowStoreEmail { get; set; } = false;
    public bool ShowStoreGst { get; set; } = true;
    public bool ShowStoreLogo { get; set; } = true;

    // Customer details to show
    public bool ShowCustomerName { get; set; } = true;
    public bool ShowCustomerPhone { get; set; } = true;
    public bool ShowCustomerAddress { get; set; } = false;
    public bool ShowCustomerGst { get; set; } = false;

    // Content toggles
    public bool ShowGstDetails { get; set; } = true;
    public bool ShowQrCode { get; set; } = false;
    public bool ShowTermsAndConditions { get; set; } = false;
    public string? TermsAndConditionsText { get; set; }
    public string? FooterMessage { get; set; } = "Thank you for shopping with us!";

    // Print defaults
    public PrinterType DefaultPrinterType { get; set; } = PrinterType.Thermal80mm;
    public int Copies { get; set; } = 1;
    public string? PaperSize { get; set; }
}
