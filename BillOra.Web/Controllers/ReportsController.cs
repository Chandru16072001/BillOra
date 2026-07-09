using BillOra.Application.Common.Interfaces;
using BillOra.Domain.Enums;
using BillOra.Persistence;
using BillOra.Persistence.Identity;
using BillOra.Shared.Constants;
using BillOra.Web.Authorization;
using BillOra.Web.Utils;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace BillOra.Web.Controllers;

// Reports hub. Every report below is built from a shared (headers, rows)
// pair so the same data powers both the on-screen table and the Excel
// (CSV) export - see the private BuildXxx methods. Accounts-side reports
// (Ledger/Cash Book/Day Book/Credit/Debit/Expense/Income/Transaction
// History) are all just filtered views of the same ledger and live under
// Accounts -> History / Balance Sheet rather than being duplicated here.
[Authorize(Roles = Roles.StoreAdmin + "," + Roles.Manager)]
[RequireModule(ModuleKeys.Reports)]
public class ReportsController : Controller
{
    private readonly BillOraDbContext _db;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly ICurrentTenantService _tenant;

    public ReportsController(BillOraDbContext db, UserManager<ApplicationUser> userManager, ICurrentTenantService tenant)
    {
        _db = db;
        _userManager = userManager;
        _tenant = tenant;
    }

    public IActionResult Index() => View();

    // ---------- Sales Report ----------
    public async Task<IActionResult> SalesReport(DateTime? from, DateTime? to)
    {
        var (headers, rows) = await BuildSalesReportAsync(from, to);
        ViewBag.From = from; ViewBag.To = to;
        return View("Table", new ReportTableModel("Sales Report", headers, rows, "SalesReport"));
    }

    private async Task<(string[], List<string[]>)> BuildSalesReportAsync(DateTime? from, DateTime? to)
    {
        var query = _db.Sales.Include(s => s.Customer).Where(s => !s.IsHeld).AsQueryable();
        if (from.HasValue) query = query.Where(s => s.SaleDate >= from.Value);
        if (to.HasValue) query = query.Where(s => s.SaleDate <= to.Value.AddDays(1).AddTicks(-1));

        var sales = await query.OrderByDescending(s => s.SaleDate).ToListAsync();
        var headers = new[] { "Invoice", "Customer", "Date", "Subtotal", "Discount", "Tax", "Grand Total" };
        var rows = sales.Select(s => new[]
        {
            s.InvoiceNumber, s.Customer?.Name ?? "Walk-in", s.SaleDate.ToString("yyyy-MM-dd HH:mm"),
            s.SubTotal.ToString("N2"), s.DiscountAmount.ToString("N2"), s.TaxAmount.ToString("N2"), s.GrandTotal.ToString("N2")
        }).ToList();
        return (headers, rows);
    }

    // ---------- Item-wise Sales Report ----------
    public async Task<IActionResult> ItemWiseSalesReport(DateTime? from, DateTime? to)
    {
        var (headers, rows) = await BuildItemWiseSalesAsync(from, to);
        ViewBag.From = from; ViewBag.To = to;
        return View("Table", new ReportTableModel("Item-wise Sales Report", headers, rows, "ItemWiseSalesReport"));
    }

    private async Task<(string[], List<string[]>)> BuildItemWiseSalesAsync(DateTime? from, DateTime? to)
    {
        var query = _db.SaleItems.Include(si => si.Item).Include(si => si.Sale).Where(si => !si.Sale!.IsHeld).AsQueryable();
        if (from.HasValue) query = query.Where(si => si.Sale!.SaleDate >= from.Value);
        if (to.HasValue) query = query.Where(si => si.Sale!.SaleDate <= to.Value.AddDays(1).AddTicks(-1));

       var saleItems = await query.ToListAsync();

var grouped = saleItems
    .GroupBy(si => si.Item!.Name)
    .Select(g => new
    {
        Item = g.Key,
        Qty = g.Sum(x => x.Quantity),
        Revenue = g.Sum(x => x.LineTotal)
    })
    .OrderByDescending(x => x.Revenue)
    .ToList();


        var headers = new[] { "Item", "Quantity Sold", "Revenue" };
        var rows = grouped.Select(g => new[] { g.Item, g.Qty.ToString("N2"), g.Revenue.ToString("N2") }).ToList();
        return (headers, rows);
    }

