using BillOra.Application.Common.Interfaces;
using BillOra.Domain.Entities;
using BillOra.Persistence;
using BillOra.Shared.Constants;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace BillOra.Web.Controllers;

// WhatsApp Business Platform configuration - one row per store. The access
// token is encrypted at rest and never echoed back to the browser; leave
// the token field blank when saving to keep the previously-saved one.
[Authorize(Roles = Roles.StoreAdmin)]
public class WhatsAppSettingsController : Controller
{
    private readonly BillOraDbContext _db;
    private readonly ICurrentTenantService _tenant;
    private readonly IDataProtector _protector;

    public WhatsAppSettingsController(BillOraDbContext db, ICurrentTenantService tenant, IDataProtectionProvider dataProtectionProvider)
    {
        _db = db;
        _tenant = tenant;
        _protector = dataProtectionProvider.CreateProtector("BillOra.WhatsAppSettings.AccessToken");
    }

    public async Task<IActionResult> Index()
    {
        var storeId = _tenant.StoreId ?? 0;
        var settings = await _db.WhatsAppSettingsEntries.FirstOrDefaultAsync(w => w.StoreId == storeId)
            ?? new WhatsAppSettings { StoreId = storeId };

        ViewBag.HasToken = !string.IsNullOrEmpty(settings.AccessTokenEncrypted);
        ViewBag.RecentLogs = await _db.WhatsAppMessageLogs.Include(l => l.Sale)
            .OrderByDescending(l => l.SentAt).Take(15).ToListAsync();

        return View(settings);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Save(string? phoneNumberId, string? whatsAppBusinessAccountId,
        string? accessToken, string templateName, string templateLanguageCode, string defaultCountryCode, bool isEnabled)
    {
        var storeId = _tenant.StoreId ?? 0;
        var settings = await _db.WhatsAppSettingsEntries.FirstOrDefaultAsync(w => w.StoreId == storeId);
        if (settings == null)
        {
            settings = new WhatsAppSettings { StoreId = storeId };
            _db.WhatsAppSettingsEntries.Add(settings);
        }

        settings.PhoneNumberId = phoneNumberId;
        settings.WhatsAppBusinessAccountId = whatsAppBusinessAccountId;
        settings.TemplateName = string.IsNullOrWhiteSpace(templateName) ? "invoice_notification" : templateName;
        settings.TemplateLanguageCode = string.IsNullOrWhiteSpace(templateLanguageCode) ? "en_US" : templateLanguageCode;
        settings.DefaultCountryCode = string.IsNullOrWhiteSpace(defaultCountryCode) ? "91" : defaultCountryCode;
        settings.IsEnabled = isEnabled;

        if (!string.IsNullOrWhiteSpace(accessToken))
            settings.AccessTokenEncrypted = _protector.Protect(accessToken);

        await _db.SaveChangesAsync();
        TempData["Success"] = "WhatsApp settings saved.";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SendTest(string testPhone, [FromServices] IWhatsAppService whatsAppService)
    {
        // Sends against the most recent real sale so you're testing the
        // exact code path used in production, not a synthetic message.
        var storeId = _tenant.StoreId ?? 0;
        var recentSale = await _db.Sales.Where(s => !s.IsHeld).OrderByDescending(s => s.SaleDate).FirstOrDefaultAsync();
        if (recentSale == null)
        {
            TempData["Error"] = "No sales exist yet to send a test invoice for.";
            return RedirectToAction(nameof(Index));
        }

        var (success, error) = await whatsAppService.SendInvoiceAsync(storeId, recentSale.Id);
        TempData[success ? "Success" : "Error"] = success
            ? $"Test invoice for {recentSale.InvoiceNumber} sent."
            : $"Test send failed: {error}";
        return RedirectToAction(nameof(Index));
    }
}
