using BillOra.Application.Common.Interfaces;
using BillOra.Domain.Entities;
using BillOra.Persistence;
using BillOra.Persistence.Identity;
using BillOra.Shared.Constants;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace BillOra.Web.Controllers;

// Staff Master (SRS-style Staff Master + RBAC). Only a Store Admin can create
// staff logins, assign a role, and grant/revoke individual module access.
// Enforces the Developer-set MaxStaffUsers license cap on this Company.
[Authorize(Roles = Roles.StoreAdmin)]
public class StaffController : Controller
{
    private readonly BillOraDbContext _db;
    private readonly ICurrentTenantService _tenant;
    private readonly UserManager<ApplicationUser> _userManager;

    public StaffController(BillOraDbContext db, ICurrentTenantService tenant, UserManager<ApplicationUser> userManager)
    {
        _db = db;
        _tenant = tenant;
        _userManager = userManager;
    }

    public async Task<IActionResult> Index()
    {
        var companyId = _tenant.CompanyId ?? 0;
        var staff = await _userManager.Users
            .Where(u => u.CompanyId == companyId)
            .OrderBy(u => u.FullName)
            .ToListAsync();

        var staffWithRoles = new List<(ApplicationUser User, IList<string> Roles)>();
        foreach (var user in staff)
            staffWithRoles.Add((user, await _userManager.GetRolesAsync(user)));

        var company = await _db.Companies.IgnoreQueryFilters().FirstOrDefaultAsync(c => c.Id == companyId);
        ViewBag.MaxStaffUsers = company?.MaxStaffUsers ?? 0;
        ViewBag.CurrentStaffCount = staff.Count;

        return View(staffWithRoles);
    }

    [HttpGet]
    public async Task<IActionResult> Create()
    {
        var companyId = _tenant.CompanyId ?? 0;
        var company = await _db.Companies.IgnoreQueryFilters().FirstOrDefaultAsync(c => c.Id == companyId);
        var currentCount = await _userManager.Users.CountAsync(u => u.CompanyId == companyId);

        if (company != null && currentCount >= company.MaxStaffUsers)
        {
            TempData["Error"] = $"You've reached your licensed staff limit ({company.MaxStaffUsers} users). Contact your software provider to increase it.";
            return RedirectToAction(nameof(Index));
        }

        ViewBag.ModuleKeys = ModuleKeys.All;
        return View();
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(string fullName, string email, string password, string role, List<string>? allowedModules)
    {
        var companyId = _tenant.CompanyId ?? 0;
        var storeId = _tenant.StoreId ?? 0;

        var company = await _db.Companies.IgnoreQueryFilters().FirstOrDefaultAsync(c => c.Id == companyId);
        var currentCount = await _userManager.Users.CountAsync(u => u.CompanyId == companyId);

        if (company != null && currentCount >= company.MaxStaffUsers)
        {
            ModelState.AddModelError(string.Empty, $"Licensed staff limit reached ({company.MaxStaffUsers} users).");
            ViewBag.ModuleKeys = ModuleKeys.All;
            return View();
        }

        if (role != Roles.Manager && role != Roles.Cashier)
        {
            ModelState.AddModelError(string.Empty, "Staff can only be assigned the Manager or Cashier role.");
            ViewBag.ModuleKeys = ModuleKeys.All;
            return View();
        }

        if (await _userManager.FindByEmailAsync(email) != null)
        {
            ModelState.AddModelError(string.Empty, "A user with that email already exists.");
            ViewBag.ModuleKeys = ModuleKeys.All;
            return View();
        }

        var user = new ApplicationUser
        {
            UserName = email,
            Email = email,
            FullName = fullName,
            EmailConfirmed = true,
            CompanyId = companyId,
            StoreId = storeId
        };

        var result = await _userManager.CreateAsync(user, password);
        if (!result.Succeeded)
        {
            foreach (var error in result.Errors) ModelState.AddModelError(string.Empty, error.Description);
            ViewBag.ModuleKeys = ModuleKeys.All;
            return View();
        }

        await _userManager.AddToRoleAsync(user, role);

        allowedModules ??= new List<string>();
        foreach (var moduleKey in ModuleKeys.All)
        {
            _db.StaffModulePermissions.Add(new StaffModulePermission
            {
                StoreId = storeId,
                ApplicationUserId = user.Id,
                ModuleKey = moduleKey,
                IsAllowed = allowedModules.Contains(moduleKey)
            });
        }
        await _db.SaveChangesAsync();

        TempData["Success"] = $"Staff account created for {fullName} ({email}).";
        return RedirectToAction(nameof(Index));
    }

    [HttpGet]
    public async Task<IActionResult> EditPermissions(string id)
    {
        var user = await _userManager.FindByIdAsync(id);
        if (user == null || user.CompanyId != _tenant.CompanyId) return NotFound();

        ViewBag.User = user;
        ViewBag.Roles = await _userManager.GetRolesAsync(user);
        ViewBag.ModuleKeys = ModuleKeys.All;
        ViewBag.Permissions = await _db.StaffModulePermissions
            .Where(p => p.ApplicationUserId == id)
            .ToDictionaryAsync(p => p.ModuleKey, p => p.IsAllowed);

        return View();
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> EditPermissions(string id, List<string>? allowedModules)
    {
        var user = await _userManager.FindByIdAsync(id);
        if (user == null || user.CompanyId != _tenant.CompanyId) return NotFound();

        allowedModules ??= new List<string>();
        var existing = await _db.StaffModulePermissions.Where(p => p.ApplicationUserId == id).ToListAsync();

        foreach (var moduleKey in ModuleKeys.All)
        {
            var perm = existing.FirstOrDefault(p => p.ModuleKey == moduleKey);
            if (perm == null)
            {
                _db.StaffModulePermissions.Add(new StaffModulePermission
                {
                    StoreId = user.StoreId ?? 0,
                    ApplicationUserId = id,
                    ModuleKey = moduleKey,
                    IsAllowed = allowedModules.Contains(moduleKey)
                });
            }
            else
            {
                perm.IsAllowed = allowedModules.Contains(moduleKey);
            }
        }

        await _db.SaveChangesAsync();
        TempData["Success"] = $"Updated permissions for {user.FullName}.";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Deactivate(string id)
    {
        var user = await _userManager.FindByIdAsync(id);
        if (user == null || user.CompanyId != _tenant.CompanyId) return NotFound();

        await _userManager.SetLockoutEnabledAsync(user, true);
        await _userManager.SetLockoutEndDateAsync(user, DateTimeOffset.MaxValue);

        TempData["Success"] = $"{user.FullName}'s account has been deactivated.";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Reactivate(string id)
    {
        var user = await _userManager.FindByIdAsync(id);
        if (user == null || user.CompanyId != _tenant.CompanyId) return NotFound();

        await _userManager.SetLockoutEndDateAsync(user, null);

        TempData["Success"] = $"{user.FullName}'s account has been reactivated.";
        return RedirectToAction(nameof(Index));
    }
}
