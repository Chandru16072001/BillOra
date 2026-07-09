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
