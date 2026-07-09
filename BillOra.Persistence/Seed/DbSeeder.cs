using BillOra.Domain.Entities;
using BillOra.Domain.Enums;
using BillOra.Persistence.Identity;
using BillOra.Shared.Constants;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
namespace BillOra.Persistence.Seed;

// Seeds roles, a Developer login, a demo Company + Store, and enough
// master data (GST slabs, payment modes, a couple of items) that the
// POS screen and Masters screens have something to show on first run.
public static class DbSeeder
{
    public static async Task SeedAsync(IServiceProvider services)
    {
        var db = services.GetRequiredService<BillOraDbContext>();
        var userManager = services.GetRequiredService<UserManager<ApplicationUser>>();
        var roleManager = services.GetRequiredService<RoleManager<IdentityRole>>();

        await db.Database.MigrateAsync();

        foreach (var role in Roles.All)
        {
            if (!await roleManager.RoleExistsAsync(role))
                await roleManager.CreateAsync(new IdentityRole(role));
        }

        // ---- Developer (super admin, not tied to any tenant) ----
        var dev = await userManager.FindByEmailAsync(AppConstants.DeveloperEmail);
        if (dev == null)
        {
            dev = new ApplicationUser
            {
                UserName = AppConstants.DeveloperEmail,
                Email = AppConstants.DeveloperEmail,
                FullName = "BillOra Developer",
                EmailConfirmed = true
            };
            await userManager.CreateAsync(dev, AppConstants.DeveloperPassword);
            await userManager.AddToRoleAsync(dev, Roles.Developer);
        }

        // ---- Demo tenant: one Company with one Store ----
        var company = await db.Companies.IgnoreQueryFilters().FirstOrDefaultAsync(c => c.OwnerEmail == AppConstants.DemoStoreAdminEmail);
        if (company == null)
        {
            company = new Company
            {
                Name = "Demo Retail Co.",
                OwnerEmail = AppConstants.DemoStoreAdminEmail,
                Phone = "9999999999",
                PlanName = AppConstants.TrialPlanName,
                SubscriptionStartDate = DateTime.UtcNow,
                SubscriptionEndDate = DateTime.UtcNow.AddMonths(1),
                SubscriptionStatus = SubscriptionStatus.Active
            };
            db.Companies.Add(company);
            await db.SaveChangesAsync();

            var store = new Store
            {
                CompanyId = company.Id,
                Name = "Demo Main Store",
                InvoicePrefix = "INV",
                Currency = "INR",
                GstEnabled = true
            };
            db.Stores.Add(store);
            await db.SaveChangesAsync();

            var admin = new ApplicationUser
            {
                UserName = AppConstants.DemoStoreAdminEmail,
                Email = AppConstants.DemoStoreAdminEmail,
                FullName = "Store Admin",
                EmailConfirmed = true,
                CompanyId = company.Id,
                StoreId = store.Id
            };
            await userManager.CreateAsync(admin, AppConstants.DemoStoreAdminPassword);
            await userManager.AddToRoleAsync(admin, Roles.StoreAdmin);

            var cashier = new ApplicationUser
            {
                UserName = AppConstants.DemoCashierEmail,
                Email = AppConstants.DemoCashierEmail,
                FullName = "Demo Cashier",
                EmailConfirmed = true,
                CompanyId = company.Id,
                StoreId = store.Id
            };
            await userManager.CreateAsync(cashier, AppConstants.DemoCashierPassword);
            await userManager.AddToRoleAsync(cashier, Roles.Cashier);

            // ---- Master data ----
            db.PaymentModes.AddRange(
                new PaymentMode { StoreId = store.Id, Name = "Cash" },
                new PaymentMode { StoreId = store.Id, Name = "UPI" },
                new PaymentMode { StoreId = store.Id, Name = "Card" },
                new PaymentMode { StoreId = store.Id, Name = "Credit" }
            );

            db.Taxes.AddRange(
                new Tax { StoreId = store.Id, Name = "GST 0%", Percentage = 0 },
                new Tax { StoreId = store.Id, Name = "GST 5%", Percentage = 5 },
                new Tax { StoreId = store.Id, Name = "GST 12%", Percentage = 12 },
                new Tax { StoreId = store.Id, Name = "GST 18%", Percentage = 18 },
                new Tax { StoreId = store.Id, Name = "GST 28%", Percentage = 28 }
            );

            var category = new Category { StoreId = store.Id, Name = "General" };
            db.Categories.Add(category);

            var unit = new UnitOfMeasure { StoreId = store.Id, Name = "Piece", ShortCode = "Pcs" };
            db.Units.Add(unit);

            db.PrinterSettings.Add(new PrinterSetting
            {
                StoreId = store.Id,
                Type = PrinterType.Thermal80mm,
                PaperSize = "80mm",
                Copies = 1,
                FooterMessage = "Thank you for shopping with us!",
                IsDefault = true
            });

            await db.SaveChangesAsync();

            db.Items.AddRange(
                new Item
                {
                    StoreId = store.Id, Name = "Sample Item A", ItemCode = "ITM001",
                    CategoryId = category.Id, UnitId = unit.Id, GstPercent = 18,
                    PurchasePrice = 50, SellingPrice = 80, MinSellingPrice = 60,
                    OpeningStock = 100, CurrentStock = 100, ReorderLevel = 10
                },
                new Item
                {
                    StoreId = store.Id, Name = "Sample Item B", ItemCode = "ITM002",
                    CategoryId = category.Id, UnitId = unit.Id, GstPercent = 5,
                    PurchasePrice = 20, SellingPrice = 35, MinSellingPrice = 25,
                    OpeningStock = 200, CurrentStock = 200, ReorderLevel = 20
                }
            );
            await db.SaveChangesAsync();
        }
    }
}
