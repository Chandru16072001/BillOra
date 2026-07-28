using BillOra.Domain.Common;
using BillOra.Domain.Enums;

namespace BillOra.Domain.Entities;

public class Category : TenantEntity
{
    public string Name { get; set; } = string.Empty;
    public ICollection<SubCategory> SubCategories { get; set; } = new List<SubCategory>();
}

public class SubCategory : TenantEntity
{
    public int CategoryId { get; set; }
    public Category? Category { get; set; }
    public string Name { get; set; } = string.Empty;
}

public class Brand : TenantEntity
{
    public string Name { get; set; } = string.Empty;
}

public class UnitOfMeasure : TenantEntity
{
    public string Name { get; set; } = string.Empty;   // e.g. "Kilogram"
    public string ShortCode { get; set; } = string.Empty; // e.g. "Kg"
}

public class Item : TenantEntity
{
    public string Name { get; set; } = string.Empty;
    public string? ItemCode { get; set; }
    public string? Barcode { get; set; }

    public int? CategoryId { get; set; }
    public Category? Category { get; set; }
    public int? SubCategoryId { get; set; }
    public SubCategory? SubCategory { get; set; }
    public int? BrandId { get; set; }
    public Brand? Brand { get; set; }
    public int? UnitId { get; set; }
    public UnitOfMeasure? Unit { get; set; }

    public string? HsnCode { get; set; }
    public decimal GstPercent { get; set; }
    public GstPriceType PriceType { get; set; } = GstPriceType.Exclusive; // only meaningful when the store has GST enabled

    public decimal PurchasePrice { get; set; }
    public decimal SellingPrice { get; set; }
    public decimal MinSellingPrice { get; set; }

    public decimal OpeningStock { get; set; }
    public decimal CurrentStock { get; set; }
    public decimal ReorderLevel { get; set; }

    public string? ImagePath { get; set; }

	public decimal? CoverageSqFtPerLiter { get; set; }
}

public class Customer : TenantEntity
{
    public string Name { get; set; } = string.Empty;
    public string? Phone { get; set; }
    public string? Email { get; set; }
    public string? GstNumber { get; set; }
    public string? Address { get; set; }
    public string? District { get; set; }
    public string? Taluk { get; set; }
    public string? State { get; set; } // compared against Store.State to decide CGST+SGST vs IGST
    public string? Country { get; set; } = "India";
    public string? PinCode { get; set; }
    public decimal CreditLimit { get; set; }
    public decimal OutstandingAmount { get; set; }
    public int LoyaltyPoints { get; set; }
public CustomerType CustomerType { get; set; } = CustomerType.Regular;
}

public class Vendor : TenantEntity
{
    public string Name { get; set; } = string.Empty;
    public string? Phone { get; set; }
    public string? Email { get; set; }
    public string? GstNumber { get; set; }
    public string? Address { get; set; }
}

public class PaymentMode : TenantEntity
{
    public string Name { get; set; } = string.Empty; // Cash, UPI, Card, Bank, Cheque, Credit
    public bool IsCustom { get; set; }
}

public class Tax : TenantEntity
{
    public string Name { get; set; } = string.Empty; // e.g. "GST 18%"
    public decimal Percentage { get; set; }
}
