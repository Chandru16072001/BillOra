namespace BillOra.Domain.Enums;

public enum InventoryTransactionType
{
    OpeningStock,
    StockIn,
    StockOut,
    Sale,
    SaleReturn,
    Purchase,
    PurchaseReturn,
    Adjustment,
    TransferIn,
    TransferOut
}

public enum PaymentStatus
{
    Paid,
    PartiallyPaid,
    Unpaid,
    Refunded
}

public enum PrinterType
{
    Thermal80mm,
    Thermal58mm,
    A4
}

public enum SubscriptionStatus
{
    Active,
    Expired,
    Suspended,
    Disabled
}

// Mini Accounts Module
public enum TransactionDirection
{
    Credit,
    Debit
}

// Item Master - whether the entered selling price already includes GST.
public enum GstPriceType
{
    Exclusive, // GST added on top of the entered price
    Inclusive  // entered price already includes GST; GST is extracted from it
}

// ---------- Restaurant module (only active when Store.BusinessType == "Restaurant") ----------

public enum TableStatus
{
    Available,
    Occupied,
    Reserved,
    Cleaning
}

public enum ReservationStatus
{
    Booked,
    Confirmed,
    Seated,
    Cancelled,
    NoShow,
    Completed
}

public enum RestaurantOrderType
{
    DineIn,
    Takeaway,
    Delivery
}

public enum RestaurantOrderStatus
{
    Open,        // being built, nothing sent to kitchen yet
    KotSent,     // at least one round sent to the kitchen
    Served,      // kitchen has served everything
    Billed,      // converted to a Sale
    Cancelled
}

// ---------- Painting Shop module (only active when Store.BusinessType == "Painting Shop") ----------

public enum QuotationStatus
{
    Draft,
    Sent,
    Approved,
    Rejected,
    Expired,
    Converted // billed into a Sale
}

public enum CustomerType
{
    WalkIn,
    Regular,
    Contractor,
    Builder
}
