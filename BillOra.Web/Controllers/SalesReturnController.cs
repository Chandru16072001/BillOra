using BillOra.Application.Common.Interfaces;
using BillOra.Application.DTOs;
using BillOra.Domain.Entities;
using BillOra.Domain.Enums;
using BillOra.Persistence;
using BillOra.Shared.Constants;
using BillOra.Web.Authorization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace BillOra.Web.Controllers;

// SRS section 11 - Sales Return, with the "Load Stock" Yes/No choice:
// Yes puts the returned quantity back into sellable inventory; No does not
// (e.g. the item came back damaged). Every return automatically updates
// stock (per that choice), posts a Debit to the Mini Accounts ledger,
// records a Stock History entry, and writes an audit log line.
[Authorize(Roles = Roles.StoreAdmin + "," + Roles.Manager + "," + Roles.Cashier)]
[RequireModule(ModuleKeys.Pos)]
public class SalesReturnController : Controller
{
    private readonly BillOraDbContext _db;
    private readonly ICurrentTenantService _tenant;
    private readonly IActivityLogger _activityLogger;
    private readonly IAccountingService _accounting;
    private readonly IEmailSender _emailSender;

    public SalesReturnController(BillOraDbContext db, ICurrentTenantService tenant, IActivityLogger activityLogger,
        IAccountingService accounting, IEmailSender emailSender)
    {
        _db = db;
        _tenant = tenant;
        _activityLogger = activityLogger;
        _accounting = accounting;
        _emailSender = emailSender;
    }

    [HttpGet]
    public IActionResult Search() => View();