    // ---------- Category-wise Sales Report ----------
    public async Task<IActionResult> CategoryWiseSalesReport(DateTime? from, DateTime? to)
    {
        var (headers, rows) = await BuildCategoryWiseSalesAsync(from, to);
        ViewBag.From = from; ViewBag.To = to;
        return View("Table", new ReportTableModel("Category-wise Sales Report", headers, rows, "CategoryWiseSalesReport"));
    }

    private async Task<(string[], List<string[]>)> BuildCategoryWiseSalesAsync(DateTime? from, DateTime? to)
    {
        var query = _db.SaleItems.Include(si => si.Item).ThenInclude(i => i!.Category)
            .Include(si => si.Sale).Where(si => !si.Sale!.IsHeld).AsQueryable();
        if (from.HasValue) query = query.Where(si => si.Sale!.SaleDate >= from.Value);
        if (to.HasValue) query = query.Where(si => si.Sale!.SaleDate <= to.Value.AddDays(1).AddTicks(-1));

var saleItems = await query.ToListAsync();

var grouped = saleItems
    .GroupBy(si => si.Item!.Category != null ? si.Item.Category.Name : "Uncategorized")        

            .Select(g => new { Category = g.Key, Qty = g.Sum(x => x.Quantity), Revenue = g.Sum(x => x.LineTotal) })
            .OrderByDescending(x => x.Revenue).ToList();

        var headers = new[] { "Category", "Quantity Sold", "Revenue" };
        var rows = grouped.Select(g => new[] { g.Category, g.Qty.ToString("N2"), g.Revenue.ToString("N2") }).ToList();
        return (headers, rows);
    }

    // ---------- Inventory / Stock Report ----------
    public async Task<IActionResult> StockReport()
    {
        var (headers, rows) = await BuildStockReportAsync();
        return View("Table", new ReportTableModel("Inventory / Stock Report", headers, rows, "StockReport"));
    }

    private async Task<(string[], List<string[]>)> BuildStockReportAsync()
    {
        var items = await _db.Items.Include(i => i.Category).Where(i => i.IsActive).OrderBy(i => i.Name).ToListAsync();
        var headers = new[] { "Item", "Category", "Current Stock", "Reorder Level", "Purchase Price", "Stock Value", "Status" };
        var rows = items.Select(i => new[]
        {
            i.Name, i.Category?.Name ?? "-", i.CurrentStock.ToString("N2"), i.ReorderLevel.ToString("N2"),
            i.PurchasePrice.ToString("N2"), (i.CurrentStock * i.PurchasePrice).ToString("N2"),
            i.CurrentStock <= 0 ? "Out of Stock" : i.CurrentStock <= i.ReorderLevel ? "Low Stock" : "OK"
        }).ToList();
        return (headers, rows);
    }

    // ---------- GRN Report ----------
    public async Task<IActionResult> GrnReport(DateTime? from, DateTime? to)
    {
        var (headers, rows) = await BuildGrnReportAsync(from, to);
        ViewBag.From = from; ViewBag.To = to;
        return View("Table", new ReportTableModel("GRN Report", headers, rows, "GrnReport"));
    }

    private async Task<(string[], List<string[]>)> BuildGrnReportAsync(DateTime? from, DateTime? to)
    {
        var query = _db.Purchases.Include(p => p.Vendor).AsQueryable();
        if (from.HasValue) query = query.Where(p => p.PurchaseDate >= from.Value);
        if (to.HasValue) query = query.Where(p => p.PurchaseDate <= to.Value.AddDays(1).AddTicks(-1));

        var purchases = await query.OrderByDescending(p => p.PurchaseDate).ToListAsync();
        var headers = new[] { "GRN No.", "Vendor", "Date", "Subtotal", "Tax", "Grand Total" };
        var rows = purchases.Select(p => new[]
        {
            p.InvoiceNumber, p.Vendor?.Name ?? "-", p.PurchaseDate.ToString("yyyy-MM-dd HH:mm"),
            p.SubTotal.ToString("N2"), p.TaxAmount.ToString("N2"), p.GrandTotal.ToString("N2")
        }).ToList();
        return (headers, rows);
    }

