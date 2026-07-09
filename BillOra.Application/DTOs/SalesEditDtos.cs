namespace BillOra.Application.DTOs;

// Payload for the admin-only "Modify" action on Sales Details - a full
// replacement of the sale's line items, quantities, and prices.
public class EditSaleRequest
{
    public int? CustomerId { get; set; }
    public int? PaymentModeId { get; set; }
    public decimal OverallDiscount { get; set; }
    public string? Notes { get; set; }
    public List<CreateSaleLineRequest> Lines { get; set; } = new();
}
