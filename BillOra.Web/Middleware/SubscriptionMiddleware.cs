using BillOra.Persistence;
using BillOra.Shared.Constants;
using Microsoft.EntityFrameworkCore;

namespace BillOra.Web.Middleware;

// Mirrors SRS section 14: once a Company's subscription lapses, every
// non-Developer user is locked out with a clear message, while the
// Developer portal itself always stays reachable so the license can be renewed.
public class SubscriptionMiddleware
{
    private readonly RequestDelegate _next;

    public SubscriptionMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(HttpContext context, BillOraDbContext db)
    {
        var path = context.Request.Path.Value ?? string.Empty;
        var isExempt = path.StartsWith("/Account", StringComparison.OrdinalIgnoreCase)
                       || path.StartsWith("/Developer", StringComparison.OrdinalIgnoreCase)
                       || path.StartsWith("/Subscription", StringComparison.OrdinalIgnoreCase)
                       || path.StartsWith("/css") || path.StartsWith("/js") || path.StartsWith("/lib");

        if (!isExempt && context.User.Identity?.IsAuthenticated == true && !context.User.IsInRole(Roles.Developer))
        {
            var companyIdClaim = context.User.FindFirst("CompanyId")?.Value;
            if (int.TryParse(companyIdClaim, out var companyId))
            {
                var company = await db.Companies.IgnoreQueryFilters().AsNoTracking()
                    .FirstOrDefaultAsync(c => c.Id == companyId);

                var expired = company == null || !company.IsActive || company.SubscriptionEndDate < DateTime.UtcNow;
                if (expired)
                {
                    context.Response.Redirect("/Subscription/Expired");
                    return;
                }
            }
        }

        await _next(context);
    }
}
