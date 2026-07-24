using System.Security.Claims;
using BillOra.Persistence;
using BillOra.Persistence.Identity;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;

namespace BillOra.Web.Controllers;

public class AccountController : Controller
{
    private readonly SignInManager<ApplicationUser> _signInManager;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly BillOraDbContext _db;

    public AccountController(SignInManager<ApplicationUser> signInManager, UserManager<ApplicationUser> userManager, BillOraDbContext db)
    {
        _signInManager = signInManager;
        _userManager = userManager;
        _db = db;
    }

    [HttpGet]
    public IActionResult Login(string? returnUrl = null)
    {
        if (User.Identity?.IsAuthenticated == true) return RedirectToAction("Index", "Dashboard");
        ViewData["ReturnUrl"] = returnUrl;
        return View();
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Login(string email, string password, bool rememberMe, string? returnUrl = null)
    {
        var user = await _userManager.FindByEmailAsync(email);
        if (user == null || !await _userManager.CheckPasswordAsync(user, password))
        {
            ModelState.AddModelError(string.Empty, "Invalid email or password.");
            return View();
        }

        if (await _userManager.IsLockedOutAsync(user))
        {
            ModelState.AddModelError(string.Empty, "This account has been deactivated. Contact your Store Admin.");
            return View();
        }

        var roles = await _userManager.GetRolesAsync(user);

        // Stamp CompanyId/StoreId/BusinessType onto the auth cookie so
        // ICurrentTenantService, EF Core query filters, and the restaurant-module
        // gating (see RequireRestaurantAttribute) can all read them without a DB round-trip.
        var claims = new List<Claim>();
        if (user.CompanyId.HasValue) claims.Add(new Claim("CompanyId", user.CompanyId.Value.ToString()));
        if (user.StoreId.HasValue) claims.Add(new Claim("StoreId", user.StoreId.Value.ToString()));

        if (user.StoreId.HasValue)
        {
            var store = await _db.Stores.FindAsync(user.StoreId.Value);
            if (store != null) claims.Add(new Claim("BusinessType", store.BusinessType ?? ""));
        }

        await _signInManager.SignInWithClaimsAsync(user, rememberMe, claims);

        if (!string.IsNullOrEmpty(returnUrl) && Url.IsLocalUrl(returnUrl))
            return Redirect(returnUrl);

        return roles.Contains("Developer")
            ? RedirectToAction("Index", "Developer")
            : RedirectToAction("Index", "Dashboard");
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Logout()
    {
        await _signInManager.SignOutAsync();
        return RedirectToAction("Login");
    }

    [HttpGet]
    public IActionResult AccessDenied() => View();
}
