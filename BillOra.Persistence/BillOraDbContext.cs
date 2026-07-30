using BillOra.Application.Common.Interfaces;
using BillOra.Domain.Common;
using BillOra.Domain.Entities;
using BillOra.Persistence.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace BillOra.Persistence;

public class BillOraDbContext : IdentityDbContext<ApplicationUser>
{
    private readonly ICurrentTenantService? _tenant;

    public BillOraDbContext(DbContextOptions<BillOraDbContext> options) : base(options) { }

    public BillOraDbContext(DbContextOptions<BillOraDbContext> options, ICurrentTenantService tenant)
        : base(options)
    {
        _tenant = tenant;
    }

    public DbSet<Company> Companies => Set<Company>();
    public DbSet<Store> Stores => Set<Store>();

    public DbSet<Category> Categories => Set<Category>();
    public DbSet<SubCategory> SubCategories => Set<SubCategory>();
    public DbSet<Brand> Brands => Set<Brand>();
    public DbSet<UnitOfMeasure> Units => Set<UnitOfMeasure>();
    public DbSet<Item> Items => Set<Item>();
public DbSet<ShadeColor> ShadeColors => Set<ShadeColor>();

public DbSet<Quotation> Quotations => Set<Quotation>();

public DbSet<QuotationItem> QuotationItems => Set<QuotationItem>();
    public DbSet<ItemPrice> ItemPrices => Set<ItemPrice>();
    public DbSet<Customer> Customers => Set<Customer>();
    public DbSet<Vendor> Vendors => Set<Vendor>();
    public DbSet<PaymentMode> PaymentModes => Set<PaymentMode>();
    public DbSet<Tax> Taxes => Set<Tax>();

    public DbSet<Sale> Sales => Set<Sale>();
    public DbSet<SaleItem> SaleItems => Set<SaleItem>();
    public DbSet<Purchase> Purchases => Set<Purchase>();
    public DbSet<PurchaseItem> PurchaseItems => Set<PurchaseItem>();
    public DbSet<InventoryTransaction> InventoryTransactions => Set<InventoryTransaction>();
    public DbSet<StockBatch> StockBatches => Set<StockBatch>();
    public DbSet<AccountTransaction> AccountTransactions => Set<AccountTransaction>();
    public DbSet<SalesReturn> SalesReturns => Set<SalesReturn>();
    public DbSet<SalesReturnItem> SalesReturnItems => Set<SalesReturnItem>();

    public DbSet<ActivityLog> ActivityLogs => Set<ActivityLog>();
    public DbSet<ApplicationSetting> ApplicationSettings => Set<ApplicationSetting>();
    public DbSet<PrinterSetting> PrinterSettings => Set<PrinterSetting>();
    public DbSet<EmailSettings> EmailSettingsEntries => Set<EmailSettings>();
    public DbSet<InvoiceSettings> InvoiceSettingsEntries => Set<InvoiceSettings>();
    public DbSet<StaffModulePermission> StaffModulePermissions => Set<StaffModulePermission>();

 public DbSet<DiningTable> DiningTables => Set<DiningTable>();
    public DbSet<TableReservation> TableReservations => Set<TableReservation>();
    public DbSet<Waiter> Waiters => Set<Waiter>();
    public DbSet<RestaurantOrder> RestaurantOrders => Set<RestaurantOrder>();
    public DbSet<RestaurantOrderItem> RestaurantOrderItems => Set<RestaurantOrderItem>();

   public DbSet<WhatsAppSettings> WhatsAppSettingsEntries => Set<WhatsAppSettings>();
    public DbSet<WhatsAppMessageLog> WhatsAppMessageLogs => Set<WhatsAppMessageLog>();

  // Global application branding (Developer-managed, not store-scoped)
    public DbSet<AppBranding> AppBrandings => Set<AppBranding>();
    public DbSet<SalePayment> SalePayments => Set<SalePayment>();
    public DbSet<CustomerPayment> CustomerPayments => Set<CustomerPayment>();

protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
{
    base.ConfigureConventions(configurationBuilder);

    configurationBuilder
        .Properties<DateTime>()
        .HaveColumnType("timestamp with time zone");

    configurationBuilder
        .Properties<DateTime?>()
        .HaveColumnType("timestamp with time zone");
}

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        // ---- Decimal precision ----
        foreach (var property in builder.Model.GetEntityTypes()
                     .SelectMany(t => t.GetProperties())
                     .Where(p => p.ClrType == typeof(decimal) || p.ClrType == typeof(decimal?)))
        {
            property.SetPrecision(18);
            property.SetScale(2);
        }