    [HttpGet]
    public async Task<IActionResult> FindInvoice(string invoiceNumber)
    {
        var sale = await _db.Sales
            .Include(s => s.Customer)
            .Include(s => s.SaleItems).ThenInclude(si => si.Item)
            .FirstOrDefaultAsync(s => s.InvoiceNumber == invoiceNumber && !s.IsHeld);

        if (sale == null) return NotFound(new { message = "No invoice found with that number." });

        // Already-returned quantity per line, so the form can cap how much more can go back.
        var alreadyReturned = await _db.SalesReturnItems
            .Where(sri => sale.SaleItems.Select(si => si.Id).Contains(sri.SaleItemId))
            .GroupBy(sri => sri.SaleItemId)
            .Select(g => new { SaleItemId = g.Key, Qty = g.Sum(x => x.ReturnQuantity) })
            .ToListAsync();

        return Json(new
        {
            sale.Id,
            sale.InvoiceNumber,
            sale.GrandTotal,
            SaleDate = sale.SaleDate.ToLocalTime().ToString("dd MMM yyyy hh:mm tt"),
            CustomerName = sale.Customer?.Name ?? "Walk-in",
            sale.CustomerId,
            Lines = sale.SaleItems.Select(si => new
            {
                si.Id,
                ItemName = si.Item?.Name,
                si.Quantity,
                si.UnitPrice,
                si.GstPercent,
                si.LineTotal,
                AlreadyReturned = alreadyReturned.FirstOrDefault(a => a.SaleItemId == si.Id)?.Qty ?? 0
            })
        });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create([FromBody] CreateSalesReturnRequest request)
    {
        if (request.Lines.Count == 0) return BadRequest("Select at least one item to return.");

        var sale = await _db.Sales.Include(s => s.SaleItems).FirstOrDefaultAsync(s => s.Id == request.SaleId);
        if (sale == null) return BadRequest("Original invoice not found.");

        var storeId = _tenant.StoreId ?? 0;
        var salesReturn = new SalesReturn
        {
            StoreId = storeId,
            SaleId = sale.Id,
            InvoiceNumber = sale.InvoiceNumber,
            CustomerId = sale.CustomerId,
            ReturnReason = request.ReturnReason,
            LoadStock = request.LoadStock,
            ProcessedByUserId = _tenant.UserId
        };

        decimal refundTotal = 0;

        foreach (var line in request.Lines)
        {
            if (line.ReturnQuantity <= 0) continue;
            var saleItem = sale.SaleItems.FirstOrDefault(si => si.Id == line.SaleItemId);
            if (saleItem == null) continue;

            var alreadyReturned = await _db.SalesReturnItems
                .Where(sri => sri.SaleItemId == saleItem.Id)
                .SumAsync(sri => (decimal?)sri.ReturnQuantity) ?? 0;

            var returnable = saleItem.Quantity - alreadyReturned;
            var qty = Math.Min(line.ReturnQuantity, returnable);
            if (qty <= 0) continue;

            // Use the sale's own recorded per-unit total (LineTotal / Quantity) rather than
            // recomputing GST here - correct regardless of whether the item was priced
            // GST-inclusive or GST-exclusive at the time of sale.
            var perUnitTotal = saleItem.Quantity > 0 ? saleItem.LineTotal / saleItem.Quantity : 0;
            var lineRefund = qty * perUnitTotal;
            refundTotal += lineRefund;

            salesReturn.Items.Add(new SalesReturnItem
            {
                SaleItemId = saleItem.Id,
                ItemId = saleItem.ItemId,
                ReturnQuantity = qty,
                UnitPrice = saleItem.UnitPrice,
                LineRefund = lineRefund
            });

            var item = await _db.Items.FindAsync(saleItem.ItemId);
            if (item != null && request.LoadStock)
            {
                item.CurrentStock += qty;
                _db.InventoryTransactions.Add(new InventoryTransaction
                {
                    StoreId = storeId,
                    ItemId = item.Id,
                    TransactionType = InventoryTransactionType.SaleReturn,
                    Quantity = qty,
                    BalanceAfter = item.CurrentStock,
                    Notes = $"Sales return against {sale.InvoiceNumber} (restocked)"
                });
            }
            else if (item != null)
            {
                // Not restocked (e.g. damaged) - still logged for a complete stock history,
                // just with zero quantity impact.
                _db.InventoryTransactions.Add(new InventoryTransaction
                {
                    StoreId = storeId,
                    ItemId = item.Id,
                    TransactionType = InventoryTransactionType.SaleReturn,
                    Quantity = 0,
                    BalanceAfter = item.CurrentStock,
                    Notes = $"Sales return against {sale.InvoiceNumber} (not restocked - damaged/unsellable)"
                });
            }
        }

        if (salesReturn.Items.Count == 0) return BadRequest("No returnable quantity for the selected items.");

        salesReturn.RefundAmount = refundTotal;
        _db.SalesReturns.Add(salesReturn);

        // Update customer outstanding/refund balance if this was a credit sale.
        if (sale.CustomerId.HasValue)
        {
            var customer = await _db.Customers.FindAsync(sale.CustomerId.Value);
            if (customer != null) customer.OutstandingAmount -= refundTotal;
        }

        await _db.SaveChangesAsync();

        await _accounting.PostAsync(storeId, $"Sales Return against {sale.InvoiceNumber}", refundTotal,
            TransactionDirection.Debit, "Sales Return/Refund",
            reason: request.ReturnReason, sourceModule: "SalesReturn", sourceId: salesReturn.Id,
            referenceNumber: sale.InvoiceNumber);

        await _activityLogger.LogAsync("Sales return processed",
            $"{sale.InvoiceNumber} - refund ₹{refundTotal:N2}, load stock: {request.LoadStock}");

        return Json(new SalesReturnResultDto { SalesReturnId = salesReturn.Id, InvoiceNumber = sale.InvoiceNumber, RefundAmount = refundTotal });
    }

    public async Task<IActionResult> Details(int id)
    {
        var salesReturn = await _db.SalesReturns
            .Include(sr => sr.Customer)
            .Include(sr => sr.Sale)
            .Include(sr => sr.Items).ThenInclude(i => i.Item)
            .FirstOrDefaultAsync(sr => sr.Id == id);
        if (salesReturn == null) return NotFound();
        return View(salesReturn);
    }

    public async Task<IActionResult> Print(int id)
    {
        var salesReturn = await _db.SalesReturns
            .Include(sr => sr.Customer)
            .Include(sr => sr.Items).ThenInclude(i => i.Item)
            .FirstOrDefaultAsync(sr => sr.Id == id);
        if (salesReturn == null) return NotFound();

        var store = await _db.Stores.FindAsync(salesReturn.StoreId);
        ViewBag.Store = store;
        return View(salesReturn);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Email(int id)
    {
        var salesReturn = await _db.SalesReturns
            .Include(sr => sr.Customer)
            .Include(sr => sr.Items).ThenInclude(i => i.Item)
            .FirstOrDefaultAsync(sr => sr.Id == id);

        if (salesReturn == null || string.IsNullOrWhiteSpace(salesReturn.Customer?.Email))
        {
            TempData["Error"] = "No customer email on file for this return.";
            return RedirectToAction(nameof(Details), new { id });
        }

        var rows = string.Join("", salesReturn.Items.Select(i =>
            $"<tr><td>{i.Item?.Name}</td><td style='text-align:right'>{i.ReturnQuantity}</td><td style='text-align:right'>₹{i.LineRefund:N2}</td></tr>"));
        var html = $@"<div style='font-family:Arial,sans-serif;max-width:480px;margin:auto;'>
            <h2>Sales Return Receipt</h2>
            <p>Against Invoice: <strong>{salesReturn.InvoiceNumber}</strong><br/>Date: {salesReturn.ReturnDate:dd MMM yyyy}</p>
            <table style='width:100%;border-collapse:collapse;' cellpadding='6'>
                <thead><tr style='background:#f5f6fb;'><th style='text-align:left'>Item</th><th>Qty</th><th>Refund</th></tr></thead>
                <tbody>{rows}</tbody>
            </table>
            <h3 style='text-align:right'>Total Refund: ₹{salesReturn.RefundAmount:N2}</h3>
        </div>";

        var (success, error) = await _emailSender.SendInvoiceEmailAsync(
            salesReturn.StoreId, salesReturn.Customer.Email, $"Sales Return Receipt - {salesReturn.InvoiceNumber}", html);

        TempData[success ? "Success" : "Error"] = success ? "Sales return receipt emailed." : $"Could not send: {error}";
        return RedirectToAction(nameof(Details), new { id });
    }

    // Sales Return Report - filterable by date range, customer, invoice number, item.
    public async Task<IActionResult> Report(DateTime? from, DateTime? to, string? customer, string? invoiceNumber, string? item)
    {
        var query = _db.SalesReturns.Include(sr => sr.Customer).Include(sr => sr.Items).ThenInclude(i => i.Item).AsQueryable();

        if (from.HasValue) query = query.Where(sr => sr.ReturnDate >= from.Value);
        if (to.HasValue) query = query.Where(sr => sr.ReturnDate <= to.Value.AddDays(1).AddTicks(-1));
        if (!string.IsNullOrWhiteSpace(customer)) query = query.Where(sr => sr.Customer != null && sr.Customer.Name.Contains(customer));
        if (!string.IsNullOrWhiteSpace(invoiceNumber)) query = query.Where(sr => sr.InvoiceNumber.Contains(invoiceNumber));
        if (!string.IsNullOrWhiteSpace(item)) query = query.Where(sr => sr.Items.Any(i => i.Item != null && i.Item.Name.Contains(item)));

        var results = await query.OrderByDescending(sr => sr.ReturnDate).ToListAsync();

        ViewBag.From = from; ViewBag.To = to; ViewBag.Customer = customer; ViewBag.InvoiceNumber = invoiceNumber; ViewBag.Item = item;
        return View(results);
    }

    [HttpGet]
    public async Task<IActionResult> ExportReport(DateTime? from, DateTime? to, string? customer, string? invoiceNumber, string? item)
    {
        var query = _db.SalesReturns.Include(sr => sr.Customer).AsQueryable();
        if (from.HasValue) query = query.Where(sr => sr.ReturnDate >= from.Value);
        if (to.HasValue) query = query.Where(sr => sr.ReturnDate <= to.Value.AddDays(1).AddTicks(-1));
        if (!string.IsNullOrWhiteSpace(customer)) query = query.Where(sr => sr.Customer != null && sr.Customer.Name.Contains(customer));
        if (!string.IsNullOrWhiteSpace(invoiceNumber)) query = query.Where(sr => sr.InvoiceNumber.Contains(invoiceNumber));

        var results = await query.OrderByDescending(sr => sr.ReturnDate).ToListAsync();

        var headers = new[] { "Return ID", "Invoice", "Customer", "Return Date", "Reason", "Load Stock", "Refund Amount" };
        var rows = results.Select(sr => new[]
        {
            sr.Id.ToString(), sr.InvoiceNumber, sr.Customer?.Name ?? "Walk-in", sr.ReturnDate.ToString("yyyy-MM-dd HH:mm"),
            sr.ReturnReason ?? "", sr.LoadStock ? "Yes" : "No", sr.RefundAmount.ToString("N2")
        });

        var csv = Utils.CsvExportHelper.ToCsv(headers, rows);
        return File(csv, "text/csv", $"SalesReturnReport_{DateTime.UtcNow:yyyyMMdd}.csv");
    }
}
