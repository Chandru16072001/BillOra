using BillOra.Application.Common.Interfaces;
using BillOra.Domain.Entities;
using BillOra.Persistence;
using BillOra.Shared.Constants;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

using BillOra.Web.Authorization;

namespace BillOra.Web.Controllers;

[Authorize(Roles = Roles.StoreAdmin + "," + Roles.Manager)]
[RequireModule(ModuleKeys.Items)]
public class ItemsController : Controller
{
    private readonly BillOraDbContext _db;
    private readonly ICurrentTenantService _tenant;
    private readonly IWebHostEnvironment _env;

    public ItemsController(BillOraDbContext db, ICurrentTenantService tenant, IWebHostEnvironment env)
    {
        _db = db;
        _tenant = tenant;
        _env = env;
    }

    public async Task<IActionResult> Index(string? search)
    {
        var query = _db.Items.Include(i => i.Category).Include(i => i.Unit).AsQueryable();
        if (!string.IsNullOrWhiteSpace(search))
            query = query.Where(i => i.Name.Contains(search) || (i.ItemCode ?? "").Contains(search));

        ViewBag.Search = search;
        return View(await query.OrderBy(i => i.Name).ToListAsync());
    }

    public async Task<IActionResult> Create()
    {
        await PopulateDropdownsAsync();
        return View(new Item());
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(Item item, IFormFile? imageFile)
    {
        if (!ModelState.IsValid)
        {
            await PopulateDropdownsAsync();
            return View(item);
        }

        item.StoreId = _tenant.StoreId ?? 0;
        item.CurrentStock = item.OpeningStock;

        if (imageFile != null && imageFile.Length > 0)
            item.ImagePath = await SaveItemImageAsync(imageFile);

        _db.Items.Add(item);
        await _db.SaveChangesAsync();

        if (item.OpeningStock > 0)
        {
            _db.InventoryTransactions.Add(new InventoryTransaction
            {
                StoreId = item.StoreId,
                ItemId = item.Id,
                TransactionType = Domain.Enums.InventoryTransactionType.OpeningStock,
                Quantity = item.OpeningStock,
                BalanceAfter = item.OpeningStock,
                Notes = "Opening stock on item creation"
            });
            await _db.SaveChangesAsync();
        }

        return RedirectToAction(nameof(Index));
    }

    public async Task<IActionResult> Edit(int id)
    {
        var item = await _db.Items.FindAsync(id);
        if (item == null) return NotFound();
        await PopulateDropdownsAsync();
        return View(item);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(int id, Item item, IFormFile? imageFile)
    {
        if (id != item.Id) return NotFound();
        if (!ModelState.IsValid)
        {
            await PopulateDropdownsAsync();
            return View(item);
        }

        var existing = await _db.Items.FindAsync(id);
        if (existing == null) return NotFound();

        existing.Name = item.Name;
        existing.ItemCode = item.ItemCode;
        existing.Barcode = item.Barcode;
        existing.CategoryId = item.CategoryId;
        existing.UnitId = item.UnitId;
        existing.HsnCode = item.HsnCode;
        existing.GstPercent = item.GstPercent;
        existing.PurchasePrice = item.PurchasePrice;
        existing.SellingPrice = item.SellingPrice;
        existing.MinSellingPrice = item.MinSellingPrice;
        existing.ReorderLevel = item.ReorderLevel;
        existing.IsActive = item.IsActive;
        existing.UpdatedAt = DateTime.UtcNow;

        if (imageFile != null && imageFile.Length > 0)
            existing.ImagePath = await SaveItemImageAsync(imageFile);

        await _db.SaveChangesAsync();
        return RedirectToAction(nameof(Index));
    }

    // Saves under wwwroot/uploads/items so it's servable as a static file at
    // /uploads/items/{filename} — used by the Item Master preview and the
    // POS search dropdown/grid thumbnails.
    private async Task<string> SaveItemImageAsync(IFormFile file)
    {
        var allowedExtensions = new[] { ".jpg", ".jpeg", ".png", ".webp" };
        var ext = Path.GetExtension(file.FileName).ToLowerInvariant();
        if (!allowedExtensions.Contains(ext)) ext = ".jpg";

        var fileName = $"{Guid.NewGuid():N}{ext}";
        var uploadsDir = Path.Combine(_env.WebRootPath, "uploads", "items");
        Directory.CreateDirectory(uploadsDir);

        var filePath = Path.Combine(uploadsDir, fileName);
        await using (var stream = new FileStream(filePath, FileMode.Create))
        {
            await file.CopyToAsync(stream);
        }

        return $"/uploads/items/{fileName}";
    }

    private async Task PopulateDropdownsAsync()
    {
        ViewBag.Categories = await _db.Categories.OrderBy(c => c.Name).ToListAsync();
        ViewBag.Units = await _db.Units.OrderBy(u => u.Name).ToListAsync();
    }
}
