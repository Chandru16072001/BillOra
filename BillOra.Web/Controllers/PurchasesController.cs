using BillOra.Application.Common.Interfaces;
using BillOra.Application.DTOs;
using BillOra.Domain.Entities;
using BillOra.Domain.Enums;
using BillOra.Persistence;
using BillOra.Shared.Constants;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

using BillOra.Web.Authorization;

namespace BillOra.Web.Controllers;

// SRS section 10 - Purchase Module, entered here as GRN (Goods Receipt Note).
// Receiving stock this way is one of the two supported stock-in methods,
// the other being the standalone Opening Stock Entry (see StockController).
[Authorize(Roles = Roles.StoreAdmin + "," + Roles.Manager)]
[RequireModule(ModuleKeys.Purchases)]
public class PurchasesController : Controller
{
    private readonly BillOraDbContext _db;
    private readonly ICurrentTenantService _tenant;
    private readonly IActivityLogger _activityLogger;
    private readonly IAccountingService _accounting;

    public PurchasesController(BillOraDbContext db, ICurrentTenantService tenant, IActivityLogger activityLogger, IAccountingService accounting)
    {
        _db = db;
        _tenant = tenant;
        _activityLogger = activityLogger;
        _accounting = accounting;
    }

    public async Task<IActionResult> Index()
    {
        var purchases = await _db.Purchases.Include(p => p.Vendor)
            .OrderByDescending(p => p.PurchaseDate).ToListAsync();
        return View(purchases);
    }

    public async Task<IActionResult> Create()
    {
        var store = await _db.Stores.FindAsync(_tenant.StoreId ?? 0);
        ViewBag.BatchTrackingEnabled = store?.BatchTrackingEnabled ?? false;
        ViewBag.Vendors = await _db.Vendors.Where(v => v.IsActive).OrderBy(v => v.Name).ToListAsync();
        ViewBag.Items = await _db.Items.Where(i => i.IsActive).OrderBy(i => i.Name).ToListAsync();
        return View();
    }

    [HttpGet]
    public async Task<IActionResult> SearchItems(string term)
    {
        term ??= string.Empty;
        var items = await _db.Items
            .Where(i => i.IsActive && (i.Name.Contains(term) || (i.ItemCode ?? "").Contains(term) || (i.Barcode ?? "") == term))
            .Take(20)
            .Select(i => new { i.Id, i.Name, i.PurchasePrice, i.GstPercent, i.CurrentStock })
            .ToListAsync();
        return Json(items);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CreateGrn([FromBody] CreateGrnRequest request)
    {
        if (request.Lines.Count == 0) return BadRequest("GRN must contain at least one item.");

        var vendor = await _db.Vendors.FindAsync(request.VendorId);
        if (vendor == null) return BadRequest("Vendor not found.");

        var storeId = _tenant.StoreId ?? 0;
        var store = await _db.Stores.FindAsync(storeId);

        var purchase = new Purchase
        {
            StoreId = storeId,
            VendorId = vendor.Id,
            InvoiceNumber = string.IsNullOrWhiteSpace(request.InvoiceNumber)
                ? await NextGrnNumberAsync(storeId)
                : request.InvoiceNumber!,
            PurchaseDate = DateTime.UtcNow
        };

        decimal subTotal = 0, taxTotal = 0;

        foreach (var line in request.Lines)
        {
            var item = await _db.Items.FindAsync(line.ItemId);
            if (item == null) continue;

            var lineBase = (line.UnitPrice * line.Quantity) - line.Discount;
            var lineTax = lineBase * line.GstPercent / 100;

            subTotal += lineBase;
            taxTotal += lineTax;

            purchase.PurchaseItems.Add(new PurchaseItem
            {
                ItemId = item.Id,
                Quantity = line.Quantity,
                UnitPrice = line.UnitPrice,
                Discount = line.Discount,
                GstPercent = line.GstPercent,
                LineTotal = lineBase + lineTax,
                BatchNumber = line.BatchNumber,
                ManufactureDate = line.ManufactureDate,
                ExpiryDate = line.ExpiryDate,
                SupplierBatchNumber = line.SupplierBatchNumber,
                SellingRate = line.SellingRate,
                BatchRemarks = line.BatchRemarks
            });

            // Receiving stock via GRN increases on-hand quantity and is logged
            // to the same inventory ledger the POS module debits from.
            item.CurrentStock += line.Quantity;
            item.PurchasePrice = line.UnitPrice; // keep last purchase price current
            if (line.SellingRate.HasValue) item.SellingPrice = line.SellingRate.Value;

            _db.InventoryTransactions.Add(new InventoryTransaction
            {
                StoreId = storeId,
                ItemId = item.Id,
                TransactionType = InventoryTransactionType.Purchase,
                Quantity = line.Quantity,
                BalanceAfter = item.CurrentStock,
                Notes = $"GRN {purchase.InvoiceNumber}" + (line.BatchNumber != null ? $" (batch {line.BatchNumber})" : "")
            });

            if (store?.BatchTrackingEnabled == true && !string.IsNullOrWhiteSpace(line.BatchNumber))
            {
                _db.StockBatches.Add(new StockBatch
                {
                    StoreId = storeId,
                    ItemId = item.Id,
                    BatchNumber = line.BatchNumber,
                    ManufactureDate = line.ManufactureDate,
                    ExpiryDate = line.ExpiryDate,
                    SupplierBatchNumber = line.SupplierBatchNumber,
                    PurchaseRate = line.UnitPrice,
                    SellingRate = line.SellingRate ?? item.SellingPrice,
                    Quantity = line.Quantity,
                    RemainingQuantity = line.Quantity,
                    Remarks = line.BatchRemarks,
                    SourceModule = "GRN"
                });
            }
        }

        purchase.SubTotal = subTotal;
        purchase.DiscountAmount = request.OverallDiscount;
        purchase.TaxAmount = taxTotal;
        purchase.GrandTotal = subTotal - request.OverallDiscount + taxTotal;

        _db.Purchases.Add(purchase);
        await _db.SaveChangesAsync();
        await _activityLogger.LogAsync("GRN received", purchase.InvoiceNumber);
        await _accounting.PostAsync(storeId, $"Purchase (GRN {purchase.InvoiceNumber})", purchase.GrandTotal,
            Domain.Enums.TransactionDirection.Debit, "Purchase Entry",
            sourceModule: "Purchase", sourceId: purchase.Id, referenceNumber: purchase.InvoiceNumber);

        return Json(new GrnResultDto { PurchaseId = purchase.Id, InvoiceNumber = purchase.InvoiceNumber, GrandTotal = purchase.GrandTotal });
    }

    public async Task<IActionResult> Details(int id)
    {
        var purchase = await _db.Purchases
            .Include(p => p.Vendor)
            .Include(p => p.PurchaseItems).ThenInclude(pi => pi.Item)
            .FirstOrDefaultAsync(p => p.Id == id);
        if (purchase == null) return NotFound();
        return View(purchase);
    }

    private async Task<string> NextGrnNumberAsync(int storeId)
    {
        var count = await _db.Purchases.IgnoreQueryFilters().CountAsync(p => p.StoreId == storeId) + 1;
        return $"GRN-{DateTime.UtcNow:yyyyMM}-{count:D4}";
    }
}
