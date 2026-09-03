using Microsoft.AspNetCore.Mvc;
using Up2Ai.Services;

namespace Up2Ai.Pages.Admin;

/// <summary>فهرست نوشته‌های وبلاگ در پنل.</summary>
public class PostsModel : AdminPageModel
{
    private readonly BlogStore _blog;

    public PostsModel(ContentStore store, AdminAuth auth, AdminUserStore users, BlogStore blog)
        : base(store, auth, users) => _blog = blog;

    public List<Post> Posts { get; private set; } = new();
    public Dictionary<string, int> CommentCounts { get; private set; } = new();
    public string? Notice { get; private set; }

    /// <summary>
    /// شناسه‌ی نوشته‌ای که کاربر روی «حذف» آن زده و هنوز تأیید نکرده.
    ///
    /// حذف عمداً دومرحله‌ای است: یک نوشته ممکن است ساعت‌ها کار باشد و
    /// برگشتی هم ندارد. پرسشِ تأیید به‌جای `confirm()` جاوااسکریپتی، خودش
    /// یک حالت از همین صفحه است — چون کلِ پنل باید بدون جاوااسکریپت هم کامل
    /// کار کند.
    /// </summary>
    public string? ConfirmDeleteId { get; private set; }

    public IActionResult OnGet(string? confirmDelete)
    {
        var guard = RequireAuth();
        if (guard is not null) return guard;

        Posts = _blog.All();
        CommentCounts = _blog.ApprovedCounts();
        ConfirmDeleteId = confirmDelete;
        if (TempData["posts.notice"] is string n) Notice = n;
        return Page();
    }

    /// <summary>
    /// حذف نوشته — نظرهایش هم با آن می‌روند.
    ///
    /// اگر نظرها می‌ماندند، هم فایلِ نظرها بی‌جهت بزرگ می‌شد و هم شمارشِ
    /// «منتظر تأیید» عددی نشان می‌داد که مدیر هیچ‌وقت نمی‌توانست به صفرش
    /// برساند (نظرِ یتیم جایی برای نمایش نداشت).
    /// </summary>
    public async Task<IActionResult> OnPostDeleteAsync(string id)
    {
        var guard = RequireAuth();
        if (guard is not null) return guard;

        var post = _blog.ById(id);
        if (post is not null)
        {
            await _blog.DeleteAsync(id);
            await _blog.DeleteCommentsOfAsync(id);
            TempData["posts.notice"] = $"«{post.Title}» حذف شد.";
        }
        return RedirectToPage("/Admin/Posts");
    }

    /// <summary>انتشار/برگرداندن به پیش‌نویس، بدون رفتن به صفحه‌ی ویرایش.</summary>
    public async Task<IActionResult> OnPostToggleAsync(string id)
    {
        var guard = RequireAuth();
        if (guard is not null) return guard;

        var post = _blog.ById(id);
        if (post is not null)
        {
            post.Published = !post.Published;
            await _blog.SaveAsync(post);
            TempData["posts.notice"] = post.Published
                ? $"«{post.Title}» منتشر شد."
                : $"«{post.Title}» به پیش‌نویس برگشت.";
        }
        return RedirectToPage("/Admin/Posts");
    }
}
