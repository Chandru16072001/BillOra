using BillOra.Application.Common.Interfaces;
using BillOra.Application.DTOs;
using BillOra.Domain.Entities;
using BillOra.Domain.Enums;
using BillOra.Persistence;
using BillOra.Shared.Constants;
using BillOra.Web.Authorization;
using BillOra.Web.Utils;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace BillOra.Web.Controllers;

// Painting Shop Quotation workflow: Room/Wall Estimator -> Quotation ->
// Discount Approval (if the discount exceeds the store's threshold) ->
// Convert to Sale. Converting reuses the exact same GST/stock-validation/
// batch/accounting pipeline as POS and restaurant Orders, so a painting
// shop's sales look and behave identically to any other sale everywhere
// else in the app (Reports, Accounts, Dashboard).
[Authorize(Roles = Roles.StoreAdmin + "," + Roles.Manager + "," + Roles.Cashier)]
[RequireModule(ModuleKeys.Quotations)]
[RequirePaintingShop]
public class QuotationsController : Controller
{
    private readonly BillOraDbContext _db;
    private readonly ICurrentTenantService _tenant;
    private readonly IActivityLogger _activityLogger;
    private readonly IAccountingService _accounting;
    private readonly IBatchStockService _batchStock;
    private readonly IEmailSender _emailSender;

    public QuotationsController(BillOraDbContext db, ICurrentTenantService tenant, IActivityLogger activityLogger,
        IAccountingService accounting, IBatchStockService batchStock, IEmailSender emailSender)
    {
        _db = db;
        _tenant = tenant;
        _activityLogger = activityLogger;
        _accounting = accounting;
        _batchStock = batchStock;
        _emailSender = emailSender;
    }

    public async Task<IActionResult> Index(string? customer, string? status)
    {
        var query = _db.Quotations.Include(q => q.Customer).AsQueryable();
        if (!string.IsNullOrWhiteSpace(customer)) query = query.Where(q => q.Customer != null && q.Customer.Name.Contains(customer));
        if (!string.IsNullOrWhiteSpace(status) && Enum.TryParse<QuotationStatus>(status, out var st)) query = query.Where(q => q.Status == st);

        ViewBag.Customer = customer;
        ViewBag.Status = status;
        return View(await query.OrderByDescending(q => q.QuotationDate).ToListAsync());
    }

    [HttpGet]
    public async Task<IActionResult> Create()
    {
        ViewBag.Items = await _db.Items.Where(i => i.IsActive).OrderBy(i => i.Name).ToListAsync();
        ViewBag.Customers = await _db.Customers.Where(c => c.IsActive).OrderBy(c => c.Name).ToListAsync();
        return View();
    }

