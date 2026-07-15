using BillOra.Application.Common.Interfaces;
using BillOra.Domain.Entities;
using BillOra.Domain.Enums;
using BillOra.Persistence;
using BillOra.Shared.Constants;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

using BillOra.Web.Authorization;

namespace BillOra.Web.Controllers;

// SRS section 9 - Inventory Module. This covers the second stock-receiving
// method requested alongside GRN: a direct Opening Stock Entry, for setting
// or topping up an item's stock without going through a vendor purchase
// (e.g. initial stock load, or a manual correction). When the store has
// Batch Tracking enabled, each entry also creates a StockBatch row.
[Authorize(Roles = Roles.StoreAdmin + "," + Roles.Manager)]
[RequireModule(ModuleKeys.Stock)]
public class StockController : Controller
{
    private readonly BillOraDbContext _db;
    private readonly ICurrentTenantService _tenant;

    public StockController(BillOraDbContext db, ICurrentTenantService tenant)
    {
        _db = db;
        _tenant = tenant;
    }

    public async Task<IActionResult> OpeningStock()
    {
        var store = await _db.Stores.FindAsync(_tenant.StoreId ?? 0);
        ViewBag.BatchTrackingEnabled = store?.BatchTrackingEnabled ?? false;
        ViewBag.Items = await _db.Items.Where(i => i.IsActive).OrderBy(i => i.Name).ToListAsync();
        ViewBag.RecentEntries = await _db.InventoryTransactions
            .Include(t => t.Item)
            .Where(t => t.TransactionType == InventoryTransactionType.OpeningStock)
            .OrderByDescending(t => t.TransactionDate)
            .Take(20)
            .ToListAsync();
        return View();
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> OpeningStock(int itemId, decimal quantity, string? notes,
        string? batchNumber, DateTime? manufactureDate, DateTime? expiryDate, decimal? purchaseRate, decimal? sellingRate)
    {
        var item = await _db.Items.FindAsync(itemId);
        if (item == null || quantity <= 0)
        {
            TempData["Error"] = "Select an item and enter a valid quantity.";
            return RedirectToAction(nameof(OpeningStock));
        }

        var storeId = _tenant.StoreId ?? 0;
        var store = await _db.Stores.FindAsync(storeId);

        item.CurrentStock += quantity;
        _db.InventoryTransactions.Add(new InventoryTransaction
        {
            StoreId = storeId,
            ItemId = item.Id,
            TransactionType = InventoryTransactionType.OpeningStock,
            Quantity = quantity,
            BalanceAfter = item.CurrentStock,
            Notes = notes
        });

        if (store?.BatchTrackingEnabled == true && !string.IsNullOrWhiteSpace(batchNumber))
        {
            _db.StockBatches.Add(new StockBatch
            {
                StoreId = storeId,
                ItemId = item.Id,
                BatchNumber = batchNumber,
                ManufactureDate = manufactureDate,
                ExpiryDate = expiryDate,
                PurchaseRate = purchaseRate ?? item.PurchasePrice,
                SellingRate = sellingRate ?? item.SellingPrice,
                Quantity = quantity,
                RemainingQuantity = quantity,
                Remarks = notes,
                SourceModule = "OpeningStock"
            });
        }

        await _db.SaveChangesAsync();

        TempData["Success"] = $"Added {quantity} to {item.Name}. New stock: {item.CurrentStock}.";
        return RedirectToAction(nameof(OpeningStock));
    }

    // Full stock ledger for an item (Stock In / Out / Sale / Purchase / Adjustment history).
    public async Task<IActionResult> History(int itemId)
    {
        var item = await _db.Items.FindAsync(itemId);
        if (item == null) return NotFound();

        ViewBag.Item = item;
        var history = await _db.InventoryTransactions
            .Where(t => t.ItemId == itemId)
            .OrderByDescending(t => t.TransactionDate)
            .ToListAsync();
        return View(history);
    }

    // Batch list for an item - expiry tracking (only relevant when batch tracking is on).
    public async Task<IActionResult> Batches(int itemId)
    {
        var item = await _db.Items.FindAsync(itemId);
        if (item == null) return NotFound();

        ViewBag.Item = item;
        var batches = await _db.StockBatches
            .Where(b => b.ItemId == itemId)
            .OrderBy(b => b.ExpiryDate)
            .ToListAsync();
        return View(batches);
    }
}
