using System.Globalization;
using System.Text;
using System.Text.Json.Serialization;

namespace Up2Ai.Services;

/// <summary>یک نوشته‌ی وبلاگ.</summary>
public sealed class Post
{
    [JsonPropertyName("id")] public string Id { get; set; } = "";
    [JsonPropertyName("slug")] public string Slug { get; set; } = "";
    [JsonPropertyName("title")] public string Title { get; set; } = "";
    [JsonPropertyName("excerpt")] public string Excerpt { get; set; } = "";
    [JsonPropertyName("body")] public string Body { get; set; } = "";
    [JsonPropertyName("tags")] public List<string> Tags { get; set; } = new();
    [JsonPropertyName("published")] public bool Published { get; set; }
    [JsonPropertyName("commentsOpen")] public bool CommentsOpen { get; set; } = true;
    [JsonPropertyName("publishedAt")] public string PublishedAt { get; set; } = "";
    [JsonPropertyName("updatedAt")] public string UpdatedAt { get; set; } = "";

    /// <summary>
    /// زمانِ تقریبیِ خواندن به دقیقه.
    ///
    /// پایه‌ی ۲۰۰ کلمه در دقیقه است — عددِ متعارف برای متنِ غیرتخصصی. کمترین
    /// مقدار یک دقیقه، چون «۰ دقیقه» روی صفحه بی‌معنی است.
    /// </summary>
    public int ReadingMinutes()
    {
        var words = Body.Split(' ', '\n', '\t', '\r').Count(w => w.Trim().Length > 0);
        return Math.Max(1, (int)Math.Round(words / 200.0));
    }
}

/// <summary>یک نظر روی یک نوشته.</summary>
public sealed class Comment
{
    [JsonPropertyName("id")] public string Id { get; set; } = "";
    [JsonPropertyName("postId")] public string PostId { get; set; } = "";
    [JsonPropertyName("name")] public string Name { get; set; } = "";
    [JsonPropertyName("body")] public string Body { get; set; } = "";
    [JsonPropertyName("at")] public string At { get; set; } = "";
    /// <summary>تا مدیر تأیید نکند روی سایت دیده نمی‌شود.</summary>
    [JsonPropertyName("approved")] public bool Approved { get; set; }
}

/// <summary>
/// انبارِ وبلاگ: نوشته‌ها و نظرها.
///
/// چرا وبلاگ اصلاً به سایت اضافه شد: صفحه‌ی تک‌صفحه‌ای برای گوگل تقریباً یک
/// صفحه است و بس. هر نوشته یک صفحه‌ی تازه با آدرس، عنوان و متنِ خودش است —
/// یعنی جای بیشتری برای پیدا شدن با عبارت‌هایی که مشتری واقعاً جست‌وجو
/// می‌کند.
///
/// دو تصمیمِ ساختاری که ارزشِ توضیح دارند:
///
///   ۱) نوشته و نظر در دو فایل جدا نگه داشته می‌شوند، نه تودرتو. نظرها را
///      بازدیدکننده‌ها می‌سازند و تعدادشان می‌تواند از خودِ نوشته‌ها خیلی
///      بیشتر شود؛ با تودرتو بودن، هر نظرِ تازه یعنی بازنویسیِ کلِ نوشته.
///
///   ۲) شناسه‌ی نظر به `postId` وصل است نه به `slug`. اسلاگ قابل ویرایش
///      است (و باید باشد، چون گاهی عنوان عوض می‌شود)؛ اگر نظرها به اسلاگ
///      وصل بودند، یک ویرایشِ ساده همه‌ی نظرهای آن نوشته را یتیم می‌کرد.
/// </summary>
public sealed class BlogStore
{
    private readonly JsonFileStore<Post> _posts;
    private readonly JsonFileStore<Comment> _comments;

    public BlogStore(IWebHostEnvironment env, IConfiguration config, ILogger<BlogStore> log)
    {
        var dir = config["UP2AI_DATA_DIR"] ?? Path.Combine(env.ContentRootPath, "data");
        _posts = new JsonFileStore<Post>(dir, "posts.json",
            p => p.Id.Length > 0 && p.Slug.Length > 0 && p.Title.Length > 0, log);
        _comments = new JsonFileStore<Comment>(dir, "comments.json",
            c => c.Id.Length > 0 && c.PostId.Length > 0 && c.Body.Length > 0, log);
    }

