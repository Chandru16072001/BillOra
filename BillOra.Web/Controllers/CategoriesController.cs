using BillOra.Application.Common.Interfaces;
using BillOra.Domain.Entities;
using BillOra.Persistence;
using BillOra.Shared.Constants;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

using BillOra.Web.Authorization;

namespace BillOra.Web.Controllers;

[Authorize(Roles = Roles.StoreAdmin + "," + Roles.Manager)]
[RequireModule(ModuleKeys.Categories)]
public class CategoriesController : Controller
{
    private readonly BillOraDbContext _db;
    private readonly ICurrentTenantService _tenant;

    public CategoriesController(BillOraDbContext db, ICurrentTenantService tenant)
    {
        _db = db;
        _tenant = tenant;
    }

    public async Task<IActionResult> Index()
    {
        var categories = await _db.Categories.Include(c => c.SubCategories)
            .OrderBy(c => c.Name).ToListAsync();
        return View(categories);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(string name)
    {
        if (!string.IsNullOrWhiteSpace(name))
        {
            _db.Categories.Add(new Category { Name = name.Trim(), StoreId = _tenant.StoreId ?? 0 });
            await _db.SaveChangesAsync();
        }
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ToggleActive(int id)
    {
        var category = await _db.Categories.FindAsync(id);
        if (category != null)
        {
            category.IsActive = !category.IsActive;
            await _db.SaveChangesAsync();
        }
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(int id)
    {
        var category = await _db.Categories.FindAsync(id);
        if (category != null)
        {
            category.IsDeleted = true; // soft delete
            await _db.SaveChangesAsync();
        }
        return RedirectToAction(nameof(Index));
    }
}
