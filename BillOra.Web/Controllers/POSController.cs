using BillOra.Application.Common.Interfaces;
using BillOra.Application.DTOs;
using BillOra.Domain.Entities;
using BillOra.Domain.Enums;
using BillOra.Persistence;
using BillOra.Shared.Constants;
using BillOra.Web.Utils;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

using BillOra.Web.Authorization;

namespace BillOra.Web.Controllers;

// The Billing Screen - the heart of the app. Every feature on this
// controller is used directly from POS/Index.cshtml: instant search,
// barcode-friendly exact-match lookup, Hold/Recall/Suspend, split
// payment, credit sales, and reprint-with-duplicate-watermark tracking.
[Authorize(Roles = Roles.StoreAdmin + "," + Roles.Manager + "," + Roles.Cashier)]
[RequireModule(ModuleKeys.Pos)]
public class POSController : Controller
{
    private readonly BillOraDbContext _db;
    private readonly ICurrentTenantService _tenant;
    private readonly IActivityLogger _activityLogger;
    private readonly IEmailSender _emailSender;
    private readonly IAccountingService _accounting;
    private readonly IBatchStockService _batchStock;

    public POSController(BillOraDbContext db, ICurrentTenantService tenant, IActivityLogger activityLogger,
        IEmailSender emailSender, IAccountingService accounting, IBatchStockService batchStock)
    {
        _db = db;
        _tenant = tenant;
        _activityLogger = activityLogger;
        _emailSender = emailSender;
        _accounting = accounting;
        _batchStock = batchStock;
    }

    // Performance: never load the full catalog on open. Top 50 best-sellers
    // (by quantity sold, last 90 days) covers the vast majority of real
    // billing traffic; anything else is one keystroke away via search.
    public async Task<IActionResult> Index()
    {
        var storeId = _tenant.StoreId ?? 0;
        var store = await _db.Stores.FindAsync(storeId);

        ViewBag.Items = await GetTopSellingItemsAsync(storeId, 50);
        ViewBag.PaymentModes = await _db.PaymentModes.Where(p => p.IsActive).ToListAsync();
        ViewBag.Customers = await _db.Customers.Where(c => c.IsActive).OrderBy(c => c.Name).ToListAsync();
        ViewBag.GstEnabled = store?.GstEnabled ?? true;
        ViewBag.StockValidationEnabled = store?.StockValidationEnabled ?? true;
        ViewBag.HeldBillCount = await _db.Sales.CountAsync(s => s.IsHeld);

        return View();
    }

    private async Task<List<Item>> GetTopSellingItemsAsync(int storeId, int take)
    {
        var since = DateTime.UtcNow.AddDays(-90);

        var topSellingIds = await _db.SaleItems
            .Where(si => si.Sale!.StoreId == storeId && !si.Sale.IsHeld && si.Sale.SaleDate >= since)
            .GroupBy(si => si.ItemId)
            .OrderByDescending(g => g.Sum(x => x.Quantity))
            .Select(g => g.Key)
            .Take(take)
            .ToListAsync();

        if (topSellingIds.Count == 0)
        {
            // New store with no sales history yet - fall back to the first
            // `take` active items alphabetically so the screen isn't empty.
            return await _db.Items.Where(i => i.IsActive).OrderBy(i => i.Name).Take(take).ToListAsync();
        }

        var items = await _db.Items.Where(i => topSellingIds.Contains(i.Id) && i.IsActive).ToListAsync();
        // Preserve best-seller order (the query above already ranked them).
        return topSellingIds.Select(id => items.FirstOrDefault(i => i.Id == id)).Where(i => i != null).Cast<Item>().ToList();
    }

