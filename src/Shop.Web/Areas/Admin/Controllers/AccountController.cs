using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Shop.Web.Areas.Admin.Models;
using Shop.Web.Infrastructure;

namespace Shop.Web.Areas.Admin.Controllers;

public class AccountController(AdminAccountService accounts) : AdminController
{
    [AllowAnonymous, HttpGet("/admin/login")]
    public IActionResult Login(string? returnUrl) =>
        View(new LoginInput { ReturnUrl = returnUrl });

    [AllowAnonymous, HttpPost("/admin/login"), EnableRateLimiting("login")]
    public async Task<IActionResult> Login(LoginInput input)
    {
        if (!ModelState.IsValid)
            return View(input);

        var user = await accounts.VerifyAsync(input.Email, input.Password);
        if (user is null)
        {
            ModelState.AddModelError("", "Wrong email or password.");
            return View(input);
        }

        var identity = new ClaimsIdentity(
            [new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()), new Claim(ClaimTypes.Name, user.Email)],
            CookieAuthenticationDefaults.AuthenticationScheme);
        await HttpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, new ClaimsPrincipal(identity));

        return Url.IsLocalUrl(input.ReturnUrl) ? Redirect(input.ReturnUrl) : Redirect("/admin");
    }

    [HttpPost("/admin/logout")]
    public async Task<IActionResult> Logout()
    {
        await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        return Redirect("/admin/login");
    }

    [HttpGet("/admin/account/password")]
    public IActionResult Password() => View(new ChangePasswordInput());

    [HttpPost("/admin/account/password")]
    public async Task<IActionResult> Password(ChangePasswordInput input)
    {
        if (!ModelState.IsValid)
            return View(input);
        var userId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        if (!await accounts.ChangePasswordAsync(userId, input.CurrentPassword, input.NewPassword))
        {
            ModelState.AddModelError(nameof(input.CurrentPassword), "The current password is wrong.");
            return View(input);
        }
        Flash("Password changed.");
        return Redirect("/admin");
    }
}
