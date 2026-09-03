using Microsoft.AspNetCore.Mvc;
using Up2Ai.Services;

namespace Up2Ai.Pages.Admin;

/// <summary>
/// ساخت و ویرایش یک نوشته.
///
/// همان صفحه هم «تازه» است و هم «ویرایش»: بدونِ `id` یعنی نوشته‌ی تازه.
/// دو صفحه‌ی جدا یعنی دو نسخه از همان فرم که باید با هم هم‌گام بمانند.
/// </summary>
public class PostEditModel : AdminPageModel
{
    private readonly BlogStore _blog;

    public PostEditModel(ContentStore store, AdminAuth auth, AdminUserStore users, BlogStore blog)
        : base(store, auth, users) => _blog = blog;

    [BindProperty] public string Id { get; set; } = "";
    [BindProperty] public string Title { get; set; } = "";
    [BindProperty] public string Slug { get; set; } = "";
    [BindProperty] public string Excerpt { get; set; } = "";
    [BindProperty] public string Body { get; set; } = "";
    [BindProperty] public string TagsText { get; set; } = "";
    [BindProperty] public bool Published { get; set; }
    // بدون مقدارِ اولیه‌ی true: تیکِ برداشته‌شده اصلاً در فرم فرستاده نمی‌شود
    // و اگر این‌جا true بماند، بایندر آن را دست‌نخورده می‌گذارد — یعنی
    // «بستنِ نظرها» هیچ‌وقت ذخیره نمی‌شد. مقدارِ پیش‌فرضِ نوشته‌ی تازه در
    // OnGet گذاشته می‌شود، و خودِ فرم هم یک فیلدِ پنهانِ false کنار هر تیک
    // دارد تا نیت صریح باشد.
    [BindProperty] public bool CommentsOpen { get; set; }

    public bool IsNew => Id.Length == 0;
    public string? Error { get; private set; }
    public string? Saved { get; private set; }

    /// <summary>پیش‌نمایشِ متن، همان‌طور که روی سایت رندر می‌شود.</summary>
    public string PreviewHtml { get; private set; } = "";

    /// <summary>اسلاگِ فعلیِ ذخیره‌شده — برای لینکِ «دیدن روی سایت».</summary>
    public string SavedSlug { get; private set; } = "";

    public bool SavedPublished { get; private set; }

    public IActionResult OnGet(string? id)
    {
        var guard = RequireAuth();
        if (guard is not null) return guard;

        if (!string.IsNullOrEmpty(id))
        {
            var post = _blog.ById(id);
            if (post is null) return NotFound();
            Fill(post);
        }
        else
        {
            // نوشته‌ی تازه پیش‌فرضاً پیش‌نویس است: هیچ نوشته‌ای نباید با یک
            // کلیکِ ناخواسته منتشر شود.
            Published = false;
            CommentsOpen = true;
        }

        if (TempData["post.saved"] is string s) Saved = s;
        return Page();
    }

    private void Fill(Post post)
    {
        Id = post.Id;
        Title = post.Title;
        Slug = post.Slug;
        Excerpt = post.Excerpt;
        Body = post.Body;
        TagsText = string.Join("، ", post.Tags);
        Published = post.Published;
        CommentsOpen = post.CommentsOpen;
        SavedSlug = post.Slug;
        SavedPublished = post.Published;
        PreviewHtml = MiniMarkdown.ToHtml(post.Body);
    }

    public async Task<IActionResult> OnPostAsync()
    {
        var guard = RequireAuth();
        if (guard is not null) return guard;

        var title = (Title ?? "").Trim();
        if (title.Length < 3)
        {
            Error = "عنوان نوشته حداقل ۳ کاراکتر باشد.";
            PreviewHtml = MiniMarkdown.ToHtml(Body);
            return Page();
        }

        if ((Body ?? "").Trim().Length < 20)
        {
            Error = "متن نوشته خیلی کوتاه است.";
            PreviewHtml = MiniMarkdown.ToHtml(Body);
            return Page();
        }

        var saved = await _blog.SaveAsync(new Post
        {
            Id = Id ?? "",
            Title = title,
            Slug = (Slug ?? "").Trim(),
            Excerpt = (Excerpt ?? "").Trim(),
            Body = Body ?? "",
            // برچسب‌ها با ویرگولِ فارسی یا انگلیسی جدا می‌شوند — هر کدام که
            // زیر دستِ نویسنده بوده.
            Tags = (TagsText ?? "").Split(new[] { '،', ',' }, StringSplitOptions.RemoveEmptyEntries).ToList(),
            Published = Published,
            CommentsOpen = CommentsOpen,
        });

        TempData["post.saved"] = saved.Published ? "ذخیره و منتشر شد." : "به‌عنوان پیش‌نویس ذخیره شد.";
        // ری‌دایرکت بعد از ذخیره: رفرشِ صفحه نباید دوباره ذخیره کند، و آدرس
        // باید شناسه‌ی نوشته‌ی تازه‌ساخته را داشته باشد.
        return RedirectToPage("/Admin/PostEdit", new { id = saved.Id });
    }
}
