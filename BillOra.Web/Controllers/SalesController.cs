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

// Sales Details screen: search by date range / customer / invoice number,
// view a sale in full, print, email, and (Store Admin only) modify items
// and quantities on an already-saved invoice.
[Authorize(Roles = Roles.StoreAdmin + "," + Roles.Manager + "," + Roles.Cashier)]
[RequireModule(ModuleKeys.Pos)]
public class SalesController : Controller
{
    private readonly BillOraDbContext _db;
    private readonly ICurrentTenantService _tenant;
    private readonly IActivityLogger _activityLogger;
    private readonly IAccountingService _accounting;
    private readonly IEmailSender _emailSender;

    public SalesController(BillOraDbContext db, ICurrentTenantService tenant, IActivityLogger activityLogger,
        IAccountingService accounting, IEmailSender emailSender)
    {
        _db = db;
        _tenant = tenant;
        _activityLogger = activityLogger;
        _accounting = accounting;
        _emailSender = emailSender;
    }

    public async Task<IActionResult> Index(DateTime? from, DateTime? to, string? customer, string? invoiceNumber)
    {
        var query = _db.Sales.Include(s => s.Customer).Where(s => !s.IsHeld).AsQueryable();

        if (from.HasValue) query = query.Where(s => s.SaleDate >= from.Value);
        if (to.HasValue) query = query.Where(s => s.SaleDate <= to.Value.AddDays(1).AddTicks(-1));
        if (!string.IsNullOrWhiteSpace(customer)) query = query.Where(s => s.Customer != null && s.Customer.Name.Contains(customer));
        if (!string.IsNullOrWhiteSpace(invoiceNumber)) query = query.Where(s => s.InvoiceNumber.Contains(invoiceNumber));

        ViewBag.From = from; ViewBag.To = to; ViewBag.Customer = customer; ViewBag.InvoiceNumber = invoiceNumber;

        var sales = await query.OrderByDescending(s => s.SaleDate).Take(200).ToListAsync();
        return View(sales);
    }