        // ---- Soft delete filter for every BaseEntity ----
        // ---- Store-scoped tenant filter for every TenantEntity ----
        // Developer accounts (StoreId == null) bypass the store filter and see everything.
        builder.Entity<Category>().HasQueryFilter(e => !e.IsDeleted && (_tenant == null || _tenant.IsDeveloper || e.StoreId == _tenant.StoreId));
        builder.Entity<SubCategory>().HasQueryFilter(e => !e.IsDeleted && (_tenant == null || _tenant.IsDeveloper || e.StoreId == _tenant.StoreId));
        builder.Entity<Brand>().HasQueryFilter(e => !e.IsDeleted && (_tenant == null || _tenant.IsDeveloper || e.StoreId == _tenant.StoreId));
        builder.Entity<UnitOfMeasure>().HasQueryFilter(e => !e.IsDeleted && (_tenant == null || _tenant.IsDeveloper || e.StoreId == _tenant.StoreId));
        builder.Entity<Item>().HasQueryFilter(e => !e.IsDeleted && (_tenant == null || _tenant.IsDeveloper || e.StoreId == _tenant.StoreId));
        builder.Entity<ItemPrice>().HasQueryFilter(e => !e.IsDeleted && (_tenant == null || _tenant.IsDeveloper || e.StoreId == _tenant.StoreId));
        builder.Entity<Customer>().HasQueryFilter(e => !e.IsDeleted && (_tenant == null || _tenant.IsDeveloper || e.StoreId == _tenant.StoreId));
       
        builder.Entity<ShadeColor>().HasQueryFilter(e => !e.IsDeleted && (_tenant == null || _tenant.IsDeveloper || e.StoreId == _tenant.StoreId));
        builder.Entity<Quotation>().HasQueryFilter(e => !e.IsDeleted && (_tenant == null || _tenant.IsDeveloper || e.StoreId == _tenant.StoreId));

        builder.Entity<Vendor>().HasQueryFilter(e => !e.IsDeleted && (_tenant == null || _tenant.IsDeveloper || e.StoreId == _tenant.StoreId));
        builder.Entity<PaymentMode>().HasQueryFilter(e => !e.IsDeleted && (_tenant == null || _tenant.IsDeveloper || e.StoreId == _tenant.StoreId));
        builder.Entity<Tax>().HasQueryFilter(e => !e.IsDeleted && (_tenant == null || _tenant.IsDeveloper || e.StoreId == _tenant.StoreId));
        builder.Entity<Sale>().HasQueryFilter(e => !e.IsDeleted && (_tenant == null || _tenant.IsDeveloper || e.StoreId == _tenant.StoreId));
        builder.Entity<Purchase>().HasQueryFilter(e => !e.IsDeleted && (_tenant == null || _tenant.IsDeveloper || e.StoreId == _tenant.StoreId));
        builder.Entity<InventoryTransaction>().HasQueryFilter(e => !e.IsDeleted && (_tenant == null || _tenant.IsDeveloper || e.StoreId == _tenant.StoreId));
        builder.Entity<StockBatch>().HasQueryFilter(e => !e.IsDeleted && (_tenant == null || _tenant.IsDeveloper || e.StoreId == _tenant.StoreId));
        builder.Entity<AccountTransaction>().HasQueryFilter(e => !e.IsDeleted && (_tenant == null || _tenant.IsDeveloper || e.StoreId == _tenant.StoreId));
        builder.Entity<SalesReturn>().HasQueryFilter(e => !e.IsDeleted && (_tenant == null || _tenant.IsDeveloper || e.StoreId == _tenant.StoreId));
        builder.Entity<ApplicationSetting>().HasQueryFilter(e => !e.IsDeleted && (_tenant == null || _tenant.IsDeveloper || e.StoreId == _tenant.StoreId));
        builder.Entity<PrinterSetting>().HasQueryFilter(e => !e.IsDeleted && (_tenant == null || _tenant.IsDeveloper || e.StoreId == _tenant.StoreId));
        builder.Entity<EmailSettings>().HasQueryFilter(e => !e.IsDeleted && (_tenant == null || _tenant.IsDeveloper || e.StoreId == _tenant.StoreId));
        builder.Entity<InvoiceSettings>().HasQueryFilter(e => !e.IsDeleted && (_tenant == null || _tenant.IsDeveloper || e.StoreId == _tenant.StoreId));
        builder.Entity<StaffModulePermission>().HasQueryFilter(e => !e.IsDeleted && (_tenant == null || _tenant.IsDeveloper || e.StoreId == _tenant.StoreId));

