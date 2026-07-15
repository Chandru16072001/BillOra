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

    public async Task<IActionResult> Index()
    {
        var storeId = _tenant.StoreId ?? 0;
        var store = await _db.Stores.FindAsync(storeId);

        ViewBag.Items = await _db.Items.Where(i => i.IsActive)
            .OrderBy(i => i.Name).ToListAsync();
        ViewBag.PaymentModes = await _db.PaymentModes.Where(p => p.IsActive).ToListAsync();
        ViewBag.Customers = await _db.Customers.Where(c => c.IsActive).OrderBy(c => c.Name).ToListAsync();
        ViewBag.GstEnabled = store?.GstEnabled ?? true;
        ViewBag.StockValidationEnabled = store?.StockValidationEnabled ?? true;
        ViewBag.HeldBills = await _db.Sales.Where(s => s.IsHeld).OrderByDescending(s => s.SaleDate).ToListAsync();

        return View();
    }

    // Product search / barcode lookup used by the billing screen's AJAX search box.
    [HttpGet]
    public async Task<IActionResult> SearchItems(string term)
    {
        term ??= string.Empty;
        var items = await _db.Items
            .Where(i => i.IsActive && (i.Name.Contains(term) || (i.Barcode ?? "") == term || (i.ItemCode ?? "").Contains(term)))
            .Take(20)
            .Select(i => new
            {
                i.Id,
                i.Name,
                i.SellingPrice,
                i.GstPercent,
                i.CurrentStock,
                i.Barcode,
                i.ImagePath
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
        // Held bills don't touch stock, so they're exempt.
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
            PaymentModeId = request.PaymentModeId,
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

        _db.Sales.Add(sale);
        await _db.SaveChangesAsync();
        await _activityLogger.LogAsync(hold ? "Bill held" : "Bill created", sale.InvoiceNumber);

        if (!hold)
        {
            // Every completed sale is automatically a Credit in the Mini Accounts ledger.
            await _accounting.PostAsync(storeId, $"Sale {sale.InvoiceNumber}", sale.GrandTotal,
                Domain.Enums.TransactionDirection.Credit,
                "Sales Invoice", sourceModule: "Sale", sourceId: sale.Id, referenceNumber: sale.InvoiceNumber,
                paymentMethod: (await _db.PaymentModes.FindAsync(sale.PaymentModeId))?.Name);

            // Awaited (not true fire-and-forget) since the DbContext is disposed
            // at the end of the request; the try/catch inside still guarantees
            // an email failure never fails the sale itself.
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

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ResumeHeldBill(int id)
    {
        var sale = await _db.Sales.Include(s => s.SaleItems).FirstOrDefaultAsync(s => s.Id == id && s.IsHeld);
        if (sale == null) return NotFound();
        return Json(sale);
    }

    // format: "thermal" or "a4"; defaults to Invoice Configuration's chosen printer type.
    public async Task<IActionResult> Print(int id, string? format = null)
    {
        var sale = await _db.Sales
            .Include(s => s.Customer)
            .Include(s => s.SaleItems).ThenInclude(si => si.Item)
            .FirstOrDefaultAsync(s => s.Id == id);
        if (sale == null) return NotFound();

        var store = await _db.Stores.FindAsync(sale.StoreId);
        var invoiceSettings = await _db.InvoiceSettingsEntries.FirstOrDefaultAsync(x => x.StoreId == sale.StoreId)
            ?? new InvoiceSettings { StoreId = sale.StoreId }; // sensible defaults if never configured

        ViewBag.Store = store;
        ViewBag.InvoiceSettings = invoiceSettings;

        if (string.IsNullOrEmpty(format))
            format = invoiceSettings.DefaultPrinterType == PrinterType.A4 ? "a4" : "thermal";

        return format.Equals("a4", StringComparison.OrdinalIgnoreCase)
            ? View("PrintA4", sale)
            : View("PrintThermal", sale);
    }

    private async Task<string> NextInvoiceNumberAsync(Store store)
    {
        var count = await _db.Sales.IgnoreQueryFilters().CountAsync(s => s.StoreId == store.Id) + 1;
        return $"{store.InvoicePrefix}-{DateTime.UtcNow:yyyyMM}-{count:D4}";
    }
}
