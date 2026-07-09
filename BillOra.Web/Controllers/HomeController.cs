using System.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace BillOra.Web.Controllers;

public class HomeController : Controller
{
    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    public IActionResult Error()
    {
        return View(new { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
    }
}
