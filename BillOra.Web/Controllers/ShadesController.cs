using BillOra.Application.Common.Interfaces;
using BillOra.Domain.Entities;
using BillOra.Persistence;
using BillOra.Shared.Constants;
using BillOra.Web.Authorization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace BillOra.Web.Controllers;

// Shade/Color Master: every tinted variant of a paint product (Item), with
// its mixing formula for the tint machine. A "Custom Shade" is a one-off
// mix (e.g. matched from a customer's sample) rather than a catalog shade;
// ReplacesShadeId tracks shade replacement when a code is discontinued.
[Authorize(Roles = Roles.StoreAdmin + "," + Roles.Manager)]
[RequireModule(ModuleKeys.Shades)]
[RequirePaintingShop]
public class ShadesController : Controller
{
    private readonly BillOraDbContext _db;
    private readonly ICurrentTenantService _tenant;

    public ShadesController(BillOraDbContext db, ICurrentTenantService tenant)
    {
        _db = db;
        _tenant = tenant;
    }

    public async Task<IActionResult> Index(int? itemId, string? search)
    {
        var query = _db.ShadeColors.Include(s => s.Item).AsQueryable();
        if (itemId.HasValue) query = query.Where(s => s.ItemId == itemId.Value);
        if (!string.IsNullOrWhiteSpace(search))
            query = query.Where(s => s.ShadeName.Contains(search) || s.ShadeCode.Contains(search));

        ViewBag.Items = await _db.Items.Where(i => i.IsActive).OrderBy(i => i.Name).ToListAsync();
        ViewBag.SelectedItemId = itemId;
        ViewBag.Search = search;

        return View(await query.OrderBy(s => s.ShadeCode).ToListAsync());
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(int itemId, string shadeCode, string shadeName, string? baseType,
        string? colorFormula, string? hexColor, bool isCustomShade, int? replacesShadeId)
    {
        if (string.IsNullOrWhiteSpace(shadeCode) || string.IsNullOrWhiteSpace(shadeName))
        {
            TempData["Error"] = "Shade code and shade name are required.";
            return RedirectToAction(nameof(Index), new { itemId });
        }

        var duplicate = await _db.ShadeColors.AnyAsync(s => s.ItemId == itemId && s.ShadeCode == shadeCode);
        if (duplicate)
        {
            TempData["Error"] = $"Shade code '{shadeCode}' already exists for this product.";
            return RedirectToAction(nameof(Index), new { itemId });
        }

        _db.ShadeColors.Add(new ShadeColor
        {
            StoreId = _tenant.StoreId ?? 0,
            ItemId = itemId,
            ShadeCode = shadeCode.Trim(),
            ShadeName = shadeName.Trim(),
            BaseType = baseType,
            ColorFormula = colorFormula,
            HexColor = string.IsNullOrWhiteSpace(hexColor) ? null : hexColor,
            IsCustomShade = isCustomShade,
            ReplacesShadeId = replacesShadeId
        });

        // A replacement shade retires the old one rather than deleting it,
        // so historical quotations/sales still show what was actually sold.
        if (replacesShadeId.HasValue)
        {
            var old = await _db.ShadeColors.FindAsync(replacesShadeId.Value);
            if (old != null) old.IsActive = false;
        }

        await _db.SaveChangesAsync();
        TempData["Success"] = $"Added shade '{shadeName}' ({shadeCode}).";
        return RedirectToAction(nameof(Index), new { itemId });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ToggleActive(int id, int? itemId)
    {
        var shade = await _db.ShadeColors.FindAsync(id);
        if (shade != null) { shade.IsActive = !shade.IsActive; await _db.SaveChangesAsync(); }
        return RedirectToAction(nameof(Index), new { itemId });
    }

    // AJAX: shades available for a given paint product, used by the
    // Quotation builder's shade picker.
    [HttpGet]
    public async Task<IActionResult> ForItem(int itemId)
    {
        var shades = await _db.ShadeColors
            .Where(s => s.ItemId == itemId && s.IsActive)
            .OrderBy(s => s.ShadeName)
            .Select(s => new { s.Id, s.ShadeCode, s.ShadeName, s.HexColor, s.BaseType })
            .ToListAsync();
        return Json(shades);
    }
}
