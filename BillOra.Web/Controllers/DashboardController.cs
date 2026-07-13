using BillOra.Application.Common.Interfaces;
using BillOra.Domain.Entities;
using BillOra.Domain.Enums;
using BillOra.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace BillOra.Web.Controllers;

// Modern analytics dashboard: KPIs (with day/month-over-month trend), a
// sales trend chart, category/payment/stock-status breakdowns, top items
// and top customers, low-stock alerts, and a handful of plain-language
// business insights computed from the same data. All chart data is handed
// to the view as simple arrays and rendered client-side with Chart.js (CDN).
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
        var yesterday = today.AddDays(-1);
        var monthStart = new DateTime(today.Year, today.Month, 1);
        var daysElapsedThisMonth = today.Day;
        var prevMonthStart = monthStart.AddMonths(-1);
        var prevMonthComparableEnd = prevMonthStart.AddDays(daysElapsedThisMonth); // exclusive, same day-count window
        var trendStart = today.AddDays(-13); // 14-day trend

        var vm = new DashboardViewModel();

        // ---------- Core sale sets ----------
        var todaySales = await _db.Sales.Where(s => s.SaleDate.Date == today && !s.IsHeld).ToListAsync();
        var yesterdaySales = await _db.Sales.Where(s => s.SaleDate.Date == yesterday && !s.IsHeld).ToListAsync();
        var monthSales = await _db.Sales.Where(s => s.SaleDate >= monthStart && !s.IsHeld).ToListAsync();
        var prevMonthComparableSales = await _db.Sales
            .Where(s => s.SaleDate >= prevMonthStart && s.SaleDate < prevMonthComparableEnd && !s.IsHeld).ToListAsync();

        vm.TodaySalesTotal = todaySales.Sum(s => s.GrandTotal);
        vm.TodayBillCount = todaySales.Count;
        vm.TodayVsYesterdayPercent = PercentChange(vm.TodaySalesTotal, yesterdaySales.Sum(s => s.GrandTotal));

        vm.MonthSalesTotal = monthSales.Sum(s => s.GrandTotal);
        vm.MonthVsLastMonthPercent = PercentChange(vm.MonthSalesTotal, prevMonthComparableSales.Sum(s => s.GrandTotal));

        // ---------- Profit (month-to-date, gross) ----------
        var monthSaleIds = monthSales.Select(s => s.Id).ToList();
        var monthLines = await _db.SaleItems.Include(si => si.Item)
            .Where(si => monthSaleIds.Contains(si.SaleId)).ToListAsync();
        var cogs = monthLines.Sum(l => l.Quantity * (l.Item?.PurchasePrice ?? 0));
        vm.MonthGrossProfit = vm.MonthSalesTotal - cogs;

        // ---------- Purchases / stock ----------
       // vm.PurchaseThisMonth = await _db.Purchases.Where(p => p.PurchaseDate >= monthStart).SumAsync(p => (decimal?)p.GrandTotal) ?? 0;
vm.PurchaseThisMonth = (await _db.Purchases
    .Where(p => p.PurchaseDate >= monthStart)
    .Select(p => p.GrandTotal)
    .ToListAsync())
    .Sum();

        var items = await _db.Items.Include(i => i.Category).Where(i => i.IsActive).ToListAsync();
        vm.StockValue = items.Sum(i => i.CurrentStock * i.PurchasePrice);
        vm.LowStockCount = items.Count(i => i.CurrentStock > 0 && i.CurrentStock <= i.ReorderLevel);
        vm.OutOfStockCount = items.Count(i => i.CurrentStock <= 0);
        vm.ItemCount = items.Count;
        vm.LowStockItems = items.Where(i => i.CurrentStock <= i.ReorderLevel)
            .OrderBy(i => i.CurrentStock).Take(8).ToList();

        // ---------- Customers / outstanding ----------
        vm.CustomerCount = await _db.Customers.CountAsync(c => c.IsActive);
      //  vm.PendingPayments = await _db.Customers.SumAsync(c => (decimal?)c.OutstandingAmount) ?? 0;
