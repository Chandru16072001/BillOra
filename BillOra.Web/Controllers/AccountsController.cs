using BillOra.Application.Common.Interfaces;
using BillOra.Domain.Entities;
using BillOra.Domain.Enums;
using BillOra.Persistence;
using BillOra.Shared.Constants;
using BillOra.Web.Authorization;
using BillOra.Web.Utils;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace BillOra.Web.Controllers;

// Mini Accounts Module. Most rows are posted automatically by other modules
// (Sale -> Credit, Purchase/SalesReturn -> Debit, see IAccountingService);
// this screen is where manual entries (expenses, income, opening balance)
// get added, and where everything - automatic or manual - can be reviewed.
[Authorize(Roles = Roles.StoreAdmin + "," + Roles.Manager)]
[RequireModule(ModuleKeys.Accounts)]
public class AccountsController : Controller
{
    private readonly BillOraDbContext _db;
    private readonly ICurrentTenantService _tenant;
    private readonly IWebHostEnvironment _env;

    public AccountsController(BillOraDbContext db, ICurrentTenantService tenant, IWebHostEnvironment env)
    {
        _db = db;
        _tenant = tenant;
        _env = env;
    }

    public async Task<IActionResult> Index()
    {
        ViewBag.RecentTransactions = await _db.AccountTransactions
            .OrderByDescending(t => t.TransactionDate).Take(15).ToListAsync();
        return View();
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(DateTime transactionDate, string transactionName, decimal amount,
        TransactionDirection type, string? reason, string? category, string? paymentMethod,
        string? referenceNumber, string? notes, IFormFile? attachment)
    {
        if (string.IsNullOrWhiteSpace(transactionName) || amount <= 0)
        {
            TempData["Error"] = "Transaction name and a positive amount are required.";
            return RedirectToAction(nameof(Index));
        }

        string? attachmentPath = null;
        if (attachment != null && attachment.Length > 0)
        {
            var uploadsDir = Path.Combine(_env.WebRootPath, "uploads", "accounts");
            Directory.CreateDirectory(uploadsDir);
            var fileName = $"{Guid.NewGuid():N}{Path.GetExtension(attachment.FileName)}";
            await using var stream = new FileStream(Path.Combine(uploadsDir, fileName), FileMode.Create);
            await attachment.CopyToAsync(stream);
            attachmentPath = $"/uploads/accounts/{fileName}";
        }

        _db.AccountTransactions.Add(new AccountTransaction
        {
            StoreId = _tenant.StoreId ?? 0,
            TransactionDate = transactionDate == default ? DateTime.UtcNow : transactionDate,
            TransactionName = transactionName,
            Amount = amount,
            Type = type,
            Reason = reason,
            Category = category,
            PaymentMethod = paymentMethod,
            ReferenceNumber = referenceNumber,
            Notes = notes,
            AttachmentPath = attachmentPath,
            CreatedByUserId = _tenant.UserId,
            SourceModule = "Manual"
        });

        await _db.SaveChangesAsync();
        TempData["Success"] = $"Recorded {type} of ₹{amount:N2} for '{transactionName}'.";
        return RedirectToAction(nameof(Index));
    }

    public async Task<IActionResult> History(DateTime? from, DateTime? to, TransactionDirection? type,
        string? category, string? paymentMethod)
    {
        var query = _db.AccountTransactions.AsQueryable();
        if (from.HasValue) query = query.Where(t => t.TransactionDate >= from.Value);
        if (to.HasValue) query = query.Where(t => t.TransactionDate <= to.Value.AddDays(1).AddTicks(-1));
        if (type.HasValue) query = query.Where(t => t.Type == type.Value);
        if (!string.IsNullOrWhiteSpace(category)) query = query.Where(t => t.Category == category);
        if (!string.IsNullOrWhiteSpace(paymentMethod)) query = query.Where(t => t.PaymentMethod == paymentMethod);

        ViewBag.From = from; ViewBag.To = to; ViewBag.Type = type; ViewBag.Category = category; ViewBag.PaymentMethod = paymentMethod;

        var results = await query.OrderByDescending(t => t.TransactionDate).ToListAsync();
        return View(results);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> EditTransaction(int id, string transactionName, decimal amount, string? reason,
        string? category, string? notes)
    {
        var tx = await _db.AccountTransactions.FindAsync(id);
        if (tx == null) return NotFound();

        tx.TransactionName = transactionName;
        tx.Amount = amount;
        tx.Reason = reason;
        tx.Category = category;
        tx.Notes = notes;
        tx.UpdatedAt = DateTime.UtcNow;

        await _db.SaveChangesAsync();
        TempData["Success"] = "Transaction updated.";
        return RedirectToAction(nameof(History));
    }

    public async Task<IActionResult> BalanceSheet(DateTime? from, DateTime? to)
    {
        var query = _db.AccountTransactions.AsQueryable();
        if (from.HasValue) query = query.Where(t => t.TransactionDate >= from.Value);
        if (to.HasValue) query = query.Where(t => t.TransactionDate <= to.Value.AddDays(1).AddTicks(-1));

        var all = await query.ToListAsync();
        var totalCredits = all.Where(t => t.Type == TransactionDirection.Credit).Sum(t => t.Amount);
        var totalDebits = all.Where(t => t.Type == TransactionDirection.Debit).Sum(t => t.Amount);

        ViewBag.From = from; ViewBag.To = to;
        ViewBag.TotalCredits = totalCredits;
        ViewBag.TotalDebits = totalDebits;
        ViewBag.CurrentBalance = totalCredits - totalDebits;
        ViewBag.CreditsByCategory = all.Where(t => t.Type == TransactionDirection.Credit)
            .GroupBy(t => t.Category ?? "Uncategorized").Select(g => new { Category = g.Key, Total = g.Sum(x => x.Amount) })
            .OrderByDescending(x => x.Total).ToList();
        ViewBag.DebitsByCategory = all.Where(t => t.Type == TransactionDirection.Debit)
            .GroupBy(t => t.Category ?? "Uncategorized").Select(g => new { Category = g.Key, Total = g.Sum(x => x.Amount) })
            .OrderByDescending(x => x.Total).ToList();

        return View();
    }

    [HttpGet]
    public async Task<IActionResult> ExportHistory(DateTime? from, DateTime? to, TransactionDirection? type, string? category)
    {
        var query = _db.AccountTransactions.AsQueryable();
        if (from.HasValue) query = query.Where(t => t.TransactionDate >= from.Value);
        if (to.HasValue) query = query.Where(t => t.TransactionDate <= to.Value.AddDays(1).AddTicks(-1));
        if (type.HasValue) query = query.Where(t => t.Type == type.Value);
        if (!string.IsNullOrWhiteSpace(category)) query = query.Where(t => t.Category == category);

        var results = await query.OrderByDescending(t => t.TransactionDate).ToListAsync();

        var headers = new[] { "Date", "Name", "Type", "Category", "Amount", "Payment Method", "Reference", "Reason", "Source" };
        var rows = results.Select(t => new[]
        {
            t.TransactionDate.ToString("yyyy-MM-dd HH:mm"), t.TransactionName, t.Type.ToString(), t.Category ?? "",
            t.Amount.ToString("N2"), t.PaymentMethod ?? "", t.ReferenceNumber ?? "", t.Reason ?? "", t.SourceModule ?? ""
        });

        var csv = CsvExportHelper.ToCsv(headers, rows);
        return File(csv, "text/csv", $"AccountHistory_{DateTime.UtcNow:yyyyMMdd}.csv");
    }
}