    /* ------------------------------ نوشته‌ها ------------------------------ */

    /// <summary>همه‌ی نوشته‌ها، تازه‌ترین بالا — برای پنل مدیریت.</summary>
    public List<Post> All() => Sorted(_posts.Read());

    /// <summary>فقط نوشته‌های منتشرشده — چیزی که بازدیدکننده می‌بیند.</summary>
    public List<Post> Published() => Sorted(_posts.Read().Where(p => p.Published).ToList());

    private static List<Post> Sorted(List<Post> list) =>
        list.OrderByDescending(p => p.PublishedAt, StringComparer.Ordinal).ToList();

    public Post? ById(string id) => _posts.Read().FirstOrDefault(p => p.Id == id);

    /// <summary>
    /// یافتنِ نوشته با اسلاگ. <paramref name="includeDrafts"/> فقط برای پنل
    /// است تا مدیر بتواند پیش‌نویس را قبل از انتشار ببیند.
    /// </summary>
    public Post? BySlug(string slug, bool includeDrafts = false) =>
        _posts.Read().FirstOrDefault(p =>
            string.Equals(p.Slug, slug, StringComparison.OrdinalIgnoreCase)
            && (includeDrafts || p.Published));

    public Task<Post> SaveAsync(Post input) => _posts.MutateAsync(list =>
    {
        var now = Iso(DateTime.UtcNow);
        var existing = input.Id.Length > 0 ? list.FirstOrDefault(p => p.Id == input.Id) : null;

        var slug = Slugify(input.Slug.Length > 0 ? input.Slug : input.Title);
        slug = UniqueSlug(list, slug, existing?.Id);

        if (existing is null)
        {
            var post = new Post
            {
                Id = Guid.NewGuid().ToString(),
                Slug = slug,
                Title = input.Title.Trim(),
                Excerpt = input.Excerpt.Trim(),
                Body = input.Body,
                Tags = CleanTags(input.Tags),
                Published = input.Published,
                CommentsOpen = input.CommentsOpen,
                // زمانِ انتشار همان لحظه‌ی ساخت است، حتی اگر پیش‌نویس بماند —
                // وگرنه پیش‌نویس‌ها در فهرستِ پنل بی‌ترتیب می‌شدند.
                PublishedAt = now,
                UpdatedAt = now,
            };
            list.Add(post);
            return (true, post);
        }

        existing.Slug = slug;
        existing.Title = input.Title.Trim();
        existing.Excerpt = input.Excerpt.Trim();
        existing.Body = input.Body;
        existing.Tags = CleanTags(input.Tags);
        existing.CommentsOpen = input.CommentsOpen;
        // اولین انتشار، تاریخِ انتشار را به همین حالا می‌برد: نوشته‌ای که سه
        // هفته پیش‌نویس بوده، روزِ انتشار باید بالای فهرست بیاید نه ته آن.
        if (input.Published && !existing.Published) existing.PublishedAt = now;
        existing.Published = input.Published;
        existing.UpdatedAt = now;
        return (true, existing);
    });

    public Task<bool> DeleteAsync(string id) => _posts.MutateAsync(list =>
    {
        var found = list.FirstOrDefault(p => p.Id == id);
        if (found is null) return (false, false);
        list.Remove(found);
        return (true, true);
    });

    /* ------------------------------- نظرها ------------------------------- */

    /// <summary>نظرهای تأییدشده‌ی یک نوشته، قدیمی‌تر بالا (ترتیبِ طبیعیِ گفت‌وگو).</summary>
    public List<Comment> ApprovedFor(string postId) =>
        _comments.Read()
            .Where(c => c.PostId == postId && c.Approved)
            .OrderBy(c => c.At, StringComparer.Ordinal)
            .ToList();

    /// <summary>همه‌ی نظرها برای پنل — تازه‌ترین بالا، چون کارِ مدیر با تازه‌هاست.</summary>
    public List<Comment> AllComments() =>
        _comments.Read()
            .OrderByDescending(c => c.At, StringComparer.Ordinal)
            .ToList();

    public int PendingCount() => _comments.Read().Count(c => !c.Approved);

