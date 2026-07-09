using Microsoft.AspNetCore.Identity;

namespace BillOra.Persistence.Identity;

// Extends Identity's user with tenant scoping. Developer users have
// CompanyId/StoreId = null (they operate above the tenant boundary).
public class ApplicationUser : IdentityUser
{
    public string FullName { get; set; } = string.Empty;
    public int? CompanyId { get; set; }
    public int? StoreId { get; set; }
    public bool IsActive { get; set; } = true; // Store Admin can deactivate a staff login without deleting it
}
