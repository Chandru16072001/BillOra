using Microsoft.AspNetCore.Mvc;

namespace BillOra.Web.Controllers;

public class SubscriptionController : Controller
{
    [HttpGet]
    public IActionResult Expired() => View();
}
