namespace BillOra.Application.DTOs;

public class CreateOrderRequest
{
    public string OrderType { get; set; } = "DineIn"; // DineIn / Takeaway / Delivery
    public int? TableId { get; set; }
    public int? WaiterId { get; set; }
    public int? CustomerId { get; set; }
    public string? Notes { get; set; }
    public List<OrderLineRequest> Lines { get; set; } = new();
}

public class OrderLineRequest
{
    public int ItemId { get; set; }
    public decimal Quantity { get; set; }
    public string? Notes { get; set; }
}

public class OrderResultDto
{
    public int OrderId { get; set; }
    public string OrderNumber { get; set; } = string.Empty;
}

// Payload for generating the final bill(s) from an order. Lines carry an
// optional splitGroup so one order can become multiple separate Sales.
public class BillOrderRequest
{
    public int? PaymentModeId { get; set; }
    public decimal OverallDiscount { get; set; }
    public List<BillOrderLineRequest> Lines { get; set; } = new();
}

public class BillOrderLineRequest
{
    public int OrderItemId { get; set; }
    public int SplitGroup { get; set; } = 1;
}

public class BillOrderResultDto
{
    public List<int> SaleIds { get; set; } = new();
    public List<string> InvoiceNumbers { get; set; } = new();
    public decimal GrandTotal { get; set; }
}