    [HttpGet]
    public async Task<IActionResult> ShadesForItem(int itemId)
    {
        var shades = await _db.ShadeColors
            .Where(s => s.ItemId == itemId && s.IsActive)
            .OrderBy(s => s.ShadeName)
            .Select(s => new { s.Id, s.ShadeCode, s.ShadeName, s.HexColor })
            .ToListAsync();
        return Json(shades);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create([FromBody] CreateQuotationRequest request)
    {
        if (request.Lines.Count == 0) return BadRequest("Add at least one item to the quotation.");

        var storeId = _tenant.StoreId ?? 0;
        var store = await _db.Stores.FindAsync(storeId);
        if (store == null) return BadRequest("Store not found.");

        var quotation = new Quotation
        {
            StoreId = storeId,
            QuotationNumber = await NextQuotationNumberAsync(storeId),
            CustomerId = request.CustomerId,
            ValidUntil = request.ValidUntil,
            Notes = request.Notes,
            Status = QuotationStatus.Draft
        };

        decimal subTotal = 0, discountTotal = 0, taxTotal = 0;

        foreach (var line in request.Lines)
        {
            var item = await _db.Items.FindAsync(line.ItemId);
            if (item == null) continue;

            var lineBase = (line.UnitPrice * line.Quantity) - line.Discount;
            var gstPercent = store.GstEnabled ? item.GstPercent : 0;
            var lineTax = lineBase * gstPercent / 100;

            subTotal += line.UnitPrice * line.Quantity;
            discountTotal += line.Discount;
            taxTotal += lineTax;

            quotation.Items.Add(new QuotationItem
            {
                ItemId = item.Id,
                ShadeColorId = line.ShadeColorId,
                Quantity = line.Quantity,
                UnitPrice = line.UnitPrice,
                Discount = line.Discount,
                LineTotal = lineBase + lineTax,
                RoomName = line.RoomName,
                WallPerimeterFt = line.WallPerimeterFt,
                WallHeightFt = line.WallHeightFt,
                Doors = line.Doors,
                Windows = line.Windows,
                Coats = line.Coats,
                WastagePercent = line.WastagePercent,
                CoverageRateUsed = line.CoverageRateUsed
            });
        }

        var grandTotal = subTotal - discountTotal + taxTotal;
        var discountPercent = subTotal > 0 ? (discountTotal / subTotal) * 100 : 0;
        var requiresApproval = discountPercent > store.MaxDiscountPercentWithoutApproval;

        quotation.SubTotal = subTotal;
        quotation.DiscountAmount = discountTotal;
        quotation.TaxAmount = taxTotal;
        quotation.GrandTotal = grandTotal;
        quotation.DiscountRequiresApproval = requiresApproval;
        quotation.DiscountApproved = !requiresApproval; // no approval needed => treated as approved

        _db.Quotations.Add(quotation);
        await _db.SaveChangesAsync();
        await _activityLogger.LogAsync("Quotation created", quotation.QuotationNumber);

        return Json(new QuotationResultDto
        {
            QuotationId = quotation.Id,
            QuotationNumber = quotation.QuotationNumber,
            RequiresDiscountApproval = requiresApproval
        });
    }

    public async Task<IActionResult> Details(int id)
    {
        var quotation = await _db.Quotations
            .Include(q => q.Customer)
            .Include(q => q.Items).ThenInclude(i => i.Item)
            .Include(q => q.Items).ThenInclude(i => i.ShadeColor)
            .FirstOrDefaultAsync(q => q.Id == id);
        if (quotation == null) return NotFound();

        ViewBag.PaymentModes = await _db.PaymentModes.Where(p => p.IsActive).ToListAsync();
        return View(quotation);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Roles = Roles.StoreAdmin)] // only Admin approves discounts over threshold
    public async Task<IActionResult> ApproveDiscount(int id)
    {
        var quotation = await _db.Quotations.FindAsync(id);
        if (quotation == null) return NotFound();

        quotation.DiscountApproved = true;
        quotation.Status = QuotationStatus.Approved;
        await _db.SaveChangesAsync();
        await _activityLogger.LogAsync("Quotation discount approved", quotation.QuotationNumber);

        TempData["Success"] = $"Discount approved for {quotation.QuotationNumber}.";
        return RedirectToAction(nameof(Details), new { id });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Reject(int id)
    {
        var quotation = await _db.Quotations.FindAsync(id);
        if (quotation == null) return NotFound();

        quotation.Status = QuotationStatus.Rejected;
        await _db.SaveChangesAsync();
        TempData["Success"] = $"{quotation.QuotationNumber} marked as rejected.";
        return RedirectToAction(nameof(Details), new { id });
    }

    // Converts the quotation into a real Sale - same GST/stock/batch/accounting
    // pipeline as everywhere else in the app.
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ConvertToSale(int id, [FromBody] ConvertQuotationRequest request)
    {
        var quotation = await _db.Quotations
            .Include(q => q.Customer)
            .Include(q => q.Items).ThenInclude(i => i.Item)
            .FirstOrDefaultAsync(q => q.Id == id);
        if (quotation == null) return NotFound();
        if (quotation.Status == QuotationStatus.Converted) return BadRequest("This quotation has already been converted.");
        if (quotation.DiscountRequiresApproval && !quotation.DiscountApproved)
            return BadRequest("This quotation's discount needs Admin approval before it can be billed.");

        var storeId = quotation.StoreId;
        var store = await _db.Stores.FindAsync(storeId);
        if (store == null) return BadRequest("Store not found.");

        if (store.StockValidationEnabled)
        {
            var shortages = new List<string>();
            foreach (var line in quotation.Items)
            {
                var item = line.Item ?? await _db.Items.FindAsync(line.ItemId);
                if (item != null && item.CurrentStock < line.Quantity)
                    shortages.Add($"{item.Name} (available: {item.CurrentStock}, needed: {line.Quantity})");
            }
            if (shortages.Count > 0) return BadRequest("Insufficient stock for: " + string.Join("; ", shortages));
        }

        var isInterState = GstCalculator.IsInterState(store.State, quotation.Customer?.State);

        var sale = new Sale
        {
            StoreId = storeId,
            CustomerId = quotation.CustomerId,
            CashierUserId = _tenant.UserId ?? string.Empty,
            PaymentModeId = request.PaymentModeId,
            SaleDate = DateTime.UtcNow,
            IsInterState = isInterState,
            Notes = $"Converted from Quotation {quotation.QuotationNumber}"
        };

        decimal subTotal = 0, taxableTotal = 0, taxTotal = 0, cgstTotal = 0, sgstTotal = 0, igstTotal = 0;

        foreach (var line in quotation.Items)
        {
            var item = line.Item ?? await _db.Items.FindAsync(line.ItemId);
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
                Notes = $"Quotation {quotation.QuotationNumber}" + (batchInfo != null ? $" | Batches: {batchInfo}" : "")
            });

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

        var grandTotalRaw = subTotal + taxTotal;
        var grandTotal = Math.Round(grandTotalRaw, 0, MidpointRounding.AwayFromZero);

        sale.SubTotal = subTotal;
        sale.DiscountAmount = quotation.DiscountAmount;
        sale.TaxableAmount = taxableTotal;
        sale.TaxAmount = taxTotal;
        sale.CgstAmount = cgstTotal;
        sale.SgstAmount = sgstTotal;
        sale.IgstAmount = igstTotal;
        sale.RoundOff = grandTotal - grandTotalRaw;
        sale.GrandTotal = grandTotal;
        sale.InvoiceNumber = await NextInvoiceNumberAsync(store);

        _db.Sales.Add(sale);
        quotation.Status = QuotationStatus.Converted;
        await _db.SaveChangesAsync();
        quotation.ConvertedSaleId = sale.Id;
        await _db.SaveChangesAsync();

        await _accounting.PostAsync(storeId, $"Sale {sale.InvoiceNumber} (Quotation {quotation.QuotationNumber})", sale.GrandTotal,
            TransactionDirection.Credit, "Sales Invoice", sourceModule: "Sale", sourceId: sale.Id,
            referenceNumber: sale.InvoiceNumber, paymentMethod: (await _db.PaymentModes.FindAsync(sale.PaymentModeId))?.Name);

        await _activityLogger.LogAsync("Quotation converted to sale", $"{quotation.QuotationNumber} -> {sale.InvoiceNumber}");

        if (quotation.CustomerId.HasValue)
        {
            try
            {
                var customer = await _db.Customers.FindAsync(quotation.CustomerId.Value);
                if (customer != null && !string.IsNullOrWhiteSpace(customer.Email))
                {
                    var html = InvoiceEmailHtmlBuilder.BuildSaleInvoiceHtml(store, sale, sale.SaleItems);
                    await _emailSender.SendInvoiceEmailAsync(storeId, customer.Email, $"Invoice {sale.InvoiceNumber} from {store.Name}", html);
                }
            }
            catch { /* best-effort, never fail billing over email */ }
        }

        return Json(new ConvertQuotationResultDto { SaleId = sale.Id, InvoiceNumber = sale.InvoiceNumber, GrandTotal = sale.GrandTotal });
    }

