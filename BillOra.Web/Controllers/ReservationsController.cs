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

// Table Reservation / Booking.
[Authorize(Roles = Roles.StoreAdmin + "," + Roles.Manager + "," + Roles.Cashier)]
[RequireModule(ModuleKeys.Reservations)]
[RequireRestaurant]
public class ReservationsController : Controller
{
    private readonly BillOraDbContext _db;
    private readonly ICurrentTenantService _tenant;

    public ReservationsController(BillOraDbContext db, ICurrentTenantService tenant)
    {
        _db = db;
        _tenant = tenant;
    }

    public async Task<IActionResult> Index()
    {
        ViewBag.Tables = await _db.DiningTables.OrderBy(t => t.TableNumber).ToListAsync();
        var reservations = await _db.TableReservations
            .Include(r => r.Table)
            .Where(r => r.Status != ReservationStatus.Completed && r.Status != ReservationStatus.Cancelled)
            .OrderBy(r => r.ReservationDateTime)
            .ToListAsync();
        return View(reservations);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(int tableId, string customerName, string? customerPhone,
        int partySize, DateTime reservationDateTime, string? notes)
    {
        var table = await _db.DiningTables.FindAsync(tableId);
        if (table == null || string.IsNullOrWhiteSpace(customerName))
        {
            TempData["Error"] = "Select a table and enter the customer's name.";
            return RedirectToAction(nameof(Index));
        }

        _db.TableReservations.Add(new TableReservation
        {
            StoreId = _tenant.StoreId ?? 0,
            TableId = tableId,
            CustomerName = customerName,
            CustomerPhone = customerPhone,
            PartySize = partySize <= 0 ? 2 : partySize,
            ReservationDateTime = reservationDateTime == default ? DateTime.UtcNow.AddHours(1) : reservationDateTime,
            Notes = notes,
            Status = ReservationStatus.Booked
        });

        table.Status = TableStatus.Reserved;
        await _db.SaveChangesAsync();

        TempData["Success"] = $"Table {table.TableNumber} reserved for {customerName}.";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> MarkSeated(int id)
    {
        var reservation = await _db.TableReservations.Include(r => r.Table).FirstOrDefaultAsync(r => r.Id == id);
        if (reservation == null) return NotFound();

        reservation.Status = ReservationStatus.Seated;
        if (reservation.Table != null) reservation.Table.Status = TableStatus.Occupied;
        await _db.SaveChangesAsync();

        TempData["Success"] = $"{reservation.CustomerName} seated at Table {reservation.Table?.TableNumber}.";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Cancel(int id)
    {
        var reservation = await _db.TableReservations.Include(r => r.Table).FirstOrDefaultAsync(r => r.Id == id);
        if (reservation == null) return NotFound();

        reservation.Status = ReservationStatus.Cancelled;
        if (reservation.Table != null && reservation.Table.Status == TableStatus.Reserved)
            reservation.Table.Status = TableStatus.Available;

        await _db.SaveChangesAsync();
        TempData["Success"] = "Reservation cancelled.";
        return RedirectToAction(nameof(Index));
    }
}
