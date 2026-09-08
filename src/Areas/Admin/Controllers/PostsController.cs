using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Up2Ai.Areas.Admin.Models;
using Up2Ai.Services;

namespace Up2Ai.Areas.Admin.Controllers;

[Area("Admin")]
[Authorize]
public class PostsController : Controller
{
    private readonly ContentStore _store;
    private readonly AdminUserStore _users;
    private readonly BlogStore _blog;

    public PostsController(ContentStore store, AdminUserStore users,
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

    /// <summary>List all blog posts</summary>
    [HttpGet("/admin/posts")]
    public IActionResult Index(string? confirmDelete)
    {
        LoadViewData();

        var model = new PostsListViewModel
        {
            Posts = _blog.All(),
            CommentCounts = _blog.ApprovedCounts(),
            ConfirmDeleteId = confirmDelete
        };

        if (TempData["posts.notice"] is string notice)
            model.Notice = notice;

        return View(model);
    }

    /// <summary>Convert body to HTML for preview. Detects if body is HTML or Markdown.</summary>
    private static string BodyToHtml(string body)
    {
        if (string.IsNullOrWhiteSpace(body))
            return "";

        // If body starts with an HTML tag or contains HTML-like structures, treat as HTML
        var trimmed = body.Trim();
        if (trimmed.StartsWith("<", StringComparison.OrdinalIgnoreCase))
            return trimmed;

        // Otherwise treat as Markdown
        return MiniMarkdown.ToHtml(body);
    }

    /// <summary>Edit a blog post (or create new)</summary>
    [HttpGet("/admin/posts/edit")]
    [HttpGet("/admin/posts/{id}/edit")]
    public IActionResult Edit(string? id)
    {
        LoadViewData();

        var model = new PostEditViewModel
        {
            IsNew = string.IsNullOrEmpty(id)
        };

        if (!model.IsNew)
        {
            var post = _blog.ById(id);
            if (post is null)
                return NotFound();

            model.Fill(post);
            // Convert body to HTML for preview (handles both HTML from CKEditor and Markdown from old posts)
            model.PreviewHtml = BodyToHtml(post.Body);
        }
        else
        {
            // New post defaults
            model.Published = false;
            model.CommentsOpen = true;
        }

        if (TempData["post.saved"] is string saved)
            model.Saved = saved;

        return View(model);
    }

    /// <summary>Save a blog post</summary>
    [HttpPost("/admin/posts/save")]
    [HttpPost("/admin/posts/{id}/save")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Save(PostEditViewModel model)
    {
        LoadViewData();

        var title = (model.Title ?? "").Trim();
        if (title.Length < 3)
        {
            model.Error = "Post title must be at least 3 characters.";
            model.PreviewHtml = BodyToHtml(model.Body);
            return View("Edit", model);
        }

        if ((model.Body ?? "").Trim().Length < 20)
        {
            model.Error = "Post body is too short.";
            model.PreviewHtml = BodyToHtml(model.Body);
            return View("Edit", model);
        }

        var saved = await _blog.SaveAsync(new Post
        {
            Id = model.Id ?? "",
            Title = title,
            Slug = (model.Slug ?? "").Trim(),
            Excerpt = (model.Excerpt ?? "").Trim(),
            Body = model.Body ?? "",
            Tags = (model.TagsText ?? "").Split(new[] { '،', ',' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(t => t.Trim())
                .Where(t => t.Length > 0)
                .ToList(),
            Published = model.Published,
            CommentsOpen = model.CommentsOpen,
        });

        TempData["post.saved"] = saved.Published ? "Saved and published." : "Saved as draft.";
        return RedirectToAction("Edit", new { id = saved.Id });
    }

    /// <summary>Delete a blog post</summary>
    [HttpPost("/admin/posts/{id}/delete")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(string id)
    {
        LoadViewData();

        var post = _blog.ById(id);
        if (post is not null)
        {
            await _blog.DeleteAsync(id);
            await _blog.DeleteCommentsOfAsync(id);
            TempData["posts.notice"] = $"'{post.Title}' deleted.";
        }

        return RedirectToAction("Index");
    }

    /// <summary>Toggle post published/draft status</summary>
    [HttpPost("/admin/posts/{id}/toggle")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Toggle(string id)
    {
        LoadViewData();

        var post = _blog.ById(id);
        if (post is not null)
        {
            post.Published = !post.Published;
            await _blog.SaveAsync(post);
            TempData["posts.notice"] = post.Published
                ? $"'{post.Title}' published."
                : $"'{post.Title}' reverted to draft.";
        }

        return RedirectToAction("Index");
    }
}
