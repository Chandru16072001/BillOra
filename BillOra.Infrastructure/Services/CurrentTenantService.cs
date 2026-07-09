using System.Security.Claims;
using BillOra.Application.Common.Interfaces;
using BillOra.Shared.Constants;
using Microsoft.AspNetCore.Http;

namespace BillOra.Infrastructure.Services;

// Reads CompanyId/StoreId/Role out of the signed-in user's claims.
// Registering ApplicationUser's CompanyId/StoreId as claims happens
// during sign-in (see AccountController) so this stays cheap - no DB hit.
public class CurrentTenantService : ICurrentTenantService
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public CurrentTenantService(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    private ClaimsPrincipal? User => _httpContextAccessor.HttpContext?.User;

    public string? UserId => User?.FindFirstValue(ClaimTypes.NameIdentifier);

    public int? CompanyId
    {
        get
        {
            var val = User?.FindFirstValue("CompanyId");
            return int.TryParse(val, out var id) ? id : null;
        }
    }

    public int? StoreId
    {
        get
        {
            var val = User?.FindFirstValue("StoreId");
            return int.TryParse(val, out var id) ? id : null;
        }
    }

    public bool IsDeveloper => User?.IsInRole(Roles.Developer) ?? false;
}
