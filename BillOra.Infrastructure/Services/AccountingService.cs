using BillOra.Application.Common.Interfaces;
using BillOra.Domain.Entities;
using BillOra.Domain.Enums;
using BillOra.Persistence;
using Microsoft.EntityFrameworkCore;

namespace BillOra.Infrastructure.Services;

public class AccountingService : IAccountingService
{
    private readonly BillOraDbContext _db;
    private readonly ICurrentTenantService _tenant;

    public AccountingService(BillOraDbContext db, ICurrentTenantService tenant)
    {
        _db = db;
        _tenant = tenant;
    }

    public async Task PostAsync(int storeId, string transactionName, decimal amount, TransactionDirection type,
        string category, string? reason = null, string? sourceModule = null, int? sourceId = null,
        string? referenceNumber = null, string? paymentMethod = null)
    {
        if (amount == 0) return;

        _db.AccountTransactions.Add(new AccountTransaction
        {
            StoreId = storeId,
            TransactionDate = DateTime.UtcNow,
            TransactionName = transactionName,
            Amount = Math.Abs(amount),
            Type = type,
            Category = category,
            Reason = reason,
            SourceModule = sourceModule,
            SourceId = sourceId,
            ReferenceNumber = referenceNumber,
            PaymentMethod = paymentMethod,
            CreatedByUserId = _tenant.UserId
        });

        await _db.SaveChangesAsync();
    }

    public async Task ReverseAsync(string sourceModule, int sourceId)
    {
        var rows = await _db.AccountTransactions
            .Where(t => t.SourceModule == sourceModule && t.SourceId == sourceId)
            .ToListAsync();

        foreach (var row in rows) row.IsDeleted = true;
        await _db.SaveChangesAsync();
    }
}
