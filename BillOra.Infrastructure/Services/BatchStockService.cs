using System.Text;
using BillOra.Application.Common.Interfaces;
using BillOra.Persistence;
using Microsoft.EntityFrameworkCore;

namespace BillOra.Infrastructure.Services;

public class BatchStockService : IBatchStockService
{
    private readonly BillOraDbContext _db;

    public BatchStockService(BillOraDbContext db)
    {
        _db = db;
    }

    public async Task<BatchAllocationResult> AllocateForSaleAsync(int storeId, int itemId, decimal quantity, int? saleItemId = null)
    {
        // Nearest expiry first; batches with no expiry (null) sort last and are
        // drawn oldest-received-first among themselves.
        var batches = await _db.StockBatches
            .Where(b => b.StoreId == storeId && b.ItemId == itemId && b.RemainingQuantity > 0)
            .OrderBy(b => b.ExpiryDate.HasValue ? 0 : 1)
            .ThenBy(b => b.ExpiryDate)
            .ThenBy(b => b.CreatedAt)
            .ToListAsync();

        var remaining = quantity;
        var summary = new StringBuilder();

        foreach (var batch in batches)
        {
            if (remaining <= 0) break;
            var draw = Math.Min(batch.RemainingQuantity, remaining);
            batch.RemainingQuantity -= draw;
            remaining -= draw;

            if (summary.Length > 0) summary.Append(", ");
            summary.Append($"{batch.BatchNumber} x{draw}");
        }

        var allocated = quantity - remaining;
        return new BatchAllocationResult(summary.Length > 0 ? summary.ToString() : null, allocated);
    }
}
