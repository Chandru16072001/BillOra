using BillOra.Domain.Enums;

namespace BillOra.Application.Common.Interfaces;

// Mini Accounts Module - the single entry point every other module calls
// to post a financial event (a sale, a purchase, a return, a manual entry...).
// Centralizing this means the Balance Sheet and ledger reports never miss
// a transaction just because a new module forgot to record one.
public interface IAccountingService
{
    Task PostAsync(int storeId, string transactionName, decimal amount, TransactionDirection type,
        string category, string? reason = null, string? sourceModule = null, int? sourceId = null,
        string? referenceNumber = null, string? paymentMethod = null);

    // Reverses (deletes) any auto-posted rows tied to a given source module + id,
    // used when a Sale is edited/returned so the ledger doesn't double-count.
    Task ReverseAsync(string sourceModule, int sourceId);
}
