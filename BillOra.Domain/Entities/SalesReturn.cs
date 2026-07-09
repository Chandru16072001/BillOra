using BillOra.Domain.Common;

namespace BillOra.Domain.Entities;

// SRS section 11 - Sales Return, with the "Load Stock" choice: whether
// returned quantity should go back into sellable inventory (Yes) or not,
// e.g. because the item is damaged (No).
public class SalesReturn : TenantEntity
{
    public int SaleId { get; set; }
    public Sale? Sale { get; set; }
    public string InvoiceNumber { get; set; } = string.Empty; // denormalized for fast search

    public int? CustomerId { get; set; }
    public Customer? Customer { get; set; }

    public DateTime ReturnDate { get; set; } = DateTime.UtcNow;
    public string? ReturnReason { get; set; }
    public bool LoadStock { get; set; } = true;
    public decimal RefundAmount { get; set; }
    public string? ProcessedByUserId { get; set; }

    public ICollection<SalesReturnItem> Items { get; set; } = new List<SalesReturnItem>();
}

public class SalesReturnItem
{
    public int Id { get; set; }
    public int SalesReturnId { get; set; }
    public SalesReturn? SalesReturn { get; set; }

    public int SaleItemId { get; set; }
    public SaleItem? SaleItem { get; set; }
    public int ItemId { get; set; }
    public Item? Item { get; set; }

    public decimal ReturnQuantity { get; set; }
    public decimal UnitPrice { get; set; }
    public decimal LineRefund { get; set; }
}
