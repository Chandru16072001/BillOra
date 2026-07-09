using BillOra.Domain.Common;
using BillOra.Domain.Enums;

namespace BillOra.Domain.Entities;

public class Sale : TenantEntity
{
    public string InvoiceNumber { get; set; } = string.Empty;
    public int? CustomerId { get; set; }          // null => walk-in customer
    public Customer? Customer { get; set; }
    public string CashierUserId { get; set; } = string.Empty;

    public DateTime SaleDate { get; set; } = DateTime.UtcNow;

    public decimal SubTotal { get; set; }
    public decimal DiscountAmount { get; set; }
    public decimal TaxAmount { get; set; }
    public decimal RoundOff { get; set; }
    public decimal GrandTotal { get; set; }

    public int? PaymentModeId { get; set; }
    public PaymentMode? PaymentMode { get; set; }
    public PaymentStatus PaymentStatus { get; set; } = PaymentStatus.Paid;

    public string? Notes { get; set; }
    public bool IsHeld { get; set; }       // "hold bill" support
    public bool IsReturned { get; set; }

    public ICollection<SaleItem> SaleItems { get; set; } = new List<SaleItem>();
}

public class SaleItem
{
    public int Id { get; set; }
    public int SaleId { get; set; }
    public Sale? Sale { get; set; }

    public int ItemId { get; set; }
    public Item? Item { get; set; }

    public decimal Quantity { get; set; }
    public decimal UnitPrice { get; set; }
    public decimal Discount { get; set; }
    public decimal GstPercent { get; set; }
    public decimal TaxAmount { get; set; }
    public decimal LineTotal { get; set; }
}
