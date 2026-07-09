namespace BillOra.Application.DTOs;

// Payload posted by the GRN (Goods Receipt Note) entry screen.
public class CreateGrnRequest
{
    public int VendorId { get; set; }
    public string? InvoiceNumber { get; set; }
    public decimal OverallDiscount { get; set; }
    public List<CreateGrnLineRequest> Lines { get; set; } = new();
}

public class CreateGrnLineRequest
{
    public int ItemId { get; set; }
    public decimal Quantity { get; set; }
    public decimal UnitPrice { get; set; }
    public decimal Discount { get; set; }
    public decimal GstPercent { get; set; }
}

public class GrnResultDto
{
    public int PurchaseId { get; set; }
    public string InvoiceNumber { get; set; } = string.Empty;
    public decimal GrandTotal { get; set; }
}

// Payload posted by the standalone Opening Stock Entry screen
// (for adjusting stock on items that already exist, outside of Item creation).
public class OpeningStockRequest
{
    public int ItemId { get; set; }
    public decimal Quantity { get; set; }
    public string? Notes { get; set; }
}
