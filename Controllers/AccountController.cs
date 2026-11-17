using ClinicaVeterinaria.Data;
using ClinicaVeterinaria.Services;
using ClinicaVeterinaria.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
namespace ClinicaVeterinaria.Controllers;
public class AccountController : Controller
{
    private readonly AppDbContext _context;
    private readonly TokenService _tokenService;
    private readonly IConfiguration _configuration;
    public AccountController(AppDbContext context, TokenService tokenService, IConfiguration configuration)
    {
        _context = context;
        _tokenService = tokenService;
        _configuration = configuration;
    }
    [AllowAnonymous]
    [HttpGet]
    public IActionResult Login() => View();
    [AllowAnonymous]
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Login(LoginViewModel model)
    {
        if (!ModelState.IsValid) return View(model);
        var usuario = await _context.Usuarios
.AsNoTracking()
            .FirstOrDefaultAsync(u => u.Email == model.Email);
        if (usuario is null || !BCrypt.Net.BCrypt.Verify(model.Senha, usuario.SenhaHash))
        {
            ModelState.AddModelError(string.Empty, "E-mail ou senha inválidos.");
            return View(model);
        }
        var token = _tokenService.GerarToken(usuario);
        var minutes = _configuration.GetValue<int>("Jwt:ExpireMinutes", 30);
        Response.Cookies.Append("jwt", token, new CookieOptions
        {
            HttpOnly = true,
            Secure = true,
            SameSite = SameSiteMode.Strict,
            Expires = DateTimeOffset.UtcNow.AddMinutes(minutes),
            IsEssential = true
        });
        return RedirectToAction("Index", "Home");
    }
    [Authorize]
    [HttpPost]
    [ValidateAntiForgeryToken]
    public IActionResult Logout()
    {
        Response.Cookies.Delete("jwt");
        return RedirectToAction(nameof(Login));
    }
}