using BillOra.Domain.Common;
using BillOra.Domain.Enums;

namespace BillOra.Domain.Entities;

public class InventoryTransaction : TenantEntity
{
    public int ItemId { get; set; }
    public Item? Item { get; set; }

    public InventoryTransactionType TransactionType { get; set; }
    public decimal Quantity { get; set; }          // positive=in, negative=out
    public decimal BalanceAfter { get; set; }
    public DateTime TransactionDate { get; set; } = DateTime.UtcNow;
    public int? ReferenceId { get; set; }          // SaleId / PurchaseId etc.
    public string? Notes { get; set; }
}
