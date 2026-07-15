using BillOra.Application.Common.Interfaces;
using BillOra.Domain.Entities;
using BillOra.Domain.Enums;
using BillOra.Persistence;
using BillOra.Shared.Constants;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace BillOra.Web.Controllers;

// Sales Invoice Configuration - controls exactly what appears on every
// printed invoice (A4 and Thermal), and which fields/sections are shown.
[Authorize(Roles = Roles.StoreAdmin)]
public class InvoiceConfigController : Controller
{
    private readonly BillOraDbContext _db;
    private readonly ICurrentTenantService _tenant;

    public InvoiceConfigController(BillOraDbContext db, ICurrentTenantService tenant)
    {
        _db = db;
        _tenant = tenant;
    }

    public async Task<IActionResult> Index()
    {
        var storeId = _tenant.StoreId ?? 0;
        var settings = await _db.InvoiceSettingsEntries.FirstOrDefaultAsync(s => s.StoreId == storeId);
        if (settings == null)
        {
            settings = new InvoiceSettings { StoreId = storeId };
            _db.InvoiceSettingsEntries.Add(settings);
            await _db.SaveChangesAsync();
        }
        return View(settings);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Save(
        string invoiceHeading,
        bool showStoreName, bool showStoreAddress, bool showStorePhone, bool showStoreEmail, bool showStoreGst, bool showStoreLogo,
        bool showCustomerName, bool showCustomerPhone, bool showCustomerAddress, bool showCustomerGst,
        bool showGstDetails, bool showQrCode, bool showTermsAndConditions, string? termsAndConditionsText,
        string? footerMessage, PrinterType defaultPrinterType, int copies, string? paperSize)
    {
        var storeId = _tenant.StoreId ?? 0;
        var settings = await _db.InvoiceSettingsEntries.FirstOrDefaultAsync(s => s.StoreId == storeId);
        if (settings == null)
        {
            settings = new InvoiceSettings { StoreId = storeId };
            _db.InvoiceSettingsEntries.Add(settings);
        }

        settings.InvoiceHeading = string.IsNullOrWhiteSpace(invoiceHeading) ? "Tax Invoice" : invoiceHeading;
        settings.ShowStoreName = showStoreName;
        settings.ShowStoreAddress = showStoreAddress;
        settings.ShowStorePhone = showStorePhone;
        settings.ShowStoreEmail = showStoreEmail;
        settings.ShowStoreGst = showStoreGst;
        settings.ShowStoreLogo = showStoreLogo;
        settings.ShowCustomerName = showCustomerName;
        settings.ShowCustomerPhone = showCustomerPhone;
        settings.ShowCustomerAddress = showCustomerAddress;
        settings.ShowCustomerGst = showCustomerGst;
        settings.ShowGstDetails = showGstDetails;
        settings.ShowQrCode = showQrCode;
        settings.ShowTermsAndConditions = showTermsAndConditions;
        settings.TermsAndConditionsText = termsAndConditionsText;
        settings.FooterMessage = footerMessage;
        settings.DefaultPrinterType = defaultPrinterType;
        settings.Copies = copies <= 0 ? 1 : copies;
        settings.PaperSize = paperSize;

        await _db.SaveChangesAsync();
        TempData["Success"] = "Sales Invoice Configuration saved.";
        return RedirectToAction(nameof(Index));
    }
}
