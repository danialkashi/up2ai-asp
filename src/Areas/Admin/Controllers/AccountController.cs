using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Up2Ai.Areas.Admin.Models;
using Up2Ai.Services;
using System.Security.Claims;

namespace Up2Ai.Areas.Admin.Controllers;

[Area("Admin")]
public class AccountController : Controller
{
    private readonly IConfiguration _config;
    private readonly AdminUserStore _users;

    public AccountController(AdminUserStore users,
                             IConfiguration config)
    {
        _users = users;
        _config = config;
    }

    /// <summary>Login page</summary>
    [Route("/admin/account/login")]
    [HttpGet]
    [AllowAnonymous]
    public IActionResult Login()
    {
        // If already logged in, redirect to dashboard
        if (User.Identity?.IsAuthenticated == true)
            return RedirectToAction("Index", "Dashboard");

        var model = new LoginViewModel
        {
            ShowUsername = !_users.IsEmpty()
        };

        return View(model);
    }

    /// <summary>Login POST handler</summary>
    [Route("/admin/account/login")]
    [HttpPost]
    [AllowAnonymous]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Login(LoginViewModel model)
    {
        model.ShowUsername = !_users.IsEmpty();

        // Rate limiting by IP
        var key = ClientIp.Of(HttpContext, _config);
        var (allowed, retryInSec) = AdminAuth.ThrottleCheck(key);
        
        if (!allowed)
        {
            var minutes = (int)Math.Ceiling(retryInSec / 60.0);
            model.Error = $"Too many failed attempts. Try again in {PersianYear.ToPersianDigits(minutes.ToString())} minutes.";
            return View(model);
        }

        var password = model.Password ?? "";
        var username = (model.Username ?? "").Trim();
        string? userId = null;

        if (password.Length > 0 && username.Length > 0)
        {
            var user = _users.Verify(username, password);
            if (user is not null)
            {
                userId = user.Id;
                await _users.TouchLoginAsync(user.Id);
            }
        }

        if (userId is null)
        {
            AdminAuth.ThrottleFail(key);
            model.Error = model.ShowUsername ? "Invalid username or password." : "Invalid password.";
            return View(model);
        }

        AdminAuth.ThrottleReset(key);

        // Sign in with ASP.NET Core cookie authentication
        var userData = _users.ById(userId);
        var claims = new List<Claim>
        {
            new Claim(ClaimTypes.NameIdentifier, userId),
            new Claim(ClaimTypes.Name, userData?.Username ?? "admin"),
            new Claim("PasswordVersion", userData?.PasswordVersion.ToString() ?? "1"),
        };

        var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
        var principal = new ClaimsPrincipal(identity);
        var authProperties = new AuthenticationProperties
        {
            IsPersistent = true,
            ExpiresUtc = DateTimeOffset.UtcNow.AddHours(12),
        };

        await HttpContext.SignInAsync(
            CookieAuthenticationDefaults.AuthenticationScheme,
            principal,
            authProperties);

        return RedirectToAction("Index", "Dashboard");
    }

    /// <summary>Logout - GET form to prevent CSRF</summary>
    [Route("/admin/account/logout")]
    [HttpGet]
    [Authorize]
    public IActionResult Logout()
    {
        return View("LogoutConfirm");
    }

    /// <summary>Logout - POST to actually sign out</summary>
    [Route("/admin/account/logout")]
    [HttpPost]
    [Authorize]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> LogoutPost()
    {
        await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        return RedirectToAction("Login");
    }

    /// <summary>Change own password - show form</summary>
    [Route("/admin/account/password")]
    [HttpGet]
    [Authorize]
    public IActionResult ChangePassword()
    {
        return View("~/Areas/Admin/Views/Account/ChangePassword.cshtml", new ChangePasswordViewModel());
    }

    /// <summary>Change own password - process submission</summary>
    [Route("/admin/account/password")]
    [HttpPost]
    [Authorize]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ChangePassword(ChangePasswordViewModel model)
    {
        var userId = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
        
        if (string.IsNullOrEmpty(userId))
        {
            return Unauthorized();
        }

        // Verify current password
        var currentPassword = model.CurrentPassword ?? "";
        var newPassword = model.NewPassword ?? "";

        if (string.IsNullOrWhiteSpace(currentPassword) || string.IsNullOrWhiteSpace(newPassword))
        {
            model.Error = "تمام فیلدها الزامی‌اند.";
            return View("~/Areas/Admin/Views/Account/ChangePassword.cshtml", model);
        }

        var result = await _users.ChangePasswordAsync(userId, currentPassword, newPassword);
        
        if (!result.Ok)
        {
            model.Error = result.Error;
            return View("~/Areas/Admin/Views/Account/ChangePassword.cshtml", model);
        }

        // Password changed successfully
        // Sign user out to require re-authentication with new password
        await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        return RedirectToAction("Login", new { changed = "true" });
    }
}
