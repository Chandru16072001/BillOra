using BillOra.Application.Common.Interfaces;
using BillOra.Domain.Entities;
using BillOra.Persistence;
using BillOra.Shared.Constants;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace BillOra.Web.Controllers;

// Application-wide branding configuration - Developer only. This is a
// single global row shared by every store/client, unlike almost
// everything else in the app which is store-scoped.
[Authorize(Roles = Roles.Developer)]
public class BrandingController : Controller
{
    private readonly BillOraDbContext _db;
    private readonly IBrandingService _brandingService;
    private readonly IWebHostEnvironment _env;

    public BrandingController(BillOraDbContext db, IBrandingService brandingService, IWebHostEnvironment env)
    {
        _db = db;
        _brandingService = brandingService;
        _env = env;
    }

    public async Task<IActionResult> Index()
    {
        var branding = await _db.AppBrandings.OrderBy(b => b.Id).FirstOrDefaultAsync() ?? new AppBranding();
        return View(branding);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Save(string softwareName, string? tagline, IFormFile? logoFile, IFormFile? faviconFile)
    {
        var branding = await _db.AppBrandings.OrderBy(b => b.Id).FirstOrDefaultAsync();
        if (branding == null)
        {
            branding = new AppBranding();
            _db.AppBrandings.Add(branding);
        }

        branding.SoftwareName = string.IsNullOrWhiteSpace(softwareName) ? "BillOra" : softwareName.Trim();
        branding.Tagline = tagline;
        branding.UpdatedAt = DateTime.UtcNow;

        if (logoFile != null && logoFile.Length > 0)
            branding.LogoPath = await SaveBrandingFileAsync(logoFile, "logo");

        if (faviconFile != null && faviconFile.Length > 0)
            branding.FaviconPath = await SaveBrandingFileAsync(faviconFile, "favicon");

        await _db.SaveChangesAsync();
        _brandingService.InvalidateCache();

        TempData["Success"] = "Branding updated - changes are live across the application immediately.";
        return RedirectToAction(nameof(Index));
    }

    private async Task<string> SaveBrandingFileAsync(IFormFile file, string prefix)
    {
        var uploadsDir = Path.Combine(_env.WebRootPath, "uploads", "branding");
        Directory.CreateDirectory(uploadsDir);

        var ext = Path.GetExtension(file.FileName);
        var fileName = $"{prefix}{ext}"; // fixed name so old references keep working / old file gets overwritten
        var filePath = Path.Combine(uploadsDir, fileName);

        await using (var stream = new FileStream(filePath, FileMode.Create))
        {
            await file.CopyToAsync(stream);
        }

        // Cache-bust with a query string so browsers pick up a replaced
        // logo/favicon immediately instead of serving a stale cached image.
        return $"/uploads/branding/{fileName}?v={DateTime.UtcNow.Ticks}";
    }
}
