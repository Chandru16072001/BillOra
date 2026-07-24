using BillOra.Application.Common.Interfaces;
using BillOra.Domain.Entities;
using BillOra.Domain.Enums;
using BillOra.Persistence;
using BillOra.Shared.Constants;
using BillOra.Web.Authorization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace BillOra.Web.Controllers;

// Table Management: the status grid (Available/Occupied/Reserved/Cleaning)
// that's the home screen of the restaurant workflow. Tapping a table jumps
// straight into starting or continuing its order.
[Authorize(Roles = Roles.StoreAdmin + "," + Roles.Manager + "," + Roles.Cashier)]
[RequireModule(ModuleKeys.Tables)]
[RequireRestaurant]
public class TablesController : Controller
{
    private readonly BillOraDbContext _db;
    private readonly ICurrentTenantService _tenant;

    public TablesController(BillOraDbContext db, ICurrentTenantService tenant)
    {
        _db = db;
        _tenant = tenant;
    }

    public async Task<IActionResult> Index()
    {
        var tables = await _db.DiningTables.OrderBy(t => t.TableNumber).ToListAsync();

        // Attach the open order id (if any) so the view can link straight into it.
        var openOrders = await _db.RestaurantOrders
            .Where(o => o.TableId != null && o.Status != RestaurantOrderStatus.Billed && o.Status != RestaurantOrderStatus.Cancelled)
            .ToListAsync();
        ViewBag.OpenOrdersByTable = openOrders.Where(o => o.TableId.HasValue).ToDictionary(o => o.TableId!.Value, o => o.Id);

        var upcomingReservations = await _db.TableReservations
            .Where(r => r.Status == ReservationStatus.Booked || r.Status == ReservationStatus.Confirmed)
            .Where(r => r.ReservationDateTime >= DateTime.UtcNow.AddHours(-1))
            .OrderBy(r => r.ReservationDateTime)
            .ToListAsync();
        ViewBag.ReservationsByTable = upcomingReservations.GroupBy(r => r.TableId).ToDictionary(g => g.Key, g => g.First());

        return View(tables);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(string tableNumber, int capacity, string? section)
    {
        if (!string.IsNullOrWhiteSpace(tableNumber))
        {
            _db.DiningTables.Add(new DiningTable
            {
                StoreId = _tenant.StoreId ?? 0,
                TableNumber = tableNumber.Trim(),
                Capacity = capacity <= 0 ? 4 : capacity,
                Section = section
            });
            await _db.SaveChangesAsync();
        }
        return RedirectToAction(nameof(Index));
    }

    // Manual status override (e.g. marking a table "Cleaning" after guests leave,
    // then "Available" once reset) - occupied/available otherwise flow automatically
    // from the Orders workflow.
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SetStatus(int id, TableStatus status)
    {
        var table = await _db.DiningTables.FindAsync(id);
        if (table != null)
        {
            table.Status = status;
            if (status == TableStatus.Available) table.CurrentOrderId = null;
            await _db.SaveChangesAsync();
        }
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(int id)
    {
        var table = await _db.DiningTables.FindAsync(id);
        if (table != null && table.Status == TableStatus.Available)
        {
            table.IsDeleted = true;
            await _db.SaveChangesAsync();
        }
        return RedirectToAction(nameof(Index));
    }
}
