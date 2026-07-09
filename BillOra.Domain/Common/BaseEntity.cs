namespace BillOra.Domain.Common;

// Base for every tenant-scoped entity. StoreId drives the multi-tenant
// EF Core global query filter configured in BillOraDbContext.
public abstract class BaseEntity
{
    public int Id { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }
    public bool IsActive { get; set; } = true;
    public bool IsDeleted { get; set; } = false; // soft delete
}

public abstract class TenantEntity : BaseEntity
{
    public int StoreId { get; set; }
}