    /// <summary>شمارشِ نظرهای تأییدشده به تفکیک نوشته — برای نشان دادن کنار هر نوشته.</summary>
    public Dictionary<string, int> ApprovedCounts() =>
        _comments.Read()
            .Where(c => c.Approved)
            .GroupBy(c => c.PostId)
            .ToDictionary(g => g.Key, g => g.Count(), StringComparer.Ordinal);

    public Task<Comment> AddCommentAsync(string postId, string name, string body) =>
        _comments.MutateAsync(list =>
        {
            var comment = new Comment
            {
                Id = Guid.NewGuid().ToString(),
                PostId = postId,
                Name = name.Trim(),
                Body = body.Trim(),
                At = Iso(DateTime.UtcNow),
                // پیش‌فرض: منتشر نشده. هر نظر اول از نظرِ مدیر رد می‌شود —
                // برای فرمی که هر کسی می‌تواند پرش کند، این تنها حالتِ امن است.
                Approved = false,
            };
            list.Add(comment);
            return (true, comment);
        });

    public Task<bool> SetCommentApprovedAsync(string id, bool approved) =>
        _comments.MutateAsync(list =>
        {
            var found = list.FirstOrDefault(c => c.Id == id);
            if (found is null || found.Approved == approved) return (false, false);
            found.Approved = approved;
            return (true, true);
        });

    public Task<bool> DeleteCommentAsync(string id) => _comments.MutateAsync(list =>
    {
        var found = list.FirstOrDefault(c => c.Id == id);
        if (found is null) return (false, false);
        list.Remove(found);
        return (true, true);
    });

    /// <summary>
    /// نظرهای یک نوشته را پاک می‌کند — هنگام حذفِ خودِ نوشته صدا زده می‌شود.
    /// بدون این، نظرها به شناسه‌ای اشاره می‌کردند که دیگر وجود ندارد و برای
    /// همیشه در فایل می‌ماندند.
    /// </summary>
    public Task<int> DeleteCommentsOfAsync(string postId) => _comments.MutateAsync(list =>
    {
        var gone = list.RemoveAll(c => c.PostId == postId);
        return (gone > 0, gone);
    });

    /* ------------------------------ کمکی‌ها ------------------------------ */

    public static string Iso(DateTime utc) =>
        utc.ToString("yyyy-MM-ddTHH:mm:ss.fffZ", CultureInfo.InvariantCulture);

    private static List<string> CleanTags(IEnumerable<string> tags) =>
        tags.Select(t => t.Trim())
            .Where(t => t.Length is > 0 and <= 40)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(8)
            .ToList();

    private static string UniqueSlug(List<Post> list, string slug, string? ignoreId)
    {
        var candidate = slug;
        var n = 2;
        while (list.Any(p => p.Id != ignoreId && string.Equals(p.Slug, candidate, StringComparison.OrdinalIgnoreCase)))
            candidate = $"{slug}-{n++}";
        return candidate;
    }

    /// <summary>
    /// عنوان → اسلاگِ آدرس.
    ///
    /// حروف فارسی عمداً نگه داشته می‌شوند: آدرسِ فارسی هم در مرورگر کار
    /// می‌کند و هم برای جست‌وجوی فارسی خواناتر است. فقط چیزهایی که در URL
    /// دردسر می‌سازند (فاصله، نقطه‌گذاری، اسلش) حذف یا به خط تیره تبدیل
    /// می‌شوند.
    ///
    /// نیم‌فاصله (U+200C) هم به خط تیره تبدیل می‌شود، چون در آدرس دیده
    /// نمی‌شود و دو کلمه را به هم می‌چسباند.
    /// </summary>
    public static string Slugify(string input)
    {
        var sb = new StringBuilder();
        foreach (var ch in input.Trim().ToLowerInvariant())
        {
            if (char.IsLetterOrDigit(ch)) sb.Append(ch);
            else if (ch is ' ' or '-' or '_' or '‌' or '\t' or '\n') sb.Append('-');
            // بقیه (نقطه، ویرگول، پرانتز، اسلش، …) حذف می‌شوند.
        }

        var slug = sb.ToString();
        while (slug.Contains("--")) slug = slug.Replace("--", "-");
        slug = slug.Trim('-');
        if (slug.Length > 80) slug = slug[..80].Trim('-');
        return slug.Length > 0 ? slug : "post-" + Guid.NewGuid().ToString("n")[..8];
    }
}
