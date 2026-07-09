namespace BillOra.Shared.Constants;

// Matches SRS section 4 - User Roles
public static class Roles
{
    public const string Developer = "Developer";   // Super Admin - manages all tenants
    public const string StoreAdmin = "StoreAdmin";  // Manages one company's stores
    public const string Manager = "Manager";        // Reports, inventory, purchase
    public const string Cashier = "Cashier";        // POS billing only

    public static readonly string[] All = { Developer, StoreAdmin, Manager, Cashier };
}
