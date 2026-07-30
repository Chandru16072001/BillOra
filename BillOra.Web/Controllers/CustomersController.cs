using BillOra.Application.Common.Interfaces;
using BillOra.Domain.Entities;
using BillOra.Persistence;
using BillOra.Shared.Constants;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using BillOra.Domain.Enums;

using BillOra.Web.Authorization;

namespace BillOra.Web.Controllers;

[Authorize(Roles = Roles.StoreAdmin + "," + Roles.Manager + "," + Roles.Cashier)]
[RequireModule(ModuleKeys.Customers)]
public class CustomersController : Controller
{
    private readonly BillOraDbContext _db;
    private readonly ICurrentTenantService _tenant;
    private readonly IAccountingService _accounting;

    public CustomersController(BillOraDbContext db, ICurrentTenantService tenant, IAccountingService accounting)
    {
        _db = db;
        _tenant = tenant;
	_accounting = accounting;
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

// Customer Outstanding Collection - view balance + history, collect payment.
public async Task<IActionResult> Outstanding(int id)
{
    var customer = await _db.Customers.FindAsync(id);
    if (customer == null) return NotFound();

    ViewBag.PaymentModes = await _db.PaymentModes.Where(p => p.IsActive).ToListAsync();
    ViewBag.PaymentHistory = await _db.CustomerPayments
        .Include(p => p.PaymentMode)
        .Where(p => p.CustomerId == id)
        .OrderByDescending(p => p.PaymentDate)
        .ToListAsync();

    return View(customer);
}

[HttpPost]
[ValidateAntiForgeryToken]
public async Task<IActionResult> CollectPayment(int id, decimal amount, int? paymentModeId, string? notes)
{
    var customer = await _db.Customers.FindAsync(id);
    if (customer == null) return NotFound();

    if (amount <= 0)
    {
        TempData["Error"] = "Enter a valid amount to collect.";
        return RedirectToAction(nameof(Outstanding), new { id });
    }
    if (amount > customer.OutstandingAmount)
    {
        TempData["Error"] = $"Amount cannot exceed the outstanding balance of ₹{customer.OutstandingAmount:N2}.";
        return RedirectToAction(nameof(Outstanding), new { id });
    }

    var payment = new CustomerPayment
    {
        StoreId = _tenant.StoreId ?? 0,
        CustomerId = id,
        Amount = amount,
        PaymentModeId = paymentModeId,
        Notes = notes,
        ReceivedByUserId = _tenant.UserId
    };
    _db.CustomerPayments.Add(payment);

    // Update the customer's outstanding balance immediately.
    customer.OutstandingAmount -= amount;

    await _db.SaveChangesAsync();

    // Outstanding Collection posts to the Credit side - this is the cash
    // actually coming in now, which is what "cancels out" the Debit
    // (Customer Receivable) entry posted at the time of the original
    // credit sale. See POSController.CreateSale for that side of it.
    var paymentModeName = paymentModeId.HasValue
        ? (await _db.PaymentModes.FindAsync(paymentModeId.Value))?.Name
        : null;

    await _accounting.PostAsync(_tenant.StoreId ?? 0, $"Payment collected from {customer.Name}", amount,
        TransactionDirection.Credit, "Outstanding Collection",
        sourceModule: "CustomerPayment", sourceId: payment.Id, paymentMethod: paymentModeName);

    TempData["Success"] = $"Collected ₹{amount:N2} from {customer.Name}. New outstanding: ₹{customer.OutstandingAmount:N2}.";
    return RedirectToAction(nameof(Outstanding), new { id });
}

// Searchable customer lookup for the Billing Screen (replaces the old
// dropdown) - matches name OR phone, case-insensitively regardless of
// which database provider is in use (see the note in POSController.SearchItems).
[HttpGet]
public async Task<IActionResult> SearchCustomers(string term)
{
    term ??= string.Empty;
    var termLower = term.ToLower();

    var customers = await _db.Customers
        .Where(c => c.IsActive && (c.Name.ToLower().Contains(termLower) || (c.Phone ?? "").Contains(term)))
        .OrderBy(c => c.Name)
        .Take(20)
        .Select(c => new { c.Id, c.Name, c.Phone, c.OutstandingAmount })
        .ToListAsync();

    return Json(customers);
}

}
