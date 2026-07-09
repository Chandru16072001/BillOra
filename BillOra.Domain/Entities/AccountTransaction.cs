using BillOra.Domain.Common;
using BillOra.Domain.Enums;

namespace BillOra.Domain.Entities;

// Mini Accounts Module - single ledger row. Rows are created two ways:
// (1) automatically by other modules (Sale, Purchase, SalesReturn, etc. -
//     see IAccountingService) so every financial event in the app shows up
//     here without the user doing anything, and
// (2) manually via the Accounts screen for things like expenses/income that
//     don't have their own module yet.
public class AccountTransaction : TenantEntity
{
    public DateTime TransactionDate { get; set; } = DateTime.UtcNow;
    public string TransactionName { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public TransactionDirection Type { get; set; }
    public string? Reason { get; set; }
    public string? Category { get; set; } // e.g. Sales, Purchase, Expense, Income, Refund, Opening Balance
    public string? PaymentMethod { get; set; } // Cash, UPI, Card, Bank Transfer, etc.
    public string? ReferenceNumber { get; set; }
    public string? Notes { get; set; }
    public string? CreatedByUserId { get; set; }
    public string? CreatedByName { get; set; }
    public string? AttachmentPath { get; set; }

    // Set when this row was posted automatically by another module, so it
    // can be found/reversed later (e.g. when a sale is edited or returned).
    public string? SourceModule { get; set; } // "Sale", "Purchase", "SalesReturn", "Manual", ...
    public int? SourceId { get; set; }
}
