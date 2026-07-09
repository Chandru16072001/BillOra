using BillOra.Domain.Common;

namespace BillOra.Domain.Entities;

// SRS section 7 - Price Master. Kept separate from Item's own SellingPrice
// so promotional pricing (with a date window) can be scheduled without
// touching the item's standing price, and so price history is preserved.
public class ItemPrice : TenantEntity
{
    public int ItemId { get; set; }
    public Item? Item { get; set; }

    public decimal OriginalPrice { get; set; }
    public decimal SellingPrice { get; set; }
    public decimal? DiscountPrice { get; set; }
    public DateTime? OfferStartDate { get; set; }
    public DateTime? OfferEndDate { get; set; }

    public bool IsCurrentOffer =>
        DiscountPrice.HasValue &&
        (!OfferStartDate.HasValue || OfferStartDate <= DateTime.UtcNow) &&
        (!OfferEndDate.HasValue || OfferEndDate >= DateTime.UtcNow);
}