    public async Task<IActionResult> Print(int id)
    {
        var quotation = await _db.Quotations
            .Include(q => q.Customer)
            .Include(q => q.Items).ThenInclude(i => i.Item)
            .Include(q => q.Items).ThenInclude(i => i.ShadeColor)
            .FirstOrDefaultAsync(q => q.Id == id);
        if (quotation == null) return NotFound();

        ViewBag.Store = await _db.Stores.FindAsync(quotation.StoreId);
        return View(quotation);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Email(int id)
    {
        var quotation = await _db.Quotations
            .Include(q => q.Customer)
            .Include(q => q.Items).ThenInclude(i => i.Item)
            .FirstOrDefaultAsync(q => q.Id == id);

        if (quotation == null || string.IsNullOrWhiteSpace(quotation.Customer?.Email))
        {
            TempData["Error"] = "No customer email on file for this quotation.";
            return RedirectToAction(nameof(Details), new { id });
        }

        var store = await _db.Stores.FindAsync(quotation.StoreId);
        var rows = string.Join("", quotation.Items.Select(l =>
            $"<tr><td>{l.Item?.Name}</td><td style='text-align:right'>{l.Quantity}</td><td style='text-align:right'>Rs.{l.LineTotal:N2}</td></tr>"));
        var html = $@"<div style='font-family:Arial,sans-serif;max-width:480px;margin:auto;'>
            <h2>Quotation {quotation.QuotationNumber}</h2>
            <p>Valid until: {quotation.ValidUntil:dd MMM yyyy}</p>
            <table style='width:100%;border-collapse:collapse;' cellpadding='6'>
                <thead><tr style='background:#f5f6fb;'><th style='text-align:left'>Item</th><th>Qty</th><th>Amount</th></tr></thead>
                <tbody>{rows}</tbody>
            </table>
            <h3 style='text-align:right'>Total: Rs.{quotation.GrandTotal:N2}</h3>
        </div>";

        var (success, error) = await _emailSender.SendInvoiceEmailAsync(
            quotation.StoreId, quotation.Customer.Email, $"Quotation {quotation.QuotationNumber} from {store?.Name}", html);

        TempData[success ? "Success" : "Error"] = success ? "Quotation emailed." : $"Could not send: {error}";
        return RedirectToAction(nameof(Details), new { id });
    }

    private async Task<string> NextQuotationNumberAsync(int storeId)
    {
        var count = await _db.Quotations.IgnoreQueryFilters().CountAsync(q => q.StoreId == storeId) + 1;
        return $"QTN-{DateTime.UtcNow:yyyyMM}-{count:D4}";
    }

    private async Task<string> NextInvoiceNumberAsync(Store store)
    {
        var count = await _db.Sales.IgnoreQueryFilters().CountAsync(s => s.StoreId == store.Id) + 1;
        return $"{store.InvoicePrefix}-{DateTime.UtcNow:yyyyMM}-{count:D4}";
    }
}
