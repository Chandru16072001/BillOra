using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace BillOra.Web.Authorization;

// Gates every restaurant-module controller. Reads the "BusinessType" claim
// stamped at login (see AccountController) - cheap, no DB hit per request.
// Developer accounts (which aren't tied to a single store) are allowed
// through so support/testing isn't blocked.
public class RequireRestaurantAttribute : Attribute, IAuthorizationFilter
{
    public void OnAuthorization(AuthorizationFilterContext context)
    {
        var user = context.HttpContext.User;
        if (user.Identity?.IsAuthenticated != true) return; // let [Authorize] handle it
        if (user.IsInRole("Developer")) return;

        var businessType = user.FindFirst("BusinessType")?.Value;
        var isRestaurant = !string.IsNullOrWhiteSpace(businessType)
            && businessType.Equals("Restaurant", StringComparison.OrdinalIgnoreCase);

        if (!isRestaurant)
        {
            context.Result = new NotFoundResult();
        }
    }
}
