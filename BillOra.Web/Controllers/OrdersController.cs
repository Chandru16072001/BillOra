using BillOra.Application.Common.Interfaces;
using BillOra.Application.DTOs;
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

// The core restaurant workflow: start an order (dine-in/takeaway/delivery),
// add items, send rounds to the kitchen (KOT), then generate the final
// bill(s) - which reuses the exact same GST/stock-validation/batch/accounting
// pipeline as POS checkout so restaurant sales show up correctly everywhere
// else in the app (Reports, Accounts, Dashboard).
[Authorize(Roles = Roles.StoreAdmin + "," + Roles.Manager + "," + Roles.Cashier)]
[RequireModule(ModuleKeys.Orders)]
[RequireRestaurant]
public class OrdersController : Controller
{
    private readonly BillOraDbContext _db;
    private readonly ICurrentTenantService _tenant;
    private readonly IActivityLogger _activityLogger;
    private readonly IAccountingService _accounting;
    private readonly IBatchStockService _batchStock;
    private readonly IEmailSender _emailSender;

    public OrdersController(BillOraDbContext db, ICurrentTenantService tenant, IActivityLogger activityLogger,
        IAccountingService accounting, IBatchStockService batchStock, IEmailSender emailSender)
    {
        _db = db;
        _tenant = tenant;
        _activityLogger = activityLogger;
        _accounting = accounting;
        _batchStock = batchStock;
        _emailSender = emailSender;
    }

    public async Task<IActionResult> Index()
    {
        var orders = await _db.RestaurantOrders
            .Include(o => o.Table).Include(o => o.Waiter)
            .Where(o => o.Status != RestaurantOrderStatus.Billed && o.Status != RestaurantOrderStatus.Cancelled)
            .OrderByDescending(o => o.CreatedAt)
            .ToListAsync();
        return View(orders);
    }

    [HttpGet]
    public async Task<IActionResult> Create(int? tableId)
    {
        ViewBag.Items = await _db.Items.Where(i => i.IsActive).OrderBy(i => i.Name).ToListAsync();
        ViewBag.Tables = await _db.DiningTables
            .Where(t => t.Status == TableStatus.Available || t.Status == TableStatus.Reserved)
            .OrderBy(t => t.TableNumber).ToListAsync();
        ViewBag.Waiters = await _db.Waiters.Where(w => w.IsActive).OrderBy(w => w.Name).ToListAsync();
        ViewBag.Customers = await _db.Customers.Where(c => c.IsActive).OrderBy(c => c.Name).ToListAsync();
        ViewBag.PreselectedTableId = tableId;
        return View();
    }

