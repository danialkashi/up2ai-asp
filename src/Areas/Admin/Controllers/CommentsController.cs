using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Up2Ai.Areas.Admin.Models;
using Up2Ai.Services;

namespace Up2Ai.Areas.Admin.Controllers;

[Area("Admin")]
[Authorize]
public class CommentsController : Controller
{
    private readonly ContentStore _store;
    private readonly AdminUserStore _users;
    private readonly BlogStore _blog;

    public CommentsController(ContentStore store, AdminUserStore users,
                              BlogStore blog)
    {
        _store = store;
        _users = users;
        _blog = blog;
    }

    private void LoadViewData()
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
    }

    /// <summary>List blog comments for moderation</summary>
    [HttpGet("/admin/comments")]
    public IActionResult Index(string? filter)
    {
        LoadViewData();

        var all = _blog.AllComments();
        var onlyPending = filter != "all";
        var comments = onlyPending ? all.Where(c => !c.Approved).ToList() : all;
        var posts = _blog.All().ToDictionary(p => p.Id, p => p, StringComparer.Ordinal);

        var model = new CommentsListViewModel
        {
            Filter = filter ?? "pending",
            OnlyPending = onlyPending,
            Comments = comments,
            PostsById = posts,
            TotalCount = all.Count,
            PendingCount = all.Count(c => !c.Approved)
        };

        if (TempData["comments.notice"] is string notice)
            model.Notice = notice;

        return View(model);
    }

    /// <summary>Approve or reject a comment</summary>
    [HttpPost("comments/{id}/approve")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Approve(string id, bool approved, string? filter)
    {
        LoadViewData();

        await _blog.SetCommentApprovedAsync(id, approved);
        TempData["comments.notice"] = approved ? "Comment published." : "Comment removed from site.";

        return RedirectToAction("Index", new { filter = filter ?? "pending" });
    }

    /// <summary>Delete a comment permanently</summary>
    [HttpPost("comments/{id}/delete")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(string id, string? filter)
    {
        LoadViewData();

        await _blog.DeleteCommentAsync(id);
        TempData["comments.notice"] = "Comment deleted.";

        return RedirectToAction("Index", new { filter = filter ?? "pending" });
    }
}