    // ---------- Purchase Report (vendor-wise summary) ----------
    public async Task<IActionResult> PurchaseReport(DateTime? from, DateTime? to)
    {
        var (headers, rows) = await BuildPurchaseReportAsync(from, to);
        ViewBag.From = from; ViewBag.To = to;
        return View("Table", new ReportTableModel("Purchase Report (Vendor-wise)", headers, rows, "PurchaseReport"));
    }

    private async Task<(string[], List<string[]>)> BuildPurchaseReportAsync(DateTime? from, DateTime? to)
    {
        var query = _db.Purchases.Include(p => p.Vendor).AsQueryable();
        if (from.HasValue) query = query.Where(p => p.PurchaseDate >= from.Value);
        if (to.HasValue) query = query.Where(p => p.PurchaseDate <= to.Value.AddDays(1).AddTicks(-1));

       var purchases = await query.ToListAsync();

var grouped = purchases
    .GroupBy(p => p.Vendor!.Name)

            .Select(g => new { Vendor = g.Key, Grns = g.Count(), Total = g.Sum(x => x.GrandTotal) })
            .OrderByDescending(x => x.Total).ToList();

        var headers = new[] { "Vendor", "GRN Count", "Total Purchased" };
        var rows = grouped.Select(g => new[] { g.Vendor, g.Grns.ToString(), g.Total.ToString("N2") }).ToList();
        return (headers, rows);
    }

    // ---------- Customer Report ----------
    public async Task<IActionResult> CustomerReport()
    {
        var (headers, rows) = await BuildCustomerReportAsync();
        return View("Table", new ReportTableModel("Customer Report", headers, rows, "CustomerReport"));
    }

    private async Task<(string[], List<string[]>)> BuildCustomerReportAsync()
    {
        var customers = await _db.Customers.Where(c => c.IsActive).ToListAsync();
        var sales = await _db.Sales
    .Where(s => !s.IsHeld && s.CustomerId.HasValue)
    .ToListAsync();

var salesByCustomer = sales
    .GroupBy(s => s.CustomerId!.Value)
    .Select(g => new
    {
        CustomerId = g.Key,
        Total = g.Sum(x => x.GrandTotal),
        Count = g.Count()
    })
    .ToList();

        var headers = new[] { "Customer", "Phone", "Total Purchases", "Bill Count", "Outstanding", "Loyalty Points" };
        var rows = customers.Select(c =>
        {
            var s = salesByCustomer.FirstOrDefault(x => x.CustomerId == c.Id);
            return new[] { c.Name, c.Phone ?? "", (s?.Total ?? 0).ToString("N2"), (s?.Count ?? 0).ToString(), c.OutstandingAmount.ToString("N2"), c.LoyaltyPoints.ToString() };
        }).OrderByDescending(r => decimal.Parse(r[2])).ToList();
        return (headers, rows);
    }

    // ---------- Vendor Report ----------
    public async Task<IActionResult> VendorReport()
    {
        var (headers, rows) = await BuildVendorReportAsync();
        return View("Table", new ReportTableModel("Vendor Report", headers, rows, "VendorReport"));
    }

    private async Task<(string[], List<string[]>)> BuildVendorReportAsync()
    {
        var vendors = await _db.Vendors.Where(v => v.IsActive).ToListAsync();
        var purchases = await _db.Purchases.ToListAsync();

var purchasesByVendor = purchases
    .GroupBy(p => p.VendorId)
    .Select(g => new
    {
        VendorId = g.Key,
        Total = g.Sum(x => x.GrandTotal),
        Count = g.Count()
    })
    .ToList();

        var headers = new[] { "Vendor", "Phone", "Total Purchased", "GRN Count" };
        var rows = vendors.Select(v =>
        {
            var p = purchasesByVendor.FirstOrDefault(x => x.VendorId == v.Id);
            return new[] { v.Name, v.Phone ?? "", (p?.Total ?? 0).ToString("N2"), (p?.Count ?? 0).ToString() };
        }).OrderByDescending(r => decimal.Parse(r[2])).ToList();
        return (headers, rows);
    }

    // ---------- GST Report ----------
    public async Task<IActionResult> GstReport(DateTime? from, DateTime? to)
    {
        var (headers, rows) = await BuildGstReportAsync(from, to);
        ViewBag.From = from; ViewBag.To = to;
        return View("Table", new ReportTableModel("GST Report", headers, rows, "GstReport"));
    }

