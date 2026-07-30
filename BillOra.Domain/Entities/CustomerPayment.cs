using BillOra.Domain.Common;

namespace BillOra.Domain.Entities;

// Payment history for Customer Outstanding Collection - kept as its own
// table (rather than only relying on the generic AccountTransaction
// ledger) so "this customer's payment history" is a simple, fast query
// instead of filtering the whole ledger by SourceModule string matching.
// Every collection here also posts a corresponding AccountTransaction
// (via IAccountingService) so it shows up in Accounts/Reports too.
public class CustomerPayment : TenantEntity
{
    public int CustomerId { get; set; }
    public Customer? Customer { get; set; }

    public decimal Amount { get; set; }
    public DateTime PaymentDate { get; set; } = DateTime.UtcNow;
    public int? PaymentModeId { get; set; }
    public PaymentMode? PaymentMode { get; set; }
    public string? Notes { get; set; }
    public string? ReceivedByUserId { get; set; }
}