        builder.Entity<DiningTable>().HasQueryFilter(e => !e.IsDeleted && (_tenant == null || _tenant.IsDeveloper || e.StoreId == _tenant.StoreId));
        builder.Entity<TableReservation>().HasQueryFilter(e => !e.IsDeleted && (_tenant == null || _tenant.IsDeveloper || e.StoreId == _tenant.StoreId));
        builder.Entity<Waiter>().HasQueryFilter(e => !e.IsDeleted && (_tenant == null || _tenant.IsDeveloper || e.StoreId == _tenant.StoreId));
        builder.Entity<RestaurantOrder>().HasQueryFilter(e => !e.IsDeleted && (_tenant == null || _tenant.IsDeveloper || e.StoreId == _tenant.StoreId));
        builder.Entity<WhatsAppSettings>().HasQueryFilter(e => !e.IsDeleted && (_tenant == null || _tenant.IsDeveloper || e.StoreId == _tenant.StoreId));
        builder.Entity<WhatsAppMessageLog>().HasQueryFilter(e => !e.IsDeleted && (_tenant == null || _tenant.IsDeveloper || e.StoreId == _tenant.StoreId));
	   builder.Entity<CustomerPayment>().HasQueryFilter(e => !e.IsDeleted && (_tenant == null || _tenant.IsDeveloper || e.StoreId == _tenant.StoreId));

