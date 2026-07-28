using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace BillOra.Web.Authorization;

// Gates every Painting Shop module controller, same pattern as
// RequireRestaurantAttribute: reads the "BusinessType" claim stamped at
// login, no DB hit per request. Developer accounts pass through so
// support/testing isn't blocked.
public class RequirePaintingShopAttribute : Attribute, IAuthorizationFilter
{
    public void OnAuthorization(AuthorizationFilterContext context)
    {
        var user = context.HttpContext.User;
        if (user.Identity?.IsAuthenticated != true) return; // let [Authorize] handle it
        if (user.IsInRole("Developer")) return;

        var businessType = user.FindFirst("BusinessType")?.Value;
        var isPaintingShop = !string.IsNullOrWhiteSpace(businessType)
            && businessType.Equals("Painting Shop", StringComparison.OrdinalIgnoreCase);

        if (!isPaintingShop)
        {
            context.Result = new NotFoundResult();
        }
    }
}
