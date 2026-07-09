namespace BillOra.Application.Common.Interfaces;

// Resolves "who is logged in and which store/company are they scoped to"
// from the current HTTP context. Implemented in BillOra.Infrastructure
// and used by the DbContext's global query filters for tenant isolation.
public interface ICurrentTenantService
{
    string? UserId { get; }
    int? CompanyId { get; }
    int? StoreId { get; }
    bool IsDeveloper { get; }
}
