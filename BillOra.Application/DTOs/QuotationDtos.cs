namespace BillOra.Application.DTOs;

public class CreateQuotationRequest
{
    public int? CustomerId { get; set; }
    public DateTime? ValidUntil { get; set; }
    public string? Notes { get; set; }
    public List<QuotationLineRequest> Lines { get; set; } = new();
}

public class QuotationLineRequest
{
    public int ItemId { get; set; }
    public int? ShadeColorId { get; set; }
    public decimal Quantity { get; set; }
    public decimal UnitPrice { get; set; }
    public decimal Discount { get; set; }

    // Present only if this line came from the Room/Wall Estimator.
    public string? RoomName { get; set; }
    public decimal? WallPerimeterFt { get; set; }
    public decimal? WallHeightFt { get; set; }
    public int? Doors { get; set; }
    public int? Windows { get; set; }
    public int? Coats { get; set; }
    public decimal? WastagePercent { get; set; }
    public decimal? CoverageRateUsed { get; set; }
}

public class QuotationResultDto
{
    public int QuotationId { get; set; }
    public string QuotationNumber { get; set; } = string.Empty;
    public bool RequiresDiscountApproval { get; set; }
}

public class ConvertQuotationRequest
{
    public int? PaymentModeId { get; set; }
}

public class ConvertQuotationResultDto
{
    public int SaleId { get; set; }
    public string InvoiceNumber { get; set; } = string.Empty;
    public decimal GrandTotal { get; set; }
}