        // ---- Relationships that would otherwise cascade-delete across tenants ----
        builder.Entity<Sale>()
            .HasOne(s => s.Customer)
            .WithMany()
            .HasForeignKey(s => s.CustomerId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.Entity<Sale>()
            .HasOne(s => s.PaymentMode)
            .WithMany()
            .HasForeignKey(s => s.PaymentModeId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.Entity<Item>()
            .HasOne(i => i.Category)
            .WithMany()
            .HasForeignKey(i => i.CategoryId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.Entity<Item>()
            .HasOne(i => i.SubCategory)
            .WithMany()
            .HasForeignKey(i => i.SubCategoryId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.Entity<Item>()
            .HasOne(i => i.Brand)
            .WithMany()
            .HasForeignKey(i => i.BrandId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.Entity<Item>()
            .HasOne(i => i.Unit)
            .WithMany()
            .HasForeignKey(i => i.UnitId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.Entity<ItemPrice>()
            .HasOne(p => p.Item)
            .WithMany()
            .HasForeignKey(p => p.ItemId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Entity<SubCategory>()
            .HasOne(sc => sc.Category)
            .WithMany(c => c.SubCategories)
            .HasForeignKey(sc => sc.CategoryId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Entity<Store>()
            .HasOne(s => s.Company)
            .WithMany(c => c.Stores)
            .HasForeignKey(s => s.CompanyId)
            .OnDelete(DeleteBehavior.Cascade);

 builder.Entity<Store>().Ignore(s => s.IsRestaurant);
builder.Entity<Store>().Ignore(s => s.IsPaintingShop);

        builder.Entity<SaleItem>()
            .HasOne(si => si.Sale)
            .WithMany(s => s.SaleItems)
            .HasForeignKey(si => si.SaleId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Entity<PurchaseItem>()
            .HasOne(pi => pi.Purchase)
            .WithMany(p => p.PurchaseItems)
            .HasForeignKey(pi => pi.PurchaseId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Entity<SalesReturn>()
            .HasOne(sr => sr.Sale)
            .WithMany()
            .HasForeignKey(sr => sr.SaleId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.Entity<SalesReturn>()
            .HasOne(sr => sr.Customer)
            .WithMany()
            .HasForeignKey(sr => sr.CustomerId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.Entity<SalesReturnItem>()
            .HasOne(sri => sri.SalesReturn)
            .WithMany(sr => sr.Items)
            .HasForeignKey(sri => sri.SalesReturnId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Entity<SalesReturnItem>()
            .HasOne(sri => sri.SaleItem)
            .WithMany()
            .HasForeignKey(sri => sri.SaleItemId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.Entity<SalesReturnItem>()
            .HasOne(sri => sri.Item)
            .WithMany()
            .HasForeignKey(sri => sri.ItemId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.Entity<StockBatch>()
            .HasOne(b => b.Item)
            .WithMany()
            .HasForeignKey(b => b.ItemId)
            .OnDelete(DeleteBehavior.Restrict);

        // ---- Useful uniqueness / indexes ----
        builder.Entity<Item>().HasIndex(i => new { i.StoreId, i.ItemCode });
        builder.Entity<Item>().HasIndex(i => new { i.StoreId, i.Barcode });
        builder.Entity<Sale>().HasIndex(s => new { s.StoreId, s.InvoiceNumber }).IsUnique();
        builder.Entity<Company>().HasIndex(c => c.LicenseKey).IsUnique();
        builder.Entity<StaffModulePermission>().HasIndex(p => new { p.ApplicationUserId, p.ModuleKey }).IsUnique();
        builder.Entity<AccountTransaction>().HasIndex(t => new { t.StoreId, t.TransactionDate });
        builder.Entity<StockBatch>().HasIndex(b => new { b.StoreId, b.ItemId, b.ExpiryDate });


  // ---- Restaurant module relationships ----
        builder.Entity<TableReservation>()
            .HasOne(r => r.Table)
            .WithMany()
            .HasForeignKey(r => r.TableId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.Entity<RestaurantOrder>()
            .HasOne(o => o.Table)
            .WithMany()
            .HasForeignKey(o => o.TableId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.Entity<RestaurantOrder>()
            .HasOne(o => o.Waiter)
            .WithMany()
            .HasForeignKey(o => o.WaiterId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.Entity<RestaurantOrder>()
            .HasOne(o => o.Customer)
            .WithMany()
            .HasForeignKey(o => o.CustomerId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.Entity<RestaurantOrderItem>()
            .HasOne(oi => oi.Order)
            .WithMany(o => o.Items)
            .HasForeignKey(oi => oi.OrderId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Entity<RestaurantOrderItem>()
            .HasOne(oi => oi.Item)
            .WithMany()
            .HasForeignKey(oi => oi.ItemId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.Entity<DiningTable>().HasIndex(t => new { t.StoreId, t.TableNumber }).IsUnique();
        builder.Entity<RestaurantOrder>().HasIndex(o => new { o.StoreId, o.OrderNumber }).IsUnique();


// ---- Painting Shop module relationships ----
        builder.Entity<ShadeColor>()
            .HasOne(s => s.Item)
            .WithMany()
            .HasForeignKey(s => s.ItemId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.Entity<Quotation>()
            .HasOne(q => q.Customer)
            .WithMany()
            .HasForeignKey(q => q.CustomerId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.Entity<QuotationItem>()
            .HasOne(qi => qi.Quotation)
            .WithMany(q => q.Items)
            .HasForeignKey(qi => qi.QuotationId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Entity<QuotationItem>()
            .HasOne(qi => qi.Item)
            .WithMany()
            .HasForeignKey(qi => qi.ItemId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.Entity<QuotationItem>()
            .HasOne(qi => qi.ShadeColor)
            .WithMany()
            .HasForeignKey(qi => qi.ShadeColorId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.Entity<ShadeColor>().HasIndex(s => new { s.StoreId, s.ItemId, s.ShadeCode });
        builder.Entity<Quotation>().HasIndex(q => new { q.StoreId, q.QuotationNumber }).IsUnique();

       builder.Entity<WhatsAppMessageLog>()
            .HasOne(l => l.Sale)
            .WithMany()
            .HasForeignKey(l => l.SaleId)
            .OnDelete(DeleteBehavior.Restrict);

       builder.Entity<SalePayment>()
            .HasOne(p => p.Sale)
            .WithMany(s => s.Payments)
            .HasForeignKey(p => p.SaleId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Entity<SalePayment>()
            .HasOne(p => p.PaymentMode)
            .WithMany()
            .HasForeignKey(p => p.PaymentModeId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.Entity<CustomerPayment>()
            .HasOne(p => p.Customer)
            .WithMany()
            .HasForeignKey(p => p.CustomerId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.Entity<CustomerPayment>()
            .HasOne(p => p.PaymentMode)
            .WithMany()
            .HasForeignKey(p => p.PaymentModeId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
