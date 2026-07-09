using BillOra.Domain.Entities;
using BillOra.Domain.Enums;
using BillOra.Persistence;
using BillOra.Persistence.Identity;
using BillOra.Shared.Constants;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace BillOra.Web.Controllers;

// SRS section 14 - Developer Portal (Subscription Management).
// Only the Developer role can reach this: create new tenants, company list,
// license status, extend / disable / renew subscriptions across every tenant.
[Authorize(Roles = Roles.Developer)]
public class DeveloperController : Controller
{
    private readonly BillOraDbContext _db;
    private readonly UserManager<ApplicationUser> _userManager;

    public DeveloperController(BillOraDbContext db, UserManager<ApplicationUser> userManager)
    {
        _db = db;
        _userManager = userManager;
    }

    [HttpGet]
    public IActionResult Create() => View();

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(string companyName, string ownerEmail, string phone,
        string storeName, string adminEmail, string adminPassword, int trialDays = 14, int maxStaffUsers = 5)
    {
        if (string.IsNullOrWhiteSpace(companyName) || string.IsNullOrWhiteSpace(adminEmail) || string.IsNullOrWhiteSpace(adminPassword))
        {
            ModelState.AddModelError(string.Empty, "Company name, admin email, and admin password are required.");
            return View();
        }

        if (await _userManager.FindByEmailAsync(adminEmail) != null)
        {
            ModelState.AddModelError(string.Empty, "A user with that admin email already exists.");
            return View();
        }

        var company = new Company
        {
            Name = companyName,
            OwnerEmail = ownerEmail,
            Phone = phone,
            PlanName = AppConstants.TrialPlanName,
            MaxStaffUsers = maxStaffUsers <= 0 ? 5 : maxStaffUsers,
            SubscriptionStartDate = DateTime.UtcNow,
            SubscriptionEndDate = DateTime.UtcNow.AddDays(trialDays),
            SubscriptionStatus = SubscriptionStatus.Active
        };
        _db.Companies.Add(company);
        await _db.SaveChangesAsync();

        var store = new Store
        {
            CompanyId = company.Id,
            Name = string.IsNullOrWhiteSpace(storeName) ? $"{companyName} Main Store" : storeName,
            InvoicePrefix = "INV",
            Currency = "INR",
            GstEnabled = true
        };
        _db.Stores.Add(store);
        await _db.SaveChangesAsync();

        var admin = new ApplicationUser
        {
            UserName = adminEmail,
            Email = adminEmail,
            FullName = $"{companyName} Admin",
            EmailConfirmed = true,
            CompanyId = company.Id,
            StoreId = store.Id
        };
        var result = await _userManager.CreateAsync(admin, adminPassword);
        if (!result.Succeeded)
        {
            foreach (var error in result.Errors) ModelState.AddModelError(string.Empty, error.Description);
            return View();
        }
        await _userManager.AddToRoleAsync(admin, Roles.StoreAdmin);

        // Seed sensible defaults so the new tenant isn't an empty shell on first login.
        _db.PaymentModes.AddRange(
            new PaymentMode { StoreId = store.Id, Name = "Cash" },
            new PaymentMode { StoreId = store.Id, Name = "UPI" },
            new PaymentMode { StoreId = store.Id, Name = "Card" }
        );
        _db.Taxes.AddRange(
            new Tax { StoreId = store.Id, Name = "GST 0%", Percentage = 0 },
            new Tax { StoreId = store.Id, Name = "GST 5%", Percentage = 5 },
            new Tax { StoreId = store.Id, Name = "GST 12%", Percentage = 12 },
            new Tax { StoreId = store.Id, Name = "GST 18%", Percentage = 18 },
            new Tax { StoreId = store.Id, Name = "GST 28%", Percentage = 28 }
        );
        await _db.SaveChangesAsync();

        TempData["Success"] = $"Client '{companyName}' created. Admin login: {adminEmail}";
        return RedirectToAction(nameof(Index));
    }

    public async Task<IActionResult> Index()
    {
        var companies = await _db.Companies.IgnoreQueryFilters()
            .Include(c => c.Stores)
            .OrderByDescending(c => c.CreatedAt)
            .ToListAsync();
        return View(companies);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> UpdateStaffLimit(int id, int maxStaffUsers)
    {
        var company = await _db.Companies.IgnoreQueryFilters().FirstOrDefaultAsync(c => c.Id == id);
        if (company != null && maxStaffUsers > 0)
        {
            company.MaxStaffUsers = maxStaffUsers;
            await _db.SaveChangesAsync();
            TempData["Success"] = $"Updated staff limit for {company.Name} to {maxStaffUsers}.";
        }
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Extend(int id, int months)
    {
        var company = await _db.Companies.IgnoreQueryFilters().FirstOrDefaultAsync(c => c.Id == id);
        if (company != null)
        {
            var baseDate = company.SubscriptionEndDate > DateTime.UtcNow ? company.SubscriptionEndDate : DateTime.UtcNow;
            company.SubscriptionEndDate = baseDate.AddMonths(months);
            company.SubscriptionStatus = SubscriptionStatus.Active;
            company.IsActive = true;
            await _db.SaveChangesAsync();
        }
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Disable(int id)
    {
        var company = await _db.Companies.IgnoreQueryFilters().FirstOrDefaultAsync(c => c.Id == id);
        if (company != null)
        {
            company.IsActive = false;
            company.SubscriptionStatus = SubscriptionStatus.Disabled;
            await _db.SaveChangesAsync();
        }
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Enable(int id)
    {
        var company = await _db.Companies.IgnoreQueryFilters().FirstOrDefaultAsync(c => c.Id == id);
        if (company != null)
        {
            company.IsActive = true;
            company.SubscriptionStatus = company.SubscriptionEndDate >= DateTime.UtcNow
                ? SubscriptionStatus.Active
                : SubscriptionStatus.Expired;
            await _db.SaveChangesAsync();
        }
        return RedirectToAction(nameof(Index));
    }
}
