using BillOra.Domain.Common;
using BillOra.Domain.Enums;

namespace BillOra.Domain.Entities;

// The tenant root. One Company can own many Stores (multi-branch).
public class Company : BaseEntity
{
    public string Name { get; set; } = string.Empty;
    public string OwnerEmail { get; set; } = string.Empty;
    public string? Phone { get; set; }
    public string? Address { get; set; }
    public string? GstNumber { get; set; }

    public string LicenseKey { get; set; } = Guid.NewGuid().ToString("N").ToUpper();
    public string PlanName { get; set; } = "Trial";
    public int MaxStaffUsers { get; set; } = 5; // licensing limit enforced when Store Admin creates staff
    public DateTime SubscriptionStartDate { get; set; } = DateTime.UtcNow;
    public DateTime SubscriptionEndDate { get; set; } = DateTime.UtcNow.AddDays(14);
    public SubscriptionStatus SubscriptionStatus { get; set; } = SubscriptionStatus.Active;
    public string? Notes { get; set; }
    public DateTime? LastLoginAt { get; set; }

    public ICollection<Store> Stores { get; set; } = new List<Store>();
}
