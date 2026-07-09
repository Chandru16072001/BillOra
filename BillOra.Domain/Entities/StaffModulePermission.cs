using BillOra.Domain.Common;

namespace BillOra.Domain.Entities;

// Fine-grained module access for Cashier/Manager staff, layered on top of the
// coarse Identity role. A StoreAdmin creates staff and ticks which modules
// each one can reach (e.g. a billing-counter hire gets only "POS").
// StoreAdmin and Developer always have full access and are never checked here.
public class StaffModulePermission : TenantEntity
{
    public string ApplicationUserId { get; set; } = string.Empty;
    public string ModuleKey { get; set; } = string.Empty; // see ModuleKeys constants
    public bool IsAllowed { get; set; } = true;
}