    private async Task<(string[], List<string[]>)> BuildGstReportAsync(DateTime? from, DateTime? to)
    {
        var query = _db.SaleItems.Include(si => si.Sale).Where(si => !si.Sale!.IsHeld).AsQueryable();
        if (from.HasValue) query = query.Where(si => si.Sale!.SaleDate >= from.Value);
        if (to.HasValue) query = query.Where(si => si.Sale!.SaleDate <= to.Value.AddDays(1).AddTicks(-1));

        var saleItems = await query.ToListAsync();

var grouped = saleItems
    .GroupBy(si => si.GstPercent)
            .Select(g => new { GstPercent = g.Key, TaxableValue = g.Sum(x => x.LineTotal - x.TaxAmount), TaxCollected = g.Sum(x => x.TaxAmount) })
            .OrderBy(x => x.GstPercent).ToList();

        var headers = new[] { "GST %", "Taxable Value", "Tax Collected" };
        var rows = grouped.Select(g => new[] { g.GstPercent + "%", g.TaxableValue.ToString("N2"), g.TaxCollected.ToString("N2") }).ToList();
        return (headers, rows);
    }

    // ---------- Profit & Loss Report ----------
    public async Task<IActionResult> ProfitLossReport(DateTime? from, DateTime? to)
    {
        var (headers, rows) = await BuildProfitLossAsync(from, to);
        ViewBag.From = from; ViewBag.To = to;
        return View("Table", new ReportTableModel("Profit & Loss Report", headers, rows, "ProfitLossReport"));
    }

    private async Task<(string[], List<string[]>)> BuildProfitLossAsync(DateTime? from, DateTime? to)
    {
        var query = _db.SaleItems.Include(si => si.Item).Include(si => si.Sale).Where(si => !si.Sale!.IsHeld).AsQueryable();
        if (from.HasValue) query = query.Where(si => si.Sale!.SaleDate >= from.Value);
        if (to.HasValue) query = query.Where(si => si.Sale!.SaleDate <= to.Value.AddDays(1).AddTicks(-1));

        var lines = await query.ToListAsync();
        var revenue = lines.Sum(l => l.LineTotal);
        var cogs = lines.Sum(l => l.Quantity * (l.Item?.PurchasePrice ?? 0));
        var grossProfit = revenue - cogs;

        var expenseQuery = _db.AccountTransactions.Where(t => t.Type == TransactionDirection.Debit && t.SourceModule == "Manual");
        if (from.HasValue) expenseQuery = expenseQuery.Where(t => t.TransactionDate >= from.Value);
        if (to.HasValue) expenseQuery = expenseQuery.Where(t => t.TransactionDate <= to.Value.AddDays(1).AddTicks(-1));
        var expenses = (await expenseQuery.ToListAsync())
    .Sum(t => t.Amount);

        var netProfit = grossProfit - expenses;

        var headers = new[] { "Line", "Amount" };
        var rows = new List<string[]>
        {
            new[] { "Revenue (Sales)", revenue.ToString("N2") },
            new[] { "Cost of Goods Sold", cogs.ToString("N2") },
            new[] { "Gross Profit", grossProfit.ToString("N2") },
            new[] { "Operating Expenses", expenses.ToString("N2") },
            new[] { "Net Profit / Loss", netProfit.ToString("N2") }
        };
        return (headers, rows);
    }

    // ---------- Payment Report ----------
    public async Task<IActionResult> PaymentReport(DateTime? from, DateTime? to)
    {
        var (headers, rows) = await BuildPaymentReportAsync(from, to);
        ViewBag.From = from; ViewBag.To = to;
        return View("Table", new ReportTableModel("Payment Report", headers, rows, "PaymentReport"));
    }

    private async Task<(string[], List<string[]>)> BuildPaymentReportAsync(DateTime? from, DateTime? to)
    {
        var query = _db.Sales.Include(s => s.PaymentMode).Where(s => !s.IsHeld).AsQueryable();
        if (from.HasValue) query = query.Where(s => s.SaleDate >= from.Value);
        if (to.HasValue) query = query.Where(s => s.SaleDate <= to.Value.AddDays(1).AddTicks(-1));

        var sales = await query.ToListAsync();

var grouped = sales
    .GroupBy(s => s.PaymentMode != null ? s.PaymentMode.Name : "Unspecified")
            .Select(g => new { Mode = g.Key, Count = g.Count(), Total = g.Sum(x => x.GrandTotal) })
            .OrderByDescending(x => x.Total).ToList();

        var headers = new[] { "Payment Mode", "Bill Count", "Total Collected" };
        var rows = grouped.Select(g => new[] { g.Mode, g.Count.ToString(), g.Total.ToString("N2") }).ToList();
        return (headers, rows);
    }

