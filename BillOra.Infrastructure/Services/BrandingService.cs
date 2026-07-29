using BillOra.Application.Common.Interfaces;
using BillOra.Domain.Entities;
using BillOra.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace BillOra.Infrastructure.Services;

public class BrandingService : IBrandingService
{
    private const string CacheKey = "BillOra.AppBranding";

    private readonly BillOraDbContext _db;
    private readonly IMemoryCache _cache;

    public BrandingService(BillOraDbContext db, IMemoryCache cache)
    {
        _db = db;
        _cache = cache;
    }

    public async Task<AppBranding> GetAsync()
    {
        if (_cache.TryGetValue(CacheKey, out AppBranding? cached) && cached != null)
            return cached;

        // Single global row by convention - there is only ever one.
        var branding = await _db.AppBrandings.AsNoTracking().OrderBy(b => b.Id).FirstOrDefaultAsync()
            ?? new AppBranding();

        _cache.Set(CacheKey, branding, TimeSpan.FromHours(1));
        return branding;
    }

    public void InvalidateCache() => _cache.Remove(CacheKey);
}
