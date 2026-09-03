using Microsoft.AspNetCore.Mvc;
using Up2Ai.Services;

namespace Up2Ai.Pages.Admin;

/// <summary>
/// بررسی نظرها.
///
/// هیچ نظری بدون تأیید روی سایت نمی‌رود، پس این صفحه صفِ کارِ روزانه است و
/// پیش‌فرضش هم همان «منتظر تأیید» است — نه کلِ فهرست.
/// </summary>
public class CommentsModel : AdminPageModel
{
    private readonly BlogStore _blog;

    public CommentsModel(ContentStore store, AdminAuth auth, AdminUserStore users, BlogStore blog)
        : base(store, auth, users) => _blog = blog;

    public List<Comment> Comments { get; private set; } = new();
    public Dictionary<string, Post> PostsById { get; private set; } = new();
    public string Filter { get; private set; } = "pending";
    public int PendingCount { get; private set; }
    public int TotalCount { get; private set; }
    public string? Notice { get; private set; }

    public bool OnlyPending => Filter != "all";

    public IActionResult OnGet(string? filter)
    {
        var guard = RequireAuth();
        if (guard is not null) return guard;

        Load(filter);
        if (TempData["comments.notice"] is string n) Notice = n;
        return Page();
    }

    private void Load(string? filter)
    {
        Filter = filter == "all" ? "all" : "pending";
        var all = _blog.AllComments();
        TotalCount = all.Count;
        PendingCount = all.Count(c => !c.Approved);
        Comments = OnlyPending ? all.Where(c => !c.Approved).ToList() : all;
        // عنوانِ نوشته کنار هر نظر لازم است: بدون آن، مدیر نمی‌داند نظر
        // مربوط به کدام نوشته است و باید حدس بزند.
        PostsById = _blog.All().ToDictionary(p => p.Id, p => p, StringComparer.Ordinal);
    }

    public async Task<IActionResult> OnPostApproveAsync(string id, bool approved, string? filter)
    {
        var guard = RequireAuth();
        if (guard is not null) return guard;

        await _blog.SetCommentApprovedAsync(id, approved);
        TempData["comments.notice"] = approved ? "نظر منتشر شد." : "نظر از سایت برداشته شد.";
        return RedirectToPage("/Admin/Comments", new { filter });
    }

    public async Task<IActionResult> OnPostDeleteAsync(string id, string? filter)
    {
        var guard = RequireAuth();
        if (guard is not null) return guard;

        await _blog.DeleteCommentAsync(id);
        TempData["comments.notice"] = "نظر حذف شد.";
        return RedirectToPage("/Admin/Comments", new { filter });
    }
}