    // Instant search / barcode scanning. A barcode scanner just types the
    // code fast and sends Enter - the view treats an exact barcode/code
    // match specially (auto-add without waiting for a click).
    [HttpGet]
    public async Task<IActionResult> SearchItems(string term)
    {
        term ??= string.Empty;
        var termLower = term.ToLower();

        // .ToLower() on both sides translates to SQL LOWER(), which behaves
        // consistently on SQLite and PostgreSQL alike - a plain .Contains()
        // is case-insensitive on SQLite's default collation but
        // case-SENSITIVE on PostgreSQL, which is what was causing
        // "Sample" to match but "sample" not to.
        var items = await _db.Items
            .Where(i => i.IsActive && (i.Name.ToLower().Contains(termLower) || (i.Barcode ?? "") == term || (i.ItemCode ?? "").ToLower().Contains(termLower)))
            .Take(24)
            .Select(i => new
            {
                i.Id,
                i.Name,
                i.SellingPrice,
                i.GstPercent,
                i.CurrentStock,
                i.Barcode,
                i.ItemCode,
                i.ImagePath,
                ExactMatch = i.Barcode == term || i.ItemCode == term
            })
            .ToListAsync();

        return Json(items);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CreateSale([FromBody] CreateSaleRequest request, bool hold = false)
    {
        if (request.Lines.Count == 0)
            return BadRequest("Bill must contain at least one item.");

        var storeId = _tenant.StoreId ?? 0;
        var store = await _db.Stores.FindAsync(storeId);
        if (store == null) return BadRequest("Store not found.");

        // ---- Stock Validation (Settings-configurable) ----
        if (!hold && store.StockValidationEnabled)
        {
            var shortages = new List<string>();
            foreach (var line in request.Lines)
            {
                var item = await _db.Items.FindAsync(line.ItemId);
                if (item != null && item.CurrentStock < line.Quantity)
                    shortages.Add($"{item.Name} (available: {item.CurrentStock}, requested: {line.Quantity})");
            }
            if (shortages.Count > 0)
                return BadRequest("Insufficient stock for: " + string.Join("; ", shortages));
        }

        Customer? customer = request.CustomerId.HasValue ? await _db.Customers.FindAsync(request.CustomerId.Value) : null;
        var isInterState = GstCalculator.IsInterState(store.State, customer?.State);

        var sale = new Sale
        {
            StoreId = storeId,
            CustomerId = request.CustomerId,
            CashierUserId = _tenant.UserId ?? string.Empty,
            Notes = request.Notes,
            IsHeld = hold,
            SaleDate = DateTime.UtcNow,
            IsInterState = isInterState
        };

        decimal subTotal = 0, taxableTotal = 0, taxTotal = 0, cgstTotal = 0, sgstTotal = 0, igstTotal = 0;

        foreach (var line in request.Lines)
        {
            var item = await _db.Items.FindAsync(line.ItemId);
            if (item == null) continue;

            var gstPercent = store.GstEnabled ? item.GstPercent : 0;
            var gst = GstCalculator.Calculate(line.UnitPrice, line.Quantity, line.Discount, gstPercent, item.PriceType, store.GstEnabled, isInterState);

            subTotal += (line.UnitPrice * line.Quantity) - line.Discount;
            taxableTotal += gst.TaxableValue;
            taxTotal += gst.TaxAmount;
            cgstTotal += gst.CgstAmount;
            sgstTotal += gst.SgstAmount;
            igstTotal += gst.IgstAmount;

            string? batchInfo = null;

            if (!hold)
            {
                item.CurrentStock -= line.Quantity;

                if (store.BatchTrackingEnabled)
                {
                    var allocation = await _batchStock.AllocateForSaleAsync(storeId, item.Id, line.Quantity);
                    batchInfo = allocation.BatchInfo;
                }

                _db.InventoryTransactions.Add(new InventoryTransaction
                {
                    StoreId = storeId,
                    ItemId = item.Id,
                    TransactionType = InventoryTransactionType.Sale,
                    Quantity = -line.Quantity,
                    BalanceAfter = item.CurrentStock,
                    Notes = batchInfo != null ? $"Batches used: {batchInfo}" : null
                });
            }

            sale.SaleItems.Add(new SaleItem
            {
                ItemId = item.Id,
                Quantity = line.Quantity,
                UnitPrice = line.UnitPrice,
                Discount = line.Discount,
                GstPercent = gstPercent,
                PriceType = item.PriceType,
                TaxableValue = gst.TaxableValue,
                TaxAmount = gst.TaxAmount,
                CgstAmount = gst.CgstAmount,
                SgstAmount = gst.SgstAmount,
                IgstAmount = gst.IgstAmount,
                LineTotal = gst.LineTotal,
                BatchInfo = batchInfo
            });
        }

        var grandTotalRaw = subTotal - request.OverallDiscount + taxTotal;
        var grandTotal = Math.Round(grandTotalRaw, 0, MidpointRounding.AwayFromZero);

        sale.SubTotal = subTotal;
        sale.DiscountAmount = request.OverallDiscount;
        sale.TaxableAmount = taxableTotal;
        sale.TaxAmount = taxTotal;
        sale.CgstAmount = cgstTotal;
        sale.SgstAmount = sgstTotal;
        sale.IgstAmount = igstTotal;
        sale.RoundOff = grandTotal - grandTotalRaw;
        sale.GrandTotal = grandTotal;
        sale.InvoiceNumber = await NextInvoiceNumberAsync(store);

        // ---- Split Payment / Credit Sale ----
        // Every payment line (Cash/Card/UPI/etc.) is recorded individually.
        // If the total collected is less than the grand total, the shortfall
        // becomes the customer's outstanding balance - this is what makes a
        // "Credit Sale" just the zero-payment edge case of the same mechanism,
        // rather than a separate code path.
        decimal totalPaid = 0;
        if (!hold)
        {
            foreach (var payment in request.Payments.Where(p => p.Amount > 0))
            {
                sale.Payments.Add(new SalePayment { PaymentModeId = payment.PaymentModeId, Amount = payment.Amount });
                totalPaid += payment.Amount;
            }

            sale.PaymentModeId = request.Payments.FirstOrDefault()?.PaymentModeId;
            sale.AmountPaid = totalPaid;

            if (totalPaid <= 0) sale.PaymentStatus = PaymentStatus.Unpaid;
            else if (totalPaid < grandTotal) sale.PaymentStatus = PaymentStatus.PartiallyPaid;
            else sale.PaymentStatus = PaymentStatus.Paid;

            var outstandingDelta = grandTotal - totalPaid;
            if (outstandingDelta > 0 && customer != null)
                customer.OutstandingAmount += outstandingDelta;
        }

        _db.Sales.Add(sale);
        await _db.SaveChangesAsync();
        await _activityLogger.LogAsync(hold ? "Bill held" : "Bill created", sale.InvoiceNumber);

        // Resuming a held bill and completing it creates a brand-new Sale
        // (simplest, safest way to reuse the exact same GST/stock/payment
        // logic above) - so the original held row must be cleaned up here,
        // otherwise it lingers in the Held Bills list forever as a ghost entry.
        if (!hold && request.ResumedFromHeldSaleId.HasValue)
        {
            var oldHeld = await _db.Sales.Include(s => s.SaleItems)
                .FirstOrDefaultAsync(s => s.Id == request.ResumedFromHeldSaleId.Value && s.IsHeld);
            if (oldHeld != null) _db.Sales.Remove(oldHeld);
            await _db.SaveChangesAsync();
        }

        if (!hold)
        {
            // Credit Sale accounting: only the amount actually collected right
            // now posts as a Credit (real cash in). Any unpaid portion posts
            // as a Debit under "Customer Receivable" - tracking money owed
            // rather than counting it as income before it's actually
            // collected. When that balance is later paid off via Customer
            // Outstanding Collection, that payment posts as a Credit
            // ("Outstanding Collection"), which is what brings the books
            // back to matching the full sale amount over time.
            var paidPortion = sale.AmountPaid;
            var creditPortion = sale.GrandTotal - sale.AmountPaid;
            var paymentModeName = (await _db.PaymentModes.FindAsync(sale.PaymentModeId))?.Name;

            if (paidPortion > 0)
            {
                await _accounting.PostAsync(storeId, $"Sale {sale.InvoiceNumber}", paidPortion,
                    Domain.Enums.TransactionDirection.Credit, "Sales Invoice",
                    sourceModule: "Sale", sourceId: sale.Id, referenceNumber: sale.InvoiceNumber,
                    paymentMethod: paymentModeName);
            }

            if (creditPortion > 0)
            {
                await _accounting.PostAsync(storeId, $"Credit sale {sale.InvoiceNumber} - amount receivable", creditPortion,
                    Domain.Enums.TransactionDirection.Debit, "Customer Receivable",
                    sourceModule: "Sale", sourceId: sale.Id, referenceNumber: sale.InvoiceNumber);
            }

            await TrySendInvoiceEmailAsync(sale, store);
        }

        return Json(new SaleResultDto { SaleId = sale.Id, InvoiceNumber = sale.InvoiceNumber, GrandTotal = sale.GrandTotal });
    }

    private async Task TrySendInvoiceEmailAsync(Sale sale, Store store)
    {
        try
        {
            if (!sale.CustomerId.HasValue) return;
            var customer = await _db.Customers.FindAsync(sale.CustomerId.Value);
            if (customer == null || string.IsNullOrWhiteSpace(customer.Email)) return;

            var lines = await _db.SaleItems.Include(si => si.Item)
                .Where(si => si.SaleId == sale.Id).ToListAsync();

            var html = InvoiceEmailHtmlBuilder.BuildSaleInvoiceHtml(store, sale, lines);

            var (success, error) = await _emailSender.SendInvoiceEmailAsync(
                sale.StoreId, customer.Email, $"Invoice {sale.InvoiceNumber} from {store.Name}", html);

            await _activityLogger.LogAsync(
                success ? "Invoice email sent" : "Invoice email failed",
                success ? sale.InvoiceNumber : $"{sale.InvoiceNumber}: {error}");
        }
        catch
        {
            // Deliberately swallowed - email delivery is best-effort and must never break checkout.
        }
    }

    // ---------- Hold / Recall / Suspend ----------
    // "Suspend" and "Hold" are the same underlying mechanism (a Sale row
    // with IsHeld=true and no stock/accounting impact) - standard POS
    // terminology overlap, not two separate features to build.

    [HttpGet]
    public async Task<IActionResult> GetHeldBills()
    {
        var heldBills = await _db.Sales
            .Include(s => s.Customer)
            .Include(s => s.SaleItems)
            .Where(s => s.IsHeld)
            .OrderByDescending(s => s.SaleDate)
            .Select(s => new HeldBillSummaryDto
            {
                SaleId = s.Id,
                InvoiceNumber = s.InvoiceNumber,
                CustomerName = s.Customer != null ? s.Customer.Name : "Walk-in",
                ItemCount = s.SaleItems.Count,
                GrandTotal = s.GrandTotal,
                HeldAt = s.SaleDate
            })
            .ToListAsync();

        return Json(heldBills);
    }

    [HttpGet]
    public async Task<IActionResult> ResumeHeldBill(int id)
    {
        var sale = await _db.Sales.Include(s => s.SaleItems).ThenInclude(si => si.Item)
            .FirstOrDefaultAsync(s => s.Id == id && s.IsHeld);
        if (sale == null) return NotFound();

        var dto = new HeldBillDetailDto
        {
            SaleId = sale.Id,
            InvoiceNumber = sale.InvoiceNumber,
            CustomerId = sale.CustomerId,
            OverallDiscount = sale.DiscountAmount,
            Lines = sale.SaleItems.Select(si => new HeldBillLineDto
            {
                ItemId = si.ItemId,
                ItemName = si.Item?.Name ?? "",
                Quantity = si.Quantity,
                UnitPrice = si.UnitPrice,
                Discount = si.Discount,
                GstPercent = si.GstPercent
            }).ToList()
        };

        return Json(dto);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteHeldBill(int id)
    {
        var sale = await _db.Sales.FirstOrDefaultAsync(s => s.Id == id && s.IsHeld);
        if (sale == null) return NotFound();

        _db.Sales.Remove(sale);
        await _db.SaveChangesAsync();
        await _activityLogger.LogAsync("Held bill discarded", sale.InvoiceNumber);
        return Ok();
    }

    // ---------- Print / Reprint / Duplicate Copy ----------
    // format: "thermal" or "a4"; defaults to Invoice Configuration's chosen printer type.
    public async Task<IActionResult> Print(int id, string? format = null)
    {
        var sale = await _db.Sales
            .Include(s => s.Customer)
            .Include(s => s.SaleItems).ThenInclude(si => si.Item)
            .Include(s => s.Payments).ThenInclude(p => p.PaymentMode)
            .FirstOrDefaultAsync(s => s.Id == id);
        if (sale == null) return NotFound();

        var store = await _db.Stores.FindAsync(sale.StoreId);
        var invoiceSettings = await _db.InvoiceSettingsEntries.FirstOrDefaultAsync(x => x.StoreId == sale.StoreId)
            ?? new InvoiceSettings { StoreId = sale.StoreId };

        // Every render of the print view counts as a physical/PDF print -
        // the second and later ones are watermarked "Duplicate Copy" so a
        // reprinted bill is never confused with the original.
        sale.PrintCount++;
        await _db.SaveChangesAsync();

        ViewBag.Store = store;
        ViewBag.InvoiceSettings = invoiceSettings;
        ViewBag.IsDuplicate = sale.PrintCount > 1;

        if (string.IsNullOrEmpty(format))
            format = invoiceSettings.DefaultPrinterType == PrinterType.A4 ? "a4" : "thermal";

        return format.Equals("a4", StringComparison.OrdinalIgnoreCase)
            ? View("PrintA4", sale)
            : View("PrintThermal", sale);
    }

   
private async Task<string> NextInvoiceNumberAsync(Store store)
{
    var prefix = $"{store.InvoicePrefix}-{DateTime.UtcNow:yyyyMM}-";

    var lastInvoice = await _db.Sales
        .IgnoreQueryFilters()
        .Where(s => s.StoreId == store.Id &&
                    s.InvoiceNumber.StartsWith(prefix))
        .OrderByDescending(s => s.InvoiceNumber)
        .Select(s => s.InvoiceNumber)
        .FirstOrDefaultAsync();

    int nextNumber = 1;

    if (!string.IsNullOrWhiteSpace(lastInvoice))
    {
        var lastPart = lastInvoice.Substring(prefix.Length);

        if (int.TryParse(lastPart, out int last))
            nextNumber = last + 1;
    }

    return $"{prefix}{nextNumber:D4}";
}


}
