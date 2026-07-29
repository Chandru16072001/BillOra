using BillOra.Domain.Entities;

namespace BillOra.Application.Common.Interfaces;

// Global (not per-store) application branding - Software Name, Logo,
// Tagline, Favicon - managed only by the Developer. Cached in memory since
// this is read on essentially every page render (title, navbar, footer,
// login page, print templates).
public interface IBrandingService
{
    Task<AppBranding> GetAsync();

    // Call after saving changes so the next page load picks them up
    // immediately instead of waiting for the cache to expire.
    void InvalidateCache();
}
