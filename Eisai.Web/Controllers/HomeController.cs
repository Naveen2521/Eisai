using Microsoft.AspNetCore.Mvc;

namespace Eisai.Web.Controllers;

public class HomeController : Controller
{
    public IActionResult Index() => View();
}
