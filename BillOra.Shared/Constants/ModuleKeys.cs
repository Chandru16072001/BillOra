namespace BillOra.Shared.Constants;

// The set of modules that can be individually granted/revoked per staff
// member via the Staff Master screen. Keys match the [RequireModule("...")]
// attribute values applied to each controller.
public static class ModuleKeys
{
    public const string Pos = "POS";
    public const string Items = "Items";
    public const string Categories = "Categories";
    public const string Customers = "Customers";
    public const string Vendors = "Vendors";
    public const string Purchases = "Purchases";
    public const string Stock = "Stock";
    public const string ItemPrices = "ItemPrices";
    public const string Reports = "Reports";
    public const string Accounts = "Accounts";

    public static readonly string[] All =
    {
        Pos, Items, Categories, Customers, Vendors, Purchases, Stock, ItemPrices, Reports, Accounts
    };

    public static readonly Dictionary<string, string> DisplayNames = new()
    {
        [Pos] = "POS Billing",
        [Items] = "Items",
        [Categories] = "Categories",
        [Customers] = "Customers",
        [Vendors] = "Vendors",
        [Purchases] = "GRN / Purchases",
        [Stock] = "Opening Stock",
        [ItemPrices] = "Price Master",
        [Reports] = "Reports",
        [Accounts] = "Mini Accounts"
    };
}
