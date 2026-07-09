using BillOra.Application.Common.Interfaces;
using BillOra.Domain.Entities;
using BillOra.Persistence;
using BillOra.Shared.Constants;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

using BillOra.Web.Authorization;

namespace BillOra.Web.Controllers;

// SRS section 7 - Price Master: original/selling/discount price plus an
// offer date window, kept separate from the item's standing price so
// promotions can be scheduled without touching Item.SellingPrice directly.
[Authorize(Roles = Roles.StoreAdmin + "," + Roles.Manager)]
[RequireModule(ModuleKeys.ItemPrices)]
public class ItemPricesController : Controller
{
    private readonly BillOraDbContext _db;
    private readonly ICurrentTenantService _tenant;

    public ItemPricesController(BillOraDbContext db, ICurrentTenantService tenant)
    {
        _db = db;
        _tenant = tenant;
    }

    public async Task<IActionResult> Index()
    {
        ViewBag.Items = await _db.Items.Where(i => i.IsActive).OrderBy(i => i.Name).ToListAsync();
        var prices = await _db.ItemPrices.Include(p => p.Item)
            .OrderByDescending(p => p.CreatedAt).ToListAsync();
        return View(prices);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(int itemId, decimal originalPrice, decimal sellingPrice,
        decimal? discountPrice, DateTime? offerStartDate, DateTime? offerEndDate)
    {
        var item = await _db.Items.FindAsync(itemId);
        if (item == null) return RedirectToAction(nameof(Index));

        _db.ItemPrices.Add(new ItemPrice
        {
            StoreId = _tenant.StoreId ?? 0,
            ItemId = itemId,
            OriginalPrice = originalPrice,
            SellingPrice = sellingPrice,
            DiscountPrice = discountPrice,
            OfferStartDate = offerStartDate,
            OfferEndDate = offerEndDate
        });

        // Keep the item's standing selling price in sync with the latest price entry.
        item.SellingPrice = discountPrice ?? sellingPrice;

        await _db.SaveChangesAsync();
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(int id)
    {
        var price = await _db.ItemPrices.FindAsync(id);
        if (price != null) { price.IsDeleted = true; await _db.SaveChangesAsync(); }
        return RedirectToAction(nameof(Index));
    }
}