    // ---------- Outstanding Report ----------
    public async Task<IActionResult> OutstandingReport()
    {
        var (headers, rows) = await BuildOutstandingAsync();
        return View("Table", new ReportTableModel("Outstanding Report", headers, rows, "OutstandingReport"));
    }

    private async Task<(string[], List<string[]>)> BuildOutstandingAsync()
    {
       var customers = (await _db.Customers
    .Where(c => c.OutstandingAmount > 0)
    .ToListAsync())
    .OrderByDescending(c => c.OutstandingAmount)
    .ToList();
        var headers = new[] { "Customer", "Phone", "Outstanding Amount" };
        var rows = customers.Select(c => new[] { c.Name, c.Phone ?? "", c.OutstandingAmount.ToString("N2") }).ToList();
        return (headers, rows);
    }

    // ---------- User Activity Report / Audit Log ----------
    public async Task<IActionResult> UserActivityReport(DateTime? from, DateTime? to)
    {
        var (headers, rows) = await BuildUserActivityAsync(from, to);
        ViewBag.From = from; ViewBag.To = to;
        return View("Table", new ReportTableModel("User Activity Report / Audit Log", headers, rows, "UserActivityReport"));
    }

    private async Task<(string[], List<string[]>)> BuildUserActivityAsync(DateTime? from, DateTime? to)
    {
        var companyId = _tenant.CompanyId;
        var query = _db.ActivityLogs.Where(a => a.CompanyId == companyId).AsQueryable();
        if (from.HasValue) query = query.Where(a => a.Timestamp >= from.Value);
        if (to.HasValue) query = query.Where(a => a.Timestamp <= to.Value.AddDays(1).AddTicks(-1));

        var logs = await query.OrderByDescending(a => a.Timestamp).Take(500).ToListAsync();
        var userIds = logs.Where(l => l.UserId != null).Select(l => l.UserId!).Distinct().ToList();
        var users = await _userManager.Users.Where(u => userIds.Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => u.FullName);

        var headers = new[] { "Timestamp", "User", "Action", "Details", "IP Address" };
        var rows = logs.Select(l => new[]
        {
            l.Timestamp.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss"),
            l.UserId != null && users.ContainsKey(l.UserId) ? users[l.UserId] : "System",
            l.Action, l.Details ?? "", l.IpAddress ?? ""
        }).ToList();
        return (headers, rows);
    }

    // ---------- Shared Excel export dispatcher ----------
    [HttpGet]
    public async Task<IActionResult> Export(string report, DateTime? from, DateTime? to)
    {
        (string[] Headers, List<string[]> Rows) result = report switch
        {
            "SalesReport" => await BuildSalesReportAsync(from, to),
            "ItemWiseSalesReport" => await BuildItemWiseSalesAsync(from, to),
            "CategoryWiseSalesReport" => await BuildCategoryWiseSalesAsync(from, to),
            "StockReport" => await BuildStockReportAsync(),
            "GrnReport" => await BuildGrnReportAsync(from, to),
            "PurchaseReport" => await BuildPurchaseReportAsync(from, to),
            "CustomerReport" => await BuildCustomerReportAsync(),
            "VendorReport" => await BuildVendorReportAsync(),
            "GstReport" => await BuildGstReportAsync(from, to),
            "ProfitLossReport" => await BuildProfitLossAsync(from, to),
            "PaymentReport" => await BuildPaymentReportAsync(from, to),
            "UserActivityReport" => await BuildUserActivityAsync(from, to),
            "OutstandingReport" => await BuildOutstandingAsync(),
            _ => (Array.Empty<string>(), new List<string[]>())
        };

        var csv = CsvExportHelper.ToCsv(result.Headers, result.Rows);
        return File(csv, "text/csv", $"{report}_{DateTime.UtcNow:yyyyMMdd}.csv");
    }
}

public record ReportTableModel(string Title, string[] Headers, List<string[]> Rows, string ReportKey);
