namespace BillOra.Application.DTOs;

public class CreateSalesReturnRequest
{
    public int SaleId { get; set; }
    public string? ReturnReason { get; set; }
    public bool LoadStock { get; set; } = true;
    public List<SalesReturnLineRequest> Lines { get; set; } = new();
}

public class SalesReturnLineRequest
{
    public int SaleItemId { get; set; }
    public decimal ReturnQuantity { get; set; }
}

public class SalesReturnResultDto
{
    public int SalesReturnId { get; set; }
    public string InvoiceNumber { get; set; } = string.Empty;
    public decimal RefundAmount { get; set; }
}
