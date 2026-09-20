using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Up2Ai.Areas.Admin.Models;
using Up2Ai.Services;

namespace Up2Ai.Areas.Admin.Controllers;

[Area("Admin")]
[Authorize]
public class UsersController : Controller
{
    private readonly ContentStore _store;
    private readonly AdminUserStore _users;

    public UsersController(ContentStore store, AdminUserStore users)
    {
        _store = store;
        _users = users;
    }

    private Cv LoadViewData()
    {
        var content = new Cv(_store.Get());
        ViewData["Content"] = content;
        ViewData["NoIndex"] = true;
        ViewData["Authed"] = User.Identity?.IsAuthenticated ?? false;

        var userId = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
        var currentUser = userId != null ? _users.ById(userId) : null;
        ViewData["CurrentUserId"] = userId;
        ViewData["CurrentUser"] = currentUser;
        ViewData["UserLabel"] = currentUser?.Label ?? "Admin";

        return content;
    }

    /// <summary>List all users</summary>
    [HttpGet("/admin/users")]
    public IActionResult Index(string? changePassword, string? confirmDelete)
    {
        var content = LoadViewData();

        var model = new UsersListViewModel
        {
            C = content,
            All = _users.List(),
            PasswordFormFor = changePassword,
            ConfirmDeleteId = confirmDelete,
            EnvLoginAvailable = false
        };

        if (TempData["users.notice"] is string notice)
            model.Notice = notice;

        return View(model);
    }

    /// <summary>Add a new user</summary>
    [HttpPost("users/add")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Add(UsersListViewModel model)
    {
        var content = LoadViewData();
        model.C = content;

        var result = await _users.AddAsync(model.NewUsername ?? "", model.NewPassword ?? "", model.NewDisplayName ?? "");
        if (!result.Ok)
        {
            model.All = _users.List();
            model.Error = result.Error;
            return View("Index", model);
        }

        TempData["users.notice"] = $"User '{(model.NewUsername ?? "").Trim()}' created.";
        return RedirectToAction("Index");
    }

    /// <summary>Change user password</summary>
    [HttpPost("users/change-password")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ChangePassword(UsersListViewModel model)
    {
        var content = LoadViewData();
        model.C = content;

        var result = await _users.SetPasswordAsync(model.PasswordUserId ?? "", model.NewPasswordForUser ?? "");
        if (!result.Ok)
        {
            model.All = _users.List();
            model.PasswordFormFor = model.PasswordUserId;
            model.Error = result.Error;
            return View("Index", model);
        }

        TempData["users.notice"] = "Password changed.";
        return RedirectToAction("Index");
    }

    /// <summary>Toggle user active status</summary>
    [HttpPost("users/{id}/toggle")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Toggle(string id)
    {
        _ = LoadViewData();

        var user = _users.ById(id);
        if (user is null)
            return RedirectToAction("Index");

        var result = await _users.SetActiveAsync(id, !user.Active, false);
        if (!result.Ok)
        {
            TempData["users.error"] = result.Error;
            return RedirectToAction("Index");
        }

        TempData["users.notice"] = user.Active ? "User disabled." : "User enabled.";
        return RedirectToAction("Index");
    }

    /// <summary>Delete a user</summary>
    [HttpPost("users/{id}/delete")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(string id)
    {
        _ = LoadViewData();

        var result = await _users.DeleteAsync(id, false);
        if (!result.Ok)
        {
            TempData["users.error"] = result.Error;
            return RedirectToAction("Index");
        }

        // If current user deleted themselves, sign out
        var currentUserId = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
        if (id == currentUserId)
        {
            Response.Cookies.Delete(AdminAuth.CookieName, new CookieOptions { Path = AdminAuth.CookiePath });
            return RedirectToAction("Login", "Account");
        }

        TempData["users.notice"] = "User deleted.";
        return RedirectToAction("Index");
    }
}
