namespace BillOra.Shared.Constants;

// The set of modules that can be individually granted/revoked per staff
// member via the Staff Master screen. Keys match the [RequireModule("...")]
// attribute values applied to each controller. The business-type-specific
// keys (Restaurant / Painting Shop) are harmless to grant on a store of the
// wrong type - the controllers themselves are gated separately by
// [RequireRestaurant] / [RequirePaintingShop].
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

    // Restaurant module
    public const string Tables = "Tables";
    public const string Reservations = "Reservations";
    public const string Orders = "Orders";
    public const string Waiters = "Waiters";

    // Painting Shop module
    public const string Shades = "Shades";
    public const string Quotations = "Quotations";



    public static readonly string[] All =
    {
        Pos, Items, Categories, Customers, Vendors, Purchases, Stock, ItemPrices, Reports, Accounts,
        Tables, Reservations, Orders, Waiters,
        Shades, Quotations
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
        [Accounts] = "Mini Accounts",
        [Tables] = "Tables (Restaurant)",
        [Reservations] = "Reservations (Restaurant)",
        [Orders] = "Orders / KOT (Restaurant)",
        [Waiters] = "Waiters (Restaurant)",
        [Shades] = "Shade / Color Master (Painting Shop)",
        [Quotations] = "Quotations (Painting Shop)"
    };
}