    [HttpGet]
    public async Task<IActionResult> SearchItems(string term)
    {
        term ??= string.Empty;
        var items = await _db.Items
            .Where(i => i.IsActive && (i.Name.Contains(term) || (i.ItemCode ?? "").Contains(term)))
            .Take(20)
            .Select(i => new { i.Id, i.Name, i.SellingPrice, i.GstPercent, i.CurrentStock, i.ImagePath })
            .ToListAsync();
        return Json(items);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create([FromBody] CreateOrderRequest request)
    {
        if (request.Lines.Count == 0) return BadRequest("Add at least one item to the order.");

        var storeId = _tenant.StoreId ?? 0;
        var orderType = Enum.TryParse<RestaurantOrderType>(request.OrderType, out var ot) ? ot : RestaurantOrderType.DineIn;

        DiningTable? table = null;
        if (orderType == RestaurantOrderType.DineIn)
        {
            if (!request.TableId.HasValue) return BadRequest("Select a table for a dine-in order.");
            table = await _db.DiningTables.FindAsync(request.TableId.Value);
            if (table == null) return BadRequest("Table not found.");
        }

        var order = new RestaurantOrder
        {
            StoreId = storeId,
            OrderNumber = await NextOrderNumberAsync(storeId),
            OrderType = orderType,
            TableId = table?.Id,
            WaiterId = request.WaiterId,
            CustomerId = request.CustomerId,
            Notes = request.Notes,
            Status = RestaurantOrderStatus.Open
        };

        foreach (var line in request.Lines)
        {
            var item = await _db.Items.FindAsync(line.ItemId);
            if (item == null) continue;
            order.Items.Add(new RestaurantOrderItem
            {
                ItemId = item.Id,
                Quantity = line.Quantity,
                UnitPrice = item.SellingPrice,
                Notes = line.Notes
            });
        }

        _db.RestaurantOrders.Add(order);

        if (table != null)
        {
            table.Status = TableStatus.Occupied;
            await _db.SaveChangesAsync(); // need order.Id first
            table.CurrentOrderId = order.Id;
        }

        await _db.SaveChangesAsync();
        await _activityLogger.LogAsync("Order created", $"{order.OrderNumber} ({orderType})");

        return Json(new OrderResultDto { OrderId = order.Id, OrderNumber = order.OrderNumber });
    }

    public async Task<IActionResult> Details(int id)
    {
        var order = await _db.RestaurantOrders
            .Include(o => o.Table).Include(o => o.Waiter).Include(o => o.Customer)
            .Include(o => o.Items).ThenInclude(i => i.Item)
            .FirstOrDefaultAsync(o => o.Id == id);
        if (order == null) return NotFound();

        ViewBag.Items = await _db.Items.Where(i => i.IsActive).OrderBy(i => i.Name).ToListAsync();
        ViewBag.PaymentModes = await _db.PaymentModes.Where(p => p.IsActive).ToListAsync();
        return View(order);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> AddItems(int id, [FromBody] List<OrderLineRequest> lines)
    {
        var order = await _db.RestaurantOrders.Include(o => o.Items).FirstOrDefaultAsync(o => o.Id == id);
        if (order == null) return NotFound();
        if (order.Status == RestaurantOrderStatus.Billed || order.Status == RestaurantOrderStatus.Cancelled)
            return BadRequest("This order is already closed.");

        foreach (var line in lines)
        {
            var item = await _db.Items.FindAsync(line.ItemId);
            if (item == null) continue;
            order.Items.Add(new RestaurantOrderItem
            {
                OrderId = order.Id,
                ItemId = item.Id,
                Quantity = line.Quantity,
                UnitPrice = item.SellingPrice,
                Notes = line.Notes
            });
        }

        await _db.SaveChangesAsync();
        return Json(new { success = true });
    }

    // Sends every not-yet-sent item as the next KOT round/batch.
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SendToKitchen(int id)
    {
        var order = await _db.RestaurantOrders.Include(o => o.Items).FirstOrDefaultAsync(o => o.Id == id);
        if (order == null) return NotFound();

        var pending = order.Items.Where(i => i.KotBatch == 0).ToList();
        if (pending.Count == 0)
        {
            TempData["Error"] = "Nothing new to send - all items are already sent to the kitchen.";
            return RedirectToAction(nameof(Details), new { id });
        }

        var batch = order.LastKotBatch + 1;
        foreach (var item in pending)
        {
            item.KotBatch = batch;
            item.KotSentAt = DateTime.UtcNow;
        }
        order.LastKotBatch = batch;
        if (order.Status == RestaurantOrderStatus.Open) order.Status = RestaurantOrderStatus.KotSent;

        await _db.SaveChangesAsync();
        await _activityLogger.LogAsync("KOT sent", $"{order.OrderNumber} batch {batch} ({pending.Count} items)");

        return RedirectToAction(nameof(PrintKot), new { id, batch });
    }

    public async Task<IActionResult> PrintKot(int id, int batch)
    {
        var order = await _db.RestaurantOrders
            .Include(o => o.Table).Include(o => o.Waiter)
            .Include(o => o.Items).ThenInclude(i => i.Item)
            .FirstOrDefaultAsync(o => o.Id == id);
        if (order == null) return NotFound();

        ViewBag.Batch = batch;
        ViewBag.Store = await _db.Stores.FindAsync(order.StoreId);
        return View(order);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> MarkServed(int id)
    {
        var order = await _db.RestaurantOrders.FindAsync(id);
        if (order != null)
        {
            order.Status = RestaurantOrderStatus.Served;
            await _db.SaveChangesAsync();
        }
        return RedirectToAction(nameof(Details), new { id });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Cancel(int id)
    {
        var order = await _db.RestaurantOrders.Include(o => o.Table).FirstOrDefaultAsync(o => o.Id == id);
        if (order == null) return NotFound();

        order.Status = RestaurantOrderStatus.Cancelled;
        if (order.Table != null) { order.Table.Status = TableStatus.Available; order.Table.CurrentOrderId = null; }

        await _db.SaveChangesAsync();
        await _activityLogger.LogAsync("Order cancelled", order.OrderNumber);
        TempData["Success"] = $"Order {order.OrderNumber} cancelled.";
        return RedirectToAction(nameof(Index));
    }

    // Generates one Sale per distinct SplitGroup among the order's items -
    // a single group (the default) means a normal single bill; multiple
    // groups means a split bill, each with its own invoice.
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> GenerateBill(int id, [FromBody] BillOrderRequest request)
    {
        var order = await _db.RestaurantOrders
            .Include(o => o.Table).Include(o => o.Waiter).Include(o => o.Customer)
            .Include(o => o.Items).ThenInclude(i => i.Item)
            .FirstOrDefaultAsync(o => o.Id == id);
        if (order == null) return NotFound();
        if (order.Status == RestaurantOrderStatus.Billed) return BadRequest("This order has already been billed.");
        if (order.Items.Count == 0) return BadRequest("Order has no items.");

        var storeId = order.StoreId;
        var store = await _db.Stores.FindAsync(storeId);
        if (store == null) return BadRequest("Store not found.");

        // Apply the split-group assignments coming from the billing screen.
        foreach (var lineReq in request.Lines)
        {
            var orderItem = order.Items.FirstOrDefault(i => i.Id == lineReq.OrderItemId);
            if (orderItem != null) orderItem.SplitGroup = lineReq.SplitGroup <= 0 ? 1 : lineReq.SplitGroup;
        }

        // Stock validation up front, across the whole order (not per split), so a
        // partial split can't succeed while leaving the rest short on stock.
        if (store.StockValidationEnabled)
        {
            var neededByItem = order.Items.GroupBy(i => i.ItemId).Select(g => new { ItemId = g.Key, Qty = g.Sum(x => x.Quantity) });
            var shortages = new List<string>();
            foreach (var need in neededByItem)
            {
                var item = await _db.Items.FindAsync(need.ItemId);
                if (item != null && item.CurrentStock < need.Qty)
                    shortages.Add($"{item.Name} (available: {item.CurrentStock}, needed: {need.Qty})");
            }
            if (shortages.Count > 0) return BadRequest("Insufficient stock for: " + string.Join("; ", shortages));
        }

        var isInterState = GstCalculator.IsInterState(store.State, order.Customer?.State);
        var splitGroups = order.Items.Select(i => i.SplitGroup).Distinct().OrderBy(g => g).ToList();
        var resultSaleIds = new List<int>();
        var resultInvoiceNumbers = new List<string>();
        decimal combinedGrandTotal = 0;

        // Discount is split proportionally by group total if there's more than one group.
        var overallSubTotal = order.Items.Sum(i => i.UnitPrice * i.Quantity);

        foreach (var group in splitGroups)
        {
            var groupItems = order.Items.Where(i => i.SplitGroup == group).ToList();
            var groupSubTotal = groupItems.Sum(i => i.UnitPrice * i.Quantity);
            var groupDiscount = overallSubTotal > 0 ? request.OverallDiscount * (groupSubTotal / overallSubTotal) : 0;

            var sale = new Sale
            {
                StoreId = storeId,
                CustomerId = order.CustomerId,
                CashierUserId = _tenant.UserId ?? string.Empty,
                PaymentModeId = request.PaymentModeId,
                SaleDate = DateTime.UtcNow,
                IsInterState = isInterState,
                TableNumber = order.Table?.TableNumber,
                WaiterName = order.Waiter?.Name,
                OrderNumber = order.OrderNumber,
                OrderType = order.OrderType switch
                {
                    RestaurantOrderType.DineIn => "Dine-in",
                    RestaurantOrderType.Takeaway => "Takeaway",
                    RestaurantOrderType.Delivery => "Delivery",
                    _ => order.OrderType.ToString()
                }
            };

            decimal subTotal = 0, taxableTotal = 0, taxTotal = 0, cgstTotal = 0, sgstTotal = 0, igstTotal = 0;

            foreach (var oi in groupItems)
            {
                var item = oi.Item ?? await _db.Items.FindAsync(oi.ItemId);
                if (item == null) continue;

                var lineDiscount = groupSubTotal > 0 ? groupDiscount * (oi.UnitPrice * oi.Quantity / groupSubTotal) : 0;
                var gstPercent = store.GstEnabled ? item.GstPercent : 0;
                var gst = GstCalculator.Calculate(oi.UnitPrice, oi.Quantity, lineDiscount, gstPercent, item.PriceType, store.GstEnabled, isInterState);

                subTotal += (oi.UnitPrice * oi.Quantity) - lineDiscount;
                taxableTotal += gst.TaxableValue;
                taxTotal += gst.TaxAmount;
                cgstTotal += gst.CgstAmount;
                sgstTotal += gst.SgstAmount;
                igstTotal += gst.IgstAmount;

                string? batchInfo = null;
                item.CurrentStock -= oi.Quantity;
                if (store.BatchTrackingEnabled)
                {
                    var allocation = await _batchStock.AllocateForSaleAsync(storeId, item.Id, oi.Quantity);
                    batchInfo = allocation.BatchInfo;
                }

                _db.InventoryTransactions.Add(new InventoryTransaction
                {
                    StoreId = storeId,
                    ItemId = item.Id,
                    TransactionType = InventoryTransactionType.Sale,
                    Quantity = -oi.Quantity,
                    BalanceAfter = item.CurrentStock,
                    Notes = $"Order {order.OrderNumber}" + (batchInfo != null ? $" | Batches: {batchInfo}" : "")
                });

                sale.SaleItems.Add(new SaleItem
                {
                    ItemId = item.Id,
                    Quantity = oi.Quantity,
                    UnitPrice = oi.UnitPrice,
                    Discount = lineDiscount,
                    GstPercent = gstPercent,
                    PriceType = item.PriceType,
                    TaxableValue = gst.TaxableValue,
                    TaxAmount = gst.TaxAmount,
                    CgstAmount = gst.CgstAmount,
                    SgstAmount = gst.SgstAmount,
                    IgstAmount = gst.IgstAmount,
                    LineTotal = gst.LineTotal,
                    BatchInfo = batchInfo
                });
            }

            var grandTotalRaw = subTotal + taxTotal;
            var grandTotal = Math.Round(grandTotalRaw, 0, MidpointRounding.AwayFromZero);

            sale.SubTotal = subTotal;
            sale.DiscountAmount = groupDiscount;
            sale.TaxableAmount = taxableTotal;
            sale.TaxAmount = taxTotal;
            sale.CgstAmount = cgstTotal;
            sale.SgstAmount = sgstTotal;
            sale.IgstAmount = igstTotal;
            sale.RoundOff = grandTotal - grandTotalRaw;
            sale.GrandTotal = grandTotal;
            sale.InvoiceNumber = await NextInvoiceNumberAsync(store);

            _db.Sales.Add(sale);
            await _db.SaveChangesAsync();

            await _accounting.PostAsync(storeId, $"Sale {sale.InvoiceNumber} (Order {order.OrderNumber})", sale.GrandTotal,
                TransactionDirection.Credit, "Sales Invoice", sourceModule: "Sale", sourceId: sale.Id,
                referenceNumber: sale.InvoiceNumber, paymentMethod: (await _db.PaymentModes.FindAsync(sale.PaymentModeId))?.Name);

            resultSaleIds.Add(sale.Id);
            resultInvoiceNumbers.Add(sale.InvoiceNumber);
            combinedGrandTotal += sale.GrandTotal;

            if (order.CustomerId.HasValue)
            {
                try
                {
                    var customer = await _db.Customers.FindAsync(order.CustomerId.Value);
                    if (customer != null && !string.IsNullOrWhiteSpace(customer.Email))
                    {
                        var html = InvoiceEmailHtmlBuilder.BuildSaleInvoiceHtml(store, sale, sale.SaleItems);
                        await _emailSender.SendInvoiceEmailAsync(storeId, customer.Email, $"Invoice {sale.InvoiceNumber} from {store.Name}", html);
                    }
                }
                catch { /* best-effort, never fail billing over email */ }
            }
        }

        order.Status = RestaurantOrderStatus.Billed;
        order.SaleId = resultSaleIds.Count == 1 ? resultSaleIds[0] : null;
        if (order.Table != null) { order.Table.Status = TableStatus.Available; order.Table.CurrentOrderId = null; }

        await _db.SaveChangesAsync();
        await _activityLogger.LogAsync("Order billed", $"{order.OrderNumber} -> {string.Join(", ", resultInvoiceNumbers)}");

        return Json(new BillOrderResultDto { SaleIds = resultSaleIds, InvoiceNumbers = resultInvoiceNumbers, GrandTotal = combinedGrandTotal });
    }

    private async Task<string> NextOrderNumberAsync(int storeId)
    {
        var count = await _db.RestaurantOrders.IgnoreQueryFilters().CountAsync(o => o.StoreId == storeId) + 1;
        return $"ORD-{DateTime.UtcNow:yyyyMMdd}-{count:D4}";
    }

    private async Task<string> NextInvoiceNumberAsync(Store store)
    {
        var count = await _db.Sales.IgnoreQueryFilters().CountAsync(s => s.StoreId == store.Id) + 1;
        return $"{store.InvoicePrefix}-{DateTime.UtcNow:yyyyMM}-{count:D4}";
    }
}
