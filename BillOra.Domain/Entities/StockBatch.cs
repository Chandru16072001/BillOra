using BillOra.Domain.Common;

namespace BillOra.Domain.Entities;

// Only used when Store.BatchTrackingEnabled is true. One row per batch
// received (via GRN or Opening Stock). Sales draw down RemainingQuantity
// using nearest-expiry-first (falling back to FIFO by CreatedAt for batches
// without an expiry date).
public class StockBatch : TenantEntity
{
    public int ItemId { get; set; }
    public Item? Item { get; set; }

    public string BatchNumber { get; set; } = string.Empty;
    public DateTime? ManufactureDate { get; set; }
    public DateTime? ExpiryDate { get; set; }
    public string? SupplierBatchNumber { get; set; }

    public decimal PurchaseRate { get; set; }
    public decimal SellingRate { get; set; }

    public decimal Quantity { get; set; }          // originally received
    public decimal RemainingQuantity { get; set; } // decremented as sold

    public string? Remarks { get; set; }
    public string? SourceModule { get; set; } // "GRN" or "OpeningStock"
    public int? SourceId { get; set; }
}
