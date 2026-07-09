using BillOra.Application.Common.Interfaces;
using BillOra.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace BillOra.Web.Controllers;

[Authorize]
public class DashboardController : Controller
{
    private readonly BillOraDbContext _db;
    private readonly ICurrentTenantService _tenant;

    public DashboardController(BillOraDbContext db, ICurrentTenantService tenant)
    {
        _db = db;
        _tenant = tenant;
    }

    public async Task<IActionResult> Index()
    {
        var today = DateTime.UtcNow.Date;
        var monthStart = new DateTime(today.Year, today.Month, 1);

        var todaySales = await _db.Sales.Where(s => s.SaleDate.Date == today && !s.IsHeld).ToListAsync();
        var monthSales = await _db.Sales.Where(s => s.SaleDate >= monthStart && !s.IsHeld).ToListAsync();

        ViewBag.TodaySalesTotal = todaySales.Sum(s => s.GrandTotal);
        ViewBag.TodayBillCount = todaySales.Count;
        ViewBag.MonthSalesTotal = monthSales.Sum(s => s.GrandTotal);

        ViewBag.LowStockCount = await _db.Items.CountAsync(i => i.CurrentStock <= i.ReorderLevel && i.IsActive);
        ViewBag.OutOfStockCount = await _db.Items.CountAsync(i => i.CurrentStock <= 0 && i.IsActive);
        ViewBag.ItemCount = await _db.Items.CountAsync(i => i.IsActive);
        ViewBag.CustomerCount = await _db.Customers.CountAsync(c => c.IsActive);

        ViewBag.RecentSales = await _db.Sales.Include(s => s.Customer)
            .OrderByDescending(s => s.SaleDate).Take(5).ToListAsync();

        return View();
    }
}