vm.PendingPayments = (await _db.Customers
    .Where(c => c.IsActive)
    .Select(c => c.OutstandingAmount)
    .ToListAsync())
    .Sum();
        // ---------- Sales trend (last 14 days) ----------
        var trendSales = await _db.Sales.Where(s => s.SaleDate.Date >= trendStart && !s.IsHeld).ToListAsync();
        for (var d = trendStart; d <= today; d = d.AddDays(1))
        {
            vm.SalesTrendLabels.Add(d.ToString("dd MMM"));
            vm.SalesTrendValues.Add(trendSales.Where(s => s.SaleDate.Date == d).Sum(s => s.GrandTotal));
        }

        // ---------- Category-wise sales (this month) ----------
        var categoryGroups = monthLines
            .GroupBy(l => l.Item?.Category?.Name ?? "Uncategorized")
            .Select(g => new { Name = g.Key, Total = g.Sum(x => x.LineTotal) })
            .OrderByDescending(g => g.Total).Take(6).ToList();
        vm.CategoryLabels = categoryGroups.Select(g => g.Name).ToList();
        vm.CategoryValues = categoryGroups.Select(g => g.Total).ToList();

        // ---------- Top selling items (this month) ----------
        var topItems = monthLines
            .GroupBy(l => l.Item?.Name ?? "Unknown")
            .Select(g => new { Name = g.Key, Revenue = g.Sum(x => x.LineTotal), Qty = g.Sum(x => x.Quantity) })
            .OrderByDescending(g => g.Revenue).Take(7).ToList();
        vm.TopItemLabels = topItems.Select(g => g.Name).ToList();
        vm.TopItemValues = topItems.Select(g => g.Revenue).ToList();

        // ---------- Payment mode distribution (this month) ----------
        var paymentModeIds = monthSales.Where(s => s.PaymentModeId.HasValue).Select(s => s.PaymentModeId!.Value).Distinct().ToList();
        var paymentModes = await _db.PaymentModes.Where(p => paymentModeIds.Contains(p.Id)).ToDictionaryAsync(p => p.Id, p => p.Name);
        var paymentGroups = monthSales
            .GroupBy(s => s.PaymentModeId.HasValue && paymentModes.ContainsKey(s.PaymentModeId.Value) ? paymentModes[s.PaymentModeId.Value] : "Unspecified")
            .Select(g => new { Mode = g.Key, Total = g.Sum(x => x.GrandTotal) })
            .OrderByDescending(g => g.Total).ToList();
        vm.PaymentLabels = paymentGroups.Select(g => g.Mode).ToList();
        vm.PaymentValues = paymentGroups.Select(g => g.Total).ToList();

        // ---------- Top customers (this month) ----------
        var customerGroups = monthSales.Where(s => s.CustomerId.HasValue)
            .GroupBy(s => s.CustomerId!.Value)
            .Select(g => new { CustomerId = g.Key, Total = g.Sum(x => x.GrandTotal), Bills = g.Count() })
            .OrderByDescending(g => g.Total).Take(5).ToList();
        var customerIds = customerGroups.Select(g => g.CustomerId).ToList();
        var customerNames = await _db.Customers.Where(c => customerIds.Contains(c.Id)).ToDictionaryAsync(c => c.Id, c => c.Name);
        vm.TopCustomers = customerGroups.Select(g => (customerNames.GetValueOrDefault(g.CustomerId, "Unknown"), g.Total, g.Bills)).ToList();

        // ---------- Recent bills ----------
        vm.RecentSales = await _db.Sales.Include(s => s.Customer)
            .OrderByDescending(s => s.SaleDate).Take(6).ToListAsync();

        // ---------- Plain-language insights ----------
        vm.Insights = BuildInsights(vm);

        return View(vm);
    }

    private static decimal PercentChange(decimal current, decimal previous)
    {
        if (previous == 0) return current > 0 ? 100 : 0;
        return Math.Round((current - previous) / previous * 100, 1);
    }

    private static List<string> BuildInsights(DashboardViewModel vm)
    {
        var insights = new List<string>();

        if (vm.TopItemLabels.Count > 0)
            insights.Add($"Your best-selling item this month is \"{vm.TopItemLabels[0]}\", bringing in ₹{vm.TopItemValues[0]:N0}.");

        if (vm.CategoryLabels.Count > 0)
            insights.Add($"\"{vm.CategoryLabels[0]}\" is your top category this month at ₹{vm.CategoryValues[0]:N0} in sales.");

        if (vm.LowStockCount + vm.OutOfStockCount > 0)
            insights.Add($"{vm.OutOfStockCount} item(s) are out of stock and {vm.LowStockCount} are running low — worth reordering soon.");
        else
            insights.Add("Stock levels look healthy — nothing is low or out of stock right now.");

        if (vm.MonthVsLastMonthPercent >= 0)
            insights.Add($"Sales are up {vm.MonthVsLastMonthPercent}% compared to the same point last month.");
        else
            insights.Add($"Sales are down {Math.Abs(vm.MonthVsLastMonthPercent)}% compared to the same point last month.");

        if (vm.PendingPayments > 0)
            insights.Add($"₹{vm.PendingPayments:N0} is currently outstanding across your customers.");

        return insights;
    }
}

public class DashboardViewModel
{
    public decimal TodaySalesTotal { get; set; }
    public int TodayBillCount { get; set; }
    public decimal TodayVsYesterdayPercent { get; set; }

    public decimal MonthSalesTotal { get; set; }
    public decimal MonthVsLastMonthPercent { get; set; }
    public decimal MonthGrossProfit { get; set; }

    public decimal PurchaseThisMonth { get; set; }
    public decimal StockValue { get; set; }
    public int LowStockCount { get; set; }
    public int OutOfStockCount { get; set; }
    public int ItemCount { get; set; }
    public List<Item> LowStockItems { get; set; } = new();

    public int CustomerCount { get; set; }
    public decimal PendingPayments { get; set; }

    public List<string> SalesTrendLabels { get; set; } = new();
    public List<decimal> SalesTrendValues { get; set; } = new();

    public List<string> CategoryLabels { get; set; } = new();
    public List<decimal> CategoryValues { get; set; } = new();

    public List<string> TopItemLabels { get; set; } = new();
    public List<decimal> TopItemValues { get; set; } = new();

    public List<string> PaymentLabels { get; set; } = new();
    public List<decimal> PaymentValues { get; set; } = new();

    public List<(string Name, decimal Total, int Bills)> TopCustomers { get; set; } = new();
    public List<Sale> RecentSales { get; set; } = new();
    public List<string> Insights { get; set; } = new();
}
