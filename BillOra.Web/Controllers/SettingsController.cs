using BillOra.Application.Common.Interfaces;
using BillOra.Domain.Entities;
using BillOra.Domain.Enums;
using BillOra.Persistence;
using BillOra.Shared.Constants;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace BillOra.Web.Controllers;

// SRS section 7 - Tax Configuration, Payment Mode Master, Printer Configuration,
// plus Email Configuration for automatic invoice emailing (Store Admin only).
[Authorize(Roles = Roles.StoreAdmin)]
public class SettingsController : Controller
{
    private readonly BillOraDbContext _db;
    private readonly ICurrentTenantService _tenant;
    private readonly IDataProtector _protector;

    public SettingsController(BillOraDbContext db, ICurrentTenantService tenant, IDataProtectionProvider dataProtectionProvider)
    {
        _db = db;
        _tenant = tenant;
        _protector = dataProtectionProvider.CreateProtector("BillOra.EmailSettings.SmtpPassword");
    }

    public async Task<IActionResult> Index()
{
    var storeId = _tenant.StoreId ?? 0;
    var store = await _db.Stores.FindAsync(storeId);

    ViewBag.Store = store;

    ViewBag.Taxes = (await _db.Taxes
        .ToListAsync())
        .OrderBy(t => t.Percentage)
        .ToList();

    ViewBag.PaymentModes = await _db.PaymentModes
        .OrderBy(p => p.Name)
        .ToListAsync();

    ViewBag.PrinterSettings = await _db.PrinterSettings
        .OrderBy(p => p.Type)
        .ToListAsync();

    ViewBag.EmailSettings = await _db.EmailSettingsEntries
        .FirstOrDefaultAsync(e => e.StoreId == storeId);

    return View();
}

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ToggleGst()
    {
        var store = await _db.Stores.FindAsync(_tenant.StoreId ?? 0);
        if (store == null)
        {
            TempData["Error"] = "Could not find your store — please log out and back in.";
            return RedirectToAction(nameof(Index));
        }
        store.GstEnabled = !store.GstEnabled;
        await _db.SaveChangesAsync();
        TempData["Success"] = $"GST is now {(store.GstEnabled ? "enabled" : "disabled")} for this store.";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ToggleStockValidation()
    {
        var store = await _db.Stores.FindAsync(_tenant.StoreId ?? 0);
        if (store == null) return RedirectToAction(nameof(Index));

        store.StockValidationEnabled = !store.StockValidationEnabled;
        await _db.SaveChangesAsync();
        TempData["Success"] = $"Stock Validation is now {(store.StockValidationEnabled ? "enabled" : "disabled")}.";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ToggleBatchTracking()
    {
        var store = await _db.Stores.FindAsync(_tenant.StoreId ?? 0);
        if (store == null) return RedirectToAction(nameof(Index));

        store.BatchTrackingEnabled = !store.BatchTrackingEnabled;
        await _db.SaveChangesAsync();
        TempData["Success"] = $"Batch Tracking is now {(store.BatchTrackingEnabled ? "enabled" : "disabled")}.";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> AddTax(string name, decimal percentage)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            TempData["Error"] = "Tax name is required.";
            return RedirectToAction(nameof(Index));
        }
        _db.Taxes.Add(new Tax { StoreId = _tenant.StoreId ?? 0, Name = name.Trim(), Percentage = percentage });
        await _db.SaveChangesAsync();
        TempData["Success"] = $"Added tax slab '{name}'.";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteTax(int id)
    {
        var tax = await _db.Taxes.FindAsync(id);
        if (tax == null)
        {
            TempData["Error"] = "That tax slab no longer exists.";
            return RedirectToAction(nameof(Index));
        }
        tax.IsDeleted = true;
        await _db.SaveChangesAsync();
        TempData["Success"] = "Tax slab deleted.";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> AddPaymentMode(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            TempData["Error"] = "Payment mode name is required.";
            return RedirectToAction(nameof(Index));
        }
        _db.PaymentModes.Add(new PaymentMode { StoreId = _tenant.StoreId ?? 0, Name = name.Trim(), IsCustom = true });
        await _db.SaveChangesAsync();
        TempData["Success"] = $"Added payment mode '{name}'.";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeletePaymentMode(int id)
    {
        var mode = await _db.PaymentModes.FindAsync(id);
        if (mode == null)
        {
            TempData["Error"] = "That payment mode no longer exists.";
            return RedirectToAction(nameof(Index));
        }
        mode.IsDeleted = true;
        await _db.SaveChangesAsync();
        TempData["Success"] = "Payment mode deleted.";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SavePrinterSetting(PrinterType type, string? paperSize, int copies, string? footerMessage, bool isDefault)
    {
        var storeId = _tenant.StoreId ?? 0;

        if (isDefault)
        {
            var others = await _db.PrinterSettings.Where(p => p.StoreId == storeId).ToListAsync();
            foreach (var o in others) o.IsDefault = false;
        }

        var setting = await _db.PrinterSettings.FirstOrDefaultAsync(p => p.StoreId == storeId && p.Type == type);
        if (setting == null)
        {
            setting = new PrinterSetting { StoreId = storeId, Type = type };
            _db.PrinterSettings.Add(setting);
        }

        setting.PaperSize = paperSize;
        setting.Copies = copies <= 0 ? 1 : copies;
        setting.FooterMessage = footerMessage;
        setting.IsDefault = isDefault;

        await _db.SaveChangesAsync();
        TempData["Success"] = $"Saved {type} printer setting{(isDefault ? " and set it as default" : "")}.";
        return RedirectToAction(nameof(Index));
    }

    // Email Configuration - SMTP details for automatic invoice emailing.
    // The password is never round-tripped back to the browser; leave it
    // blank on the form to keep the previously-saved one.
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SaveEmailSettings(string smtpHost, int smtpPort, string smtpUsername,
        string? smtpPassword, bool useSsl, string? fromEmail, string? fromName, bool autoEmailEnabled)
    {
        var storeId = _tenant.StoreId ?? 0;
        var settings = await _db.EmailSettingsEntries.FirstOrDefaultAsync(e => e.StoreId == storeId);
        if (settings == null)
        {
            settings = new EmailSettings { StoreId = storeId };
            _db.EmailSettingsEntries.Add(settings);
        }

        settings.SmtpHost = smtpHost;
        settings.SmtpPort = smtpPort <= 0 ? 587 : smtpPort;
        settings.SmtpUsername = smtpUsername;
        settings.UseSsl = useSsl;
        settings.FromEmail = string.IsNullOrWhiteSpace(fromEmail) ? smtpUsername : fromEmail;
        settings.FromName = fromName;
        settings.AutoEmailEnabled = autoEmailEnabled;

        if (!string.IsNullOrWhiteSpace(smtpPassword))
            settings.SmtpPasswordEncrypted = _protector.Protect(smtpPassword);

        await _db.SaveChangesAsync();
        TempData["Success"] = "Email configuration saved.";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SendTestEmail(string testRecipient, [FromServices] IEmailSender emailSender)
    {
        if (string.IsNullOrWhiteSpace(testRecipient))
        {
            TempData["Error"] = "Enter an email address to send the test to.";
            return RedirectToAction(nameof(Index));
        }

        var storeId = _tenant.StoreId ?? 0;
        var (success, error) = await emailSender.SendInvoiceEmailAsync(
            storeId,
            testRecipient,
            "BillOra - Test Email",
            "<p>This is a test email from your BillOra store's email configuration. If you received this, automatic invoice emailing is set up correctly.</p>");

        TempData[success ? "Success" : "Error"] = success
            ? $"Test email sent to {testRecipient}."
            : $"Could not send test email: {error}";

        return RedirectToAction(nameof(Index));
    }
}
