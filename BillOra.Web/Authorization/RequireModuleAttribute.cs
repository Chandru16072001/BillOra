using BillOra.Persistence;
using BillOra.Shared.Constants;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.EntityFrameworkCore;

namespace BillOra.Web.Authorization;

// Layered on top of [Authorize(Roles = ...)]. Developer and StoreAdmin always
// pass through untouched. For Manager/Cashier, checks StaffModulePermission:
// an explicit "denied" row blocks access; no row at all means the account
// pre-dates the Staff Master feature, so it defaults to allowed rather than
// silently locking out existing logins.
public class RequireModuleAttribute : Attribute, IAsyncAuthorizationFilter
{
    private readonly string _moduleKey;

    public RequireModuleAttribute(string moduleKey)
    {
        _moduleKey = moduleKey;
    }

    public async Task OnAuthorizationAsync(AuthorizationFilterContext context)
    {
        var user = context.HttpContext.User;
        if (user.Identity?.IsAuthenticated != true) return; // let [Authorize] handle it

        if (user.IsInRole(Roles.Developer) || user.IsInRole(Roles.StoreAdmin)) return;

        var userId = user.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrEmpty(userId)) return;

        var db = context.HttpContext.RequestServices.GetRequiredService<BillOraDbContext>();
        var permission = await db.StaffModulePermissions
            .FirstOrDefaultAsync(p => p.ApplicationUserId == userId && p.ModuleKey == _moduleKey);

        if (permission != null && !permission.IsAllowed)
        {
            context.Result = new RedirectToActionResult("AccessDenied", "Account", null);
        }
    }
}
