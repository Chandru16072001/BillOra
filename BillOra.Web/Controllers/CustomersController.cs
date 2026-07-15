using BillOra.Application.Common.Interfaces;
using BillOra.Domain.Entities;
using BillOra.Persistence;
using BillOra.Shared.Constants;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

using BillOra.Web.Authorization;

namespace BillOra.Web.Controllers;

[Authorize(Roles = Roles.StoreAdmin + "," + Roles.Manager + "," + Roles.Cashier)]
[RequireModule(ModuleKeys.Customers)]
public class CustomersController : Controller
{
    private readonly BillOraDbContext _db;
    private readonly ICurrentTenantService _tenant;

    public CustomersController(BillOraDbContext db, ICurrentTenantService tenant)
    {
        _db = db;
        _tenant = tenant;
    }

    public async Task<IActionResult> Index(string? search)
    {
        var query = _db.Customers.AsQueryable();
        if (!string.IsNullOrWhiteSpace(search))
            query = query.Where(c => c.Name.Contains(search) || (c.Phone ?? "").Contains(search));

        ViewBag.Search = search;
        return View(await query.OrderBy(c => c.Name).ToListAsync());
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(Customer customer)
    {
        customer.StoreId = _tenant.StoreId ?? 0;
        _db.Customers.Add(customer);
        await _db.SaveChangesAsync();

        // Support the POS "quick add customer" popup without leaving the billing screen.
        if (Request.Headers["X-Requested-With"] == "XMLHttpRequest")
            return Json(new { id = customer.Id, name = customer.Name, phone = customer.Phone });

        return RedirectToAction(nameof(Index));
    }

    public async Task<IActionResult> Edit(int id)
    {
        var customer = await _db.Customers.FindAsync(id);
        if (customer == null) return NotFound();
        return View(customer);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(int id, Customer customer)
    {
        var existing = await _db.Customers.FindAsync(id);
        if (existing == null) return NotFound();

        existing.Name = customer.Name;
        existing.Phone = customer.Phone;
        existing.Email = customer.Email;
        existing.GstNumber = customer.GstNumber;
        existing.Address = customer.Address;
        existing.District = customer.District;
        existing.Taluk = customer.Taluk;
        existing.State = customer.State;
        existing.Country = customer.Country;
        existing.PinCode = customer.PinCode;
        existing.CreditLimit = customer.CreditLimit;
        existing.IsActive = customer.IsActive;
        existing.UpdatedAt = DateTime.UtcNow;

        await _db.SaveChangesAsync();
        TempData["Success"] = $"Updated {existing.Name}.";
        return RedirectToAction(nameof(Index));
    }
}
