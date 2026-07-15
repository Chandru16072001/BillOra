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
