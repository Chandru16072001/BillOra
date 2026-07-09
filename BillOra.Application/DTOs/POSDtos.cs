namespace BillOra.Application.DTOs;

// Payload the POS billing screen posts (via AJAX) to create a sale.
public class CreateSaleRequest
{
    public int? CustomerId { get; set; }
    public int? PaymentModeId { get; set; }
    public decimal OverallDiscount { get; set; }
    public string? Notes { get; set; }
    public List<CreateSaleLineRequest> Lines { get; set; } = new();
}

public class CreateSaleLineRequest
{
    public int ItemId { get; set; }
    public decimal Quantity { get; set; }
    public decimal UnitPrice { get; set; }
    public decimal Discount { get; set; }
}

public class SaleResultDto
{
    public int SaleId { get; set; }
    public string InvoiceNumber { get; set; } = string.Empty;
    public decimal GrandTotal { get; set; }
}
