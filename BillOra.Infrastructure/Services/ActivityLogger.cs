using BillOra.Application.Common.Interfaces;
using BillOra.Domain.Entities;
using BillOra.Persistence;
using Microsoft.AspNetCore.Http;

namespace BillOra.Infrastructure.Services;

public class ActivityLogger : IActivityLogger
{
    private readonly BillOraDbContext _db;
    private readonly ICurrentTenantService _tenant;
    private readonly IHttpContextAccessor _httpContextAccessor;

    public ActivityLogger(BillOraDbContext db, ICurrentTenantService tenant, IHttpContextAccessor httpContextAccessor)
    {
        _db = db;
        _tenant = tenant;
        _httpContextAccessor = httpContextAccessor;
    }

    public async Task LogAsync(string action, string? details = null)
    {
        _db.ActivityLogs.Add(new ActivityLog
        {
            CompanyId = _tenant.CompanyId,
            UserId = _tenant.UserId,
            Action = action,
            Details = details,
            IpAddress = _httpContextAccessor.HttpContext?.Connection?.RemoteIpAddress?.ToString(),
            Timestamp = DateTime.UtcNow
        });
        await _db.SaveChangesAsync();
    }
}
