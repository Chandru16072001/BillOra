using BillOra.Application.Common.Interfaces;
using BillOra.Domain.Entities;
using BillOra.Persistence;
using BillOra.Shared.Constants;
using BillOra.Web.Authorization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace BillOra.Web.Controllers;

[Authorize(Roles = Roles.StoreAdmin + "," + Roles.Manager)]
[RequireModule(ModuleKeys.Waiters)]
[RequireRestaurant]
public class WaitersController : Controller
{
    private readonly BillOraDbContext _db;
    private readonly ICurrentTenantService _tenant;

    public WaitersController(BillOraDbContext db, ICurrentTenantService tenant)
    {
        _db = db;
        _tenant = tenant;
    }

    public async Task<IActionResult> Index()
    {
        return View(await _db.Waiters.OrderBy(w => w.Name).ToListAsync());
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(string name, string? phone)
    {
        if (!string.IsNullOrWhiteSpace(name))
        {
            _db.Waiters.Add(new Waiter { StoreId = _tenant.StoreId ?? 0, Name = name.Trim(), Phone = phone });
            await _db.SaveChangesAsync();
        }
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ToggleActive(int id)
    {
        var waiter = await _db.Waiters.FindAsync(id);
        if (waiter != null) { waiter.IsActive = !waiter.IsActive; await _db.SaveChangesAsync(); }
        return RedirectToAction(nameof(Index));
    }
}