    public async Task<IActionResult> Details(int id)
    {
        var sale = await _db.Sales
            .Include(s => s.Customer)
            .Include(s => s.PaymentMode)
            .Include(s => s.SaleItems).ThenInclude(si => si.Item)
            .FirstOrDefaultAsync(s => s.Id == id);
        if (sale == null) return NotFound();

        ViewBag.Returns = await _db.SalesReturns.Where(sr => sr.SaleId == id).ToListAsync();
        return View(sale);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Email(int id)
    {
        var sale = await _db.Sales
            .Include(s => s.Customer)
            .Include(s => s.SaleItems).ThenInclude(si => si.Item)
            .FirstOrDefaultAsync(s => s.Id == id);

        if (sale == null || string.IsNullOrWhiteSpace(sale.Customer?.Email))
        {
            TempData["Error"] = "No customer email on file for this invoice.";
            return RedirectToAction(nameof(Details), new { id });
        }

        var store = await _db.Stores.FindAsync(sale.StoreId);
        var html = Utils.InvoiceEmailHtmlBuilder.BuildSaleInvoiceHtml(store!, sale, sale.SaleItems);

        var (success, error) = await _emailSender.SendInvoiceEmailAsync(
            sale.StoreId, sale.Customer.Email, $"Invoice {sale.InvoiceNumber} from {store!.Name}", html);

        TempData[success ? "Success" : "Error"] = success ? "Invoice emailed." : $"Could not send: {error}";
        return RedirectToAction(nameof(Details), new { id });
    }

    [HttpGet]
    [Authorize(Roles = Roles.StoreAdmin)]
    public async Task<IActionResult> Edit(int id)
    {
        var sale = await _db.Sales.Include(s => s.SaleItems).ThenInclude(si => si.Item).FirstOrDefaultAsync(s => s.Id == id);
        if (sale == null) return NotFound();

        ViewBag.Items = await _db.Items.Where(i => i.IsActive).OrderBy(i => i.Name).ToListAsync();
        ViewBag.PaymentModes = await _db.PaymentModes.Where(p => p.IsActive).ToListAsync();
        ViewBag.Customers = await _db.Customers.Where(c => c.IsActive).OrderBy(c => c.Name).ToListAsync();
        return View(sale);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Roles = Roles.StoreAdmin)]
    public async Task<IActionResult> Edit(int id, [FromBody] EditSaleRequest request)
    {
        var sale = await _db.Sales.Include(s => s.SaleItems).FirstOrDefaultAsync(s => s.Id == id);
        if (sale == null) return NotFound();
        if (request.Lines.Count == 0) return BadRequest("A sale must have at least one item.");

        var storeId = sale.StoreId;
        var store = await _db.Stores.FindAsync(storeId);

        // Undo the original stock impact before applying the edited lines.
        foreach (var oldLine in sale.SaleItems)
        {
            var item = await _db.Items.FindAsync(oldLine.ItemId);
            if (item == null) continue;
            item.CurrentStock += oldLine.Quantity;
            _db.InventoryTransactions.Add(new InventoryTransaction
            {
                StoreId = storeId,
                ItemId = item.Id,
                TransactionType = InventoryTransactionType.Adjustment,
                Quantity = oldLine.Quantity,
                BalanceAfter = item.CurrentStock,
                Notes = $"Reverted for edit of {sale.InvoiceNumber}"
            });
        }

        _db.SaleItems.RemoveRange(sale.SaleItems);

        var customer = request.CustomerId.HasValue ? await _db.Customers.FindAsync(request.CustomerId.Value) : null;
        var isInterState = Utils.GstCalculator.IsInterState(store!.State, customer?.State);

        decimal subTotal = 0, taxableTotal = 0, taxTotal = 0, cgstTotal = 0, sgstTotal = 0, igstTotal = 0;
        var newLines = new List<SaleItem>();

        foreach (var line in request.Lines)
        {
            var item = await _db.Items.FindAsync(line.ItemId);
            if (item == null) continue;

            var gstPercent = store.GstEnabled ? item.GstPercent : 0;
            var gst = Utils.GstCalculator.Calculate(line.UnitPrice, line.Quantity, line.Discount, gstPercent, item.PriceType, store.GstEnabled, isInterState);

            subTotal += (line.UnitPrice * line.Quantity) - line.Discount;
            taxableTotal += gst.TaxableValue;
            taxTotal += gst.TaxAmount;
            cgstTotal += gst.CgstAmount;
            sgstTotal += gst.SgstAmount;
            igstTotal += gst.IgstAmount;

            newLines.Add(new SaleItem
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
                LineTotal = gst.LineTotal
            });

            item.CurrentStock -= line.Quantity;
            _db.InventoryTransactions.Add(new InventoryTransaction
            {
                StoreId = storeId,
                ItemId = item.Id,
                TransactionType = InventoryTransactionType.Adjustment,
                Quantity = -line.Quantity,
                BalanceAfter = item.CurrentStock,
                Notes = $"Reapplied for edit of {sale.InvoiceNumber}"
            });
        }

        foreach (var l in newLines) sale.SaleItems.Add(l);

        var grandTotalRaw = subTotal - request.OverallDiscount + taxTotal;
        var grandTotal = Math.Round(grandTotalRaw, 0, MidpointRounding.AwayFromZero);

        sale.CustomerId = request.CustomerId;
        sale.PaymentModeId = request.PaymentModeId;
        sale.Notes = request.Notes;
        sale.SubTotal = subTotal;
        sale.DiscountAmount = request.OverallDiscount;
        sale.TaxableAmount = taxableTotal;
        sale.TaxAmount = taxTotal;
        sale.CgstAmount = cgstTotal;
        sale.SgstAmount = sgstTotal;
        sale.IgstAmount = igstTotal;
        sale.IsInterState = isInterState;
        sale.RoundOff = grandTotal - grandTotalRaw;
        sale.GrandTotal = grandTotal;

        await _db.SaveChangesAsync();

        // Reverse the original ledger entry and post a fresh one for the corrected amount.
        await _accounting.ReverseAsync("Sale", sale.Id);
        await _accounting.PostAsync(storeId, $"Sale {sale.InvoiceNumber} (modified)", sale.GrandTotal,
            TransactionDirection.Credit, "Sales Invoice", sourceModule: "Sale", sourceId: sale.Id,
            referenceNumber: sale.InvoiceNumber);

        await _activityLogger.LogAsync("Bill modified", $"{sale.InvoiceNumber} - new total ₹{sale.GrandTotal:N2}");

        return Json(new { saleId = sale.Id, invoiceNumber = sale.InvoiceNumber, grandTotal = sale.GrandTotal });
    }
}
