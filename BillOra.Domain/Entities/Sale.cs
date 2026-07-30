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

    // GST breakdown (invoice shows these separately).
    public decimal TaxableAmount { get; set; }
    public decimal CgstAmount { get; set; }
    public decimal SgstAmount { get; set; }
    public decimal IgstAmount { get; set; }
    public bool IsInterState { get; set; } // true => IGST applied instead of CGST+SGST

    // Kept as the "primary" payment mode for backward-compatible display
    // (existing reports/print views read this directly); the authoritative
    // breakdown for split payments lives in the Payments collection below.
    public int? PaymentModeId { get; set; }
    public PaymentMode? PaymentMode { get; set; }
    public PaymentStatus PaymentStatus { get; set; } = PaymentStatus.Paid;
    public decimal AmountPaid { get; set; }   // sum of Payments - may be less than GrandTotal for a credit/partial sale

    public string? Notes { get; set; }
    public bool IsHeld { get; set; }       // "hold bill" support
    public bool IsReturned { get; set; }
    public int PrintCount { get; set; }    // 0 = never printed; >1 print shows a "Duplicate Copy" watermark

    // Restaurant module - populated only when the sale originated from an
    // Order (see RestaurantOrder). Stored as snapshots rather than foreign
    // keys so a printed/emailed invoice stays accurate even if the table
    // is later renamed or the order record is cleaned up.
    public string? TableNumber { get; set; }
    public string? WaiterName { get; set; }
    public string? OrderNumber { get; set; }
    public string? OrderType { get; set; } // "Dine-in" / "Takeaway" / "Delivery"

    public ICollection<SaleItem> SaleItems { get; set; } = new List<SaleItem>();
    public ICollection<SalePayment> Payments { get; set; } = new List<SalePayment>();
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
    public GstPriceType PriceType { get; set; } = GstPriceType.Exclusive;

    public decimal TaxableValue { get; set; }
    public decimal TaxAmount { get; set; }   // = CgstAmount + SgstAmount + IgstAmount
    public decimal CgstAmount { get; set; }
    public decimal SgstAmount { get; set; }
    public decimal IgstAmount { get; set; }
    public decimal LineTotal { get; set; }

    // Batch tracking (only populated when the store has batch tracking on).
    public string? BatchInfo { get; set; }
}

// Split Payment support: one row per payment mode used on a sale
// (e.g. Cash 500 + UPI 300 on a 800 bill). A normal single-payment sale
// still gets exactly one row here, so all reporting can read from this
// table uniformly instead of branching on "is this split or not."
public class SalePayment
{
    public int Id { get; set; }
    public int SaleId { get; set; }
    public Sale? Sale { get; set; }

    public int? PaymentModeId { get; set; }
    public PaymentMode? PaymentMode { get; set; }
    public decimal Amount { get; set; }
}
