namespace BillOra.Application.DTOs;

// Payload the POS billing screen posts (via AJAX) to create a sale.
public class CreateSaleRequest
{
    public int? CustomerId { get; set; }
    public decimal OverallDiscount { get; set; }
    public string? Notes { get; set; }
    public List<CreateSaleLineRequest> Lines { get; set; } = new();

    // Split Payment: one or more (mode, amount) pairs. A normal single-mode
    // sale just has one entry here. If the sum is less than the grand
    // total, the shortfall becomes the customer's outstanding balance
    // (Credit Sale) - this is also how a pure credit sale is represented:
    // an empty or partial Payments list.
    public List<PaymentLineRequest> Payments { get; set; } = new();

    // Set when checkout completes a bill that was loaded via Recall/Resume,
    // so the server can clean up the original held row instead of leaving
    // an orphaned duplicate sitting in the Held Bills list forever.
    public int? ResumedFromHeldSaleId { get; set; }
}

public class CreateSaleLineRequest
{
    public int ItemId { get; set; }
    public decimal Quantity { get; set; }
    public decimal UnitPrice { get; set; }
    public decimal Discount { get; set; }
}

public class PaymentLineRequest
{
    public int? PaymentModeId { get; set; }
    public decimal Amount { get; set; }
}

public class SaleResultDto
{
    public int SaleId { get; set; }
    public string InvoiceNumber { get; set; } = string.Empty;
    public decimal GrandTotal { get; set; }
}

// Lightweight summary for the Held Bills panel - deliberately not the full
// Sale graph, so opening that panel stays fast even with many held bills.
public class HeldBillSummaryDto
{
    public int SaleId { get; set; }
    public string InvoiceNumber { get; set; } = string.Empty;
    public string CustomerName { get; set; } = string.Empty;
    public int ItemCount { get; set; }
    public decimal GrandTotal { get; set; }
    public DateTime HeldAt { get; set; }
}

// Full detail returned when actually resuming a specific held bill, enough
// for the billing screen to rebuild its cart exactly as it was left.
public class HeldBillDetailDto
{
    public int SaleId { get; set; }
    public string InvoiceNumber { get; set; } = string.Empty;
    public int? CustomerId { get; set; }
    public decimal OverallDiscount { get; set; }
    public List<HeldBillLineDto> Lines { get; set; } = new();
}

public class HeldBillLineDto
{
    public int ItemId { get; set; }
    public string ItemName { get; set; } = string.Empty;
    public decimal Quantity { get; set; }
    public decimal UnitPrice { get; set; }
    public decimal Discount { get; set; }
    public decimal GstPercent { get; set; }
}
