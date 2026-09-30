using Microsoft.AspNetCore.Mvc;

namespace Eisai.Web.Controllers;

public class MastersController : Controller
{
    public IActionResult Index() => View();

    public IActionResult Create() => View();

    public IActionResult Records() => View();
}
