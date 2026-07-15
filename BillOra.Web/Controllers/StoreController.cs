using BillOra.Application.Common.Interfaces;
using BillOra.Persistence;
using BillOra.Shared.Constants;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BillOra.Web.Controllers;

// Store Information screen - full business profile for the current store.
[Authorize(Roles = Roles.StoreAdmin)]
public class StoreController : Controller
{
    private readonly BillOraDbContext _db;
    private readonly ICurrentTenantService _tenant;
    private readonly IWebHostEnvironment _env;

    public StoreController(BillOraDbContext db, ICurrentTenantService tenant, IWebHostEnvironment env)
    {
        _db = db;
        _tenant = tenant;
        _env = env;
    }

    public async Task<IActionResult> Index()
    {
        var store = await _db.Stores.FindAsync(_tenant.StoreId ?? 0);
        if (store == null) return NotFound();
        return View(store);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(
        string name, string? ownerName, string? gstNumber, string? panNumber, string? businessType, string? fssaiNumber,
        string? phone, string? alternatePhone, string? email,
        string? address, string? district, string? taluk, string? state, string? country, string? pinCode,
        IFormFile? logoFile)
    {
        var store = await _db.Stores.FindAsync(_tenant.StoreId ?? 0);
        if (store == null) return NotFound();

        store.Name = name;
        store.OwnerName = ownerName;
        store.GstNumber = gstNumber;
        store.PanNumber = panNumber;
        store.BusinessType = businessType;
        store.FssaiNumber = fssaiNumber;
        store.Phone = phone;
        store.AlternatePhone = alternatePhone;
        store.Email = email;
        store.Address = address;
        store.District = district;
        store.Taluk = taluk;
        store.State = state;
        store.Country = country;
        store.PinCode = pinCode;
        store.UpdatedAt = DateTime.UtcNow;

        if (logoFile != null && logoFile.Length > 0)
        {
            var uploadsDir = Path.Combine(_env.WebRootPath, "uploads", "store");
            Directory.CreateDirectory(uploadsDir);
            var ext = Path.GetExtension(logoFile.FileName);
            var fileName = $"logo_{store.Id}{ext}";
            await using var stream = new FileStream(Path.Combine(uploadsDir, fileName), FileMode.Create);
            await logoFile.CopyToAsync(stream);
            store.LogoPath = $"/uploads/store/{fileName}";
        }

        await _db.SaveChangesAsync();
        TempData["Success"] = "Store information updated.";
        return RedirectToAction(nameof(Index));
    }
}
