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
[RequireModule(ModuleKeys.Vendors)]
public class VendorsController : Controller
{
    private readonly BillOraDbContext _db;
    private readonly ICurrentTenantService _tenant;

    public VendorsController(BillOraDbContext db, ICurrentTenantService tenant)
    {
        _db = db;
        _tenant = tenant;
    }

    public async Task<IActionResult> Index(string? search)
    {
        var query = _db.Vendors.AsQueryable();
        if (!string.IsNullOrWhiteSpace(search))
            query = query.Where(v => v.Name.Contains(search) || (v.Phone ?? "").Contains(search));

        ViewBag.Search = search;
        return View(await query.OrderBy(v => v.Name).ToListAsync());
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(Vendor vendor)
    {
        vendor.StoreId = _tenant.StoreId ?? 0;
        _db.Vendors.Add(vendor);
        await _db.SaveChangesAsync();

        if (Request.Headers["X-Requested-With"] == "XMLHttpRequest")
            return Json(new { id = vendor.Id, name = vendor.Name, phone = vendor.Phone });

        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(int id, Vendor vendor)
    {
        var existing = await _db.Vendors.FindAsync(id);
        if (existing == null) return NotFound();

        existing.Name = vendor.Name;
        existing.Phone = vendor.Phone;
        existing.Email = vendor.Email;
        existing.GstNumber = vendor.GstNumber;
        existing.Address = vendor.Address;
        existing.IsActive = vendor.IsActive;
        existing.UpdatedAt = DateTime.UtcNow;

        await _db.SaveChangesAsync();
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(int id)
    {
        var vendor = await _db.Vendors.FindAsync(id);
        if (vendor != null)
        {
            vendor.IsDeleted = true;
            await _db.SaveChangesAsync();
        }
        return RedirectToAction(nameof(Index));
    }
}
