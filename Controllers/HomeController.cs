using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
namespace ClinicaVeterinaria.Controllers;
[Authorize]
public class HomeController : Controller
{
    public IActionResult Index() => View();
    [AllowAnonymous]
    public IActionResult Error() => View(new ClinicaVeterinaria.Models.ErrorViewModel
    {
        RequestId = System.Diagnostics.Activity.Current?.Id ?? HttpContext.TraceIdentifier
    });
}