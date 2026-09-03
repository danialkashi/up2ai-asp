using Microsoft.AspNetCore.Mvc;
using Up2Ai.Services;

namespace Up2Ai.Pages.Blog;

/// <summary>
/// فهرست نوشته‌های وبلاگ.
///
/// صفحه‌بندی عمداً با پارامترِ آدرس (`/blog?page=2`) است و نه با اسکرولِ
/// بی‌نهایت: هر صفحه آدرسِ خودش را دارد، پس هم گوگل می‌تواند دنبالش برود و
/// هم بدون جاوااسکریپت کار می‌کند.
/// </summary>
public class BlogIndexModel : ContentPageModel
{
    /// <summary>
    /// ده نوشته در هر صفحه، مگر آن‌که از پنل عدد دیگری انتخاب شده باشد.
    /// کم‌تر یعنی صفحه‌بندیِ زودهنگام برای وبلاگی که تازه شروع شده؛ بیشتر
    /// یعنی صفحه‌ی سنگین روی اینترنتِ موبایل.
    /// </summary>
    public const int DefaultPageSize = 10;

    /// <summary>عددِ همین درخواست — از محتوا خوانده می‌شود.</summary>
    public int PageSize { get; private set; } = DefaultPageSize;

    private readonly BlogStore _blog;

    public BlogIndexModel(ContentStore store, BlogStore blog) : base(store) => _blog = blog;

    public List<Post> Posts { get; private set; } = new();
    public Dictionary<string, int> CommentCounts { get; private set; } = new();
    public int PageNumber { get; private set; } = 1;
    public int TotalPages { get; private set; } = 1;

    /// <summary>
    /// شماره‌ی صفحه از رشته‌ی پرسمان.
    ///
    /// `[FromQuery]` این‌جا لازم است و تزئینی نیست: در Razor Pages کلیدِ
    /// «page» یک کلیدِ رزروشده‌ی مسیریابی است و مقدارش مسیرِ خودِ صفحه
    /// («/Blog/Index») است. بدون این ویژگی، بایندر همان مقدار را برمی‌داشت،
    /// در تبدیل به عدد شکست می‌خورد و همیشه null می‌گرفتیم — یعنی
    /// `/blog?page=2` بی‌صدا همان صفحه‌ی اول را نشان می‌داد.
    /// </summary>
    public void OnGet([FromQuery] int? page)
    {
        // عددِ پنل. اگر کسی دستی فایل محتوا را دست‌کاری کند و چیز بی‌ربطی
        // بگذارد، به‌جای خطا به پیش‌فرض برمی‌گردیم و در بازه‌ی معقول می‌مانیم —
        // یک «0» می‌توانست تقسیم‌بر‌صفر بدهد و کل صفحه را بخواباند.
        PageSize = int.TryParse(C["copy"]["blog"]["pageSize"].S, out var n)
            ? Math.Clamp(n, 3, 50)
            : DefaultPageSize;

        var all = _blog.Published();
        TotalPages = Math.Max(1, (int)Math.Ceiling(all.Count / (double)PageSize));
        // شماره‌ی صفحه از آدرس می‌آید، پس هر عددی ممکن است بیاید — داخل بازه
        // نگهش می‌داریم تا `/blog?page=999` صفحه‌ی خالی ندهد.
        PageNumber = Math.Clamp(page ?? 1, 1, TotalPages);
        Posts = all.Skip((PageNumber - 1) * PageSize).Take(PageSize).ToList();
        CommentCounts = _blog.ApprovedCounts();
    }
}
