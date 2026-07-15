using BillOra.Domain.Common;

namespace BillOra.Domain.Entities;

public class Purchase : TenantEntity
{
    public int VendorId { get; set; }
    public Vendor? Vendor { get; set; }

    public string InvoiceNumber { get; set; } = string.Empty;
    public DateTime PurchaseDate { get; set; } = DateTime.UtcNow;

    public decimal SubTotal { get; set; }
    public decimal DiscountAmount { get; set; }
    public decimal TaxAmount { get; set; }
    public decimal GrandTotal { get; set; }
    public bool IsReturned { get; set; }

    public ICollection<PurchaseItem> PurchaseItems { get; set; } = new List<PurchaseItem>();
}

public class PurchaseItem
{
    public int Id { get; set; }
    public int PurchaseId { get; set; }
    public Purchase? Purchase { get; set; }

    public int ItemId { get; set; }
    public Item? Item { get; set; }

    public decimal Quantity { get; set; }
    public decimal UnitPrice { get; set; }
    public decimal Discount { get; set; }
    public decimal GstPercent { get; set; }
    public decimal LineTotal { get; set; }

    // Populated only when the store has Batch Tracking enabled; used to
    // create the corresponding StockBatch row when the GRN is saved.
    public string? BatchNumber { get; set; }
    public DateTime? ManufactureDate { get; set; }
    public DateTime? ExpiryDate { get; set; }
    public string? SupplierBatchNumber { get; set; }
    public decimal? SellingRate { get; set; }
    public string? BatchRemarks { get; set; }
}
