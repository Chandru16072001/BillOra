using BillOra.Domain.Common;

namespace BillOra.Domain.Entities;

// Application-wide branding, managed only by the Developer. Deliberately
// NOT a TenantEntity - this is a single global row, the same for every
// store/client, not scoped per-store like everything else in the app.
public class AppBranding : BaseEntity
{
    public string SoftwareName { get; set; } = "BillOra";
    public string? LogoPath { get; set; }
    public string? Tagline { get; set; }
    public string? FaviconPath { get; set; }
}
