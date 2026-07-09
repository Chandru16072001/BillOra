using System.Security.Claims;
using BillOra.Persistence.Identity;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;

namespace BillOra.Web.Controllers;

public class AccountController : Controller
{
    private readonly SignInManager<ApplicationUser> _signInManager;
    private readonly UserManager<ApplicationUser> _userManager;

    public AccountController(SignInManager<ApplicationUser> signInManager, UserManager<ApplicationUser> userManager)
    {
        _signInManager = signInManager;
        _userManager = userManager;
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

        // Stamp CompanyId/StoreId onto the auth cookie so ICurrentTenantService
        // (and every EF Core query filter) can read them without a DB round-trip.
        var claims = new List<Claim>();
        if (user.CompanyId.HasValue) claims.Add(new Claim("CompanyId", user.CompanyId.Value.ToString()));
        if (user.StoreId.HasValue) claims.Add(new Claim("StoreId", user.StoreId.Value.ToString()));

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
