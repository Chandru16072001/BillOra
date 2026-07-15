namespace BillOra.Application.Common.Interfaces;

public record BatchAllocationResult(string? BatchInfo, decimal AllocatedQuantity);

// Only meaningful when Store.BatchTrackingEnabled is true. Sales draw stock
// nearest-expiry-first (batches with no expiry date are treated as "expire
// last" and drawn via FIFO by received date instead).
public interface IBatchStockService
{
    Task<BatchAllocationResult> AllocateForSaleAsync(int storeId, int itemId, decimal quantity, int? saleItemId = null);
}
