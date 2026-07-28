using BillOra.Domain.Common;
using BillOra.Domain.Enums;

namespace BillOra.Domain.Entities;

// Everything in this file only appears in the UI when Store.BusinessType
// is "Painting Shop" - see Store.IsPaintingShop and [RequirePaintingShop].

// Shade/Color Master: a specific tinted shade of a base paint product
// (the Item). Multiple shades share one Item (e.g. "Weatherproof Emulsion 4L"
// might have 40 shade variants), which is why this isn't just fields on Item.
public class ShadeColor : TenantEntity
{
    public int ItemId { get; set; } // the paint product this shade belongs to
    public Item? Item { get; set; }

    public string ShadeCode { get; set; } = string.Empty;
    public string ShadeName { get; set; } = string.Empty;
    public string? BaseType { get; set; }     // e.g. "White Base", "Medium Base", "Deep Base", "Pastel Base"
    public string? ColorFormula { get; set; } // e.g. "R:2.5 Y:6.0 B:0.5 Bk:0.2 (ml per liter)" - tint machine recipe
    public string? HexColor { get; set; }     // swatch preview, e.g. "#E8B4B8"
    public bool IsCustomShade { get; set; }   // one-off custom mix vs a catalog shade
    public int? ReplacesShadeId { get; set; } // shade replacement tracking (discontinued -> new code)
}

// A non-committing estimate. Can include room/wall-measurement inputs per
// line (see QuotationItem) so the quoted quantity is traceable back to how
// it was calculated, not just a bare number. Converts to a real Sale
// (through the same GST/stock/accounting pipeline as POS and Orders) once approved.
public class Quotation : TenantEntity
{
    public string QuotationNumber { get; set; } = string.Empty;
    public int? CustomerId { get; set; }
    public Customer? Customer { get; set; }

    public DateTime QuotationDate { get; set; } = DateTime.UtcNow;
    public DateTime? ValidUntil { get; set; }
    public QuotationStatus Status { get; set; } = QuotationStatus.Draft;

    public decimal SubTotal { get; set; }
    public decimal DiscountAmount { get; set; }
    public bool DiscountRequiresApproval { get; set; }
    public bool DiscountApproved { get; set; }
    public string? DiscountApprovedByUserId { get; set; }
    public decimal TaxAmount { get; set; }
    public decimal GrandTotal { get; set; }

    public string? Notes { get; set; }
    public int? ConvertedSaleId { get; set; } // set once billed

    public ICollection<QuotationItem> Items { get; set; } = new List<QuotationItem>();
}

public class QuotationItem
{
    public int Id { get; set; }
    public int QuotationId { get; set; }
    public Quotation? Quotation { get; set; }

    public int ItemId { get; set; }
    public Item? Item { get; set; }
    public int? ShadeColorId { get; set; }
    public ShadeColor? ShadeColor { get; set; }

    public decimal Quantity { get; set; } // liters (or item's unit)
    public decimal UnitPrice { get; set; }
    public decimal Discount { get; set; }
    public decimal LineTotal { get; set; }

    // Room/Wall Estimator inputs - populated only when this line was built
    // via the coverage calculator rather than entered as a plain quantity.
    public string? RoomName { get; set; }
    public decimal? WallPerimeterFt { get; set; }
    public decimal? WallHeightFt { get; set; }
    public int? Doors { get; set; }
    public int? Windows { get; set; }
    public int? Coats { get; set; }
    public decimal? WastagePercent { get; set; }
    public decimal? CoverageRateUsed { get; set; } // sq ft/liter actually used for this line's math
}
