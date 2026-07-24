using BillOra.Domain.Common;
using BillOra.Domain.Enums;

namespace BillOra.Domain.Entities;

// Everything in this file only appears in the UI when Store.BusinessType
// is "Restaurant" - see Store.IsRestaurant and the [RequireRestaurant] filter.

public class DiningTable : TenantEntity
{
    public string TableNumber { get; set; } = string.Empty;
    public int Capacity { get; set; } = 4;
    public string? Section { get; set; } // e.g. "Main Hall", "Rooftop", "AC Section"
    public TableStatus Status { get; set; } = TableStatus.Available;
    public int? CurrentOrderId { get; set; } // set while occupied by an open RestaurantOrder
}

public class TableReservation : TenantEntity
{
    public int TableId { get; set; }
    public DiningTable? Table { get; set; }

    public string CustomerName { get; set; } = string.Empty;
    public string? CustomerPhone { get; set; }
    public int PartySize { get; set; } = 2;
    public DateTime ReservationDateTime { get; set; } = DateTime.UtcNow;
    public ReservationStatus Status { get; set; } = ReservationStatus.Booked;
    public string? Notes { get; set; }
}

// Deliberately a lightweight master (not an ApplicationUser) - most
// waitstaff don't need a system login, just a name to print on the bill.
public class Waiter : TenantEntity
{
    public string Name { get; set; } = string.Empty;
    public string? Phone { get; set; }
}

public class RestaurantOrder : TenantEntity
{
    public string OrderNumber { get; set; } = string.Empty;
    public RestaurantOrderType OrderType { get; set; } = RestaurantOrderType.DineIn;
    public RestaurantOrderStatus Status { get; set; } = RestaurantOrderStatus.Open;

    public int? TableId { get; set; }
    public DiningTable? Table { get; set; }
    public int? WaiterId { get; set; }
    public Waiter? Waiter { get; set; }
    public int? CustomerId { get; set; }
    public Customer? Customer { get; set; }

    public int? SaleId { get; set; } // set once billed (single-bill case)
    public string? Notes { get; set; }
    public int LastKotBatch { get; set; } = 0; // incremented each "Send to Kitchen"

    public ICollection<RestaurantOrderItem> Items { get; set; } = new List<RestaurantOrderItem>();
}

public class RestaurantOrderItem
{
    public int Id { get; set; }
    public int OrderId { get; set; }
    public RestaurantOrder? Order { get; set; }

    public int ItemId { get; set; }
    public Item? Item { get; set; }

    public decimal Quantity { get; set; }
    public decimal UnitPrice { get; set; }
    public string? Notes { get; set; } // e.g. "no onions", "extra spicy"

    public int KotBatch { get; set; } = 0; // 0 = not yet sent to kitchen
    public DateTime? KotSentAt { get; set; }

    // Used only when the bill is split - items sharing a group number go on
    // the same generated Sale. Null/0 means "not yet assigned to a split".
    public int SplitGroup { get; set; } = 1;
}
