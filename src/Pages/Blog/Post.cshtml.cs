using Microsoft.AspNetCore.Mvc;
using Up2Ai.Services;

namespace Up2Ai.Pages.Blog;

/// <summary>
/// یک نوشته‌ی وبلاگ، به‌همراه نظرها و فرمِ ثبت نظر.
///
/// فرمِ نظر دقیقاً همان لایه‌های ضدّاسپمِ فرمِ مشاوره را دارد (کپچا، فیلدِ
/// تله، کفِ زمانِ پر کردن، سقفِ ارسال از یک IP) — با این تفاوت که سهمیه‌ی
/// جداگانه‌ای دارد: کسی که نظر گذاشته نباید نتواند درخواست مشاوره بفرستد.
///
/// و یک تفاوتِ مهم با فرم مشاوره: نظر پس از ثبت *منتشر نمی‌شود*. تا مدیر
/// تأیید نکند فقط خودِ فرستنده پیامِ «ثبت شد و بعد از بررسی منتشر می‌شود» را
/// می‌بیند. برای فرمی که هر کسی روی اینترنت می‌تواند پرش کند، این تنها
/// حالتِ امن است.
/// </summary>
public class BlogPostModel : ContentPageModel
{
    private const int MaxCommentLength = 2000;

    /// <summary>
    /// سقفِ نام. بدون این، یک ارسالِ خودکار می‌توانست چند کیلوبایت «نام»
    /// بفرستد که هم چیدمانِ فهرست نظرها را می‌شکست و هم بی‌دلیل در فایل
    /// می‌نشست.
    /// </summary>
    private const int MaxNameLength = 60;

    private readonly BlogStore _blog;
    private readonly FormGuard _guard;
    private readonly IConfiguration _config;
    private readonly ILogger<BlogPostModel> _log;

    public BlogPostModel(ContentStore store, BlogStore blog, FormGuard guard,
                         IConfiguration config, ILogger<BlogPostModel> log) : base(store)
    {
        _blog = blog;
        _guard = guard;
        _config = config;
        _log = log;
    }

    public Post Post { get; private set; } = null!;
    public List<Comment> Comments { get; private set; } = new();

    /// <summary>متنِ نوشته، تبدیل‌شده به HTML امن.</summary>
    public string BodyHtml { get; private set; } = "";

    /* ---- فرم نظر ---- */
    [BindProperty] public string CommentName { get; set; } = "";
    [BindProperty] public string CommentBody { get; set; } = "";
    [BindProperty(Name = FormGuard.TokenField)] public string CaptchaToken { get; set; } = "";
    [BindProperty(Name = FormGuard.AnswerField)] public string CaptchaAnswer { get; set; } = "";
    [BindProperty(Name = FormGuard.HoneypotField)] public string Honeypot { get; set; } = "";
    [BindProperty(Name = FormGuard.StartedField)] public string StartedStamp { get; set; } = "";

    public FormGuard.Challenge Captcha { get; private set; } = null!;
    public string FormStamp { get; private set; } = "";
    public string? FormError { get; private set; }
    public Dictionary<string, string> FieldErrors { get; } = new();

    /// <summary>نظر تازه ثبت شده و منتظر تأیید است.</summary>
    public bool JustSubmitted { get; private set; }

    private const string SentKey = "comment.sent";

    /// <summary>
    /// داده‌ی ساخت‌یافته‌ی نوشته (BlogPosting) به‌همراه مسیر راهنما.
    ///
    /// چرا مهم است: بدون این، گوگل تاریخِ انتشار و نویسنده را از روی متن
    /// حدس می‌زند و اغلب اشتباه حدس می‌زند. ناشر همان Organization صفحه‌ی
    /// اصلی است — با `@@id` به آن وصل می‌شود تا دو هویتِ جدا ساخته نشود.
    /// </summary>
    public string ArticleJsonLd(string baseUrl, string postUrl)
    {
        var siteName = C["site"]["name"].S;
        var description = Post.Excerpt.Length > 0
            ? Post.Excerpt
            : MiniMarkdown.ToPlainText(Post.Body, 160);

        var article = new System.Text.Json.Nodes.JsonObject
        {
            ["@type"] = "BlogPosting",
            ["headline"] = Post.Title,
            ["description"] = description,
            ["datePublished"] = Post.PublishedAt,
            ["dateModified"] = Post.UpdatedAt.Length > 0 ? Post.UpdatedAt : Post.PublishedAt,
            ["inLanguage"] = "fa-IR",
            ["mainEntityOfPage"] = new System.Text.Json.Nodes.JsonObject
            {
                ["@type"] = "WebPage",
                ["@id"] = postUrl,
            },
            ["author"] = new System.Text.Json.Nodes.JsonObject
            {
                ["@type"] = "Organization",
                ["name"] = siteName,
                ["@id"] = $"{baseUrl}/#organization",
            },
            ["publisher"] = new System.Text.Json.Nodes.JsonObject
            {
                ["@id"] = $"{baseUrl}/#organization",
            },
        };

        if (Post.Tags.Count > 0)
            article["keywords"] = string.Join(", ", Post.Tags);

        var breadcrumb = new System.Text.Json.Nodes.JsonObject
        {
            ["@type"] = "BreadcrumbList",
            ["itemListElement"] = new System.Text.Json.Nodes.JsonArray
            {
                new System.Text.Json.Nodes.JsonObject
                {
                    ["@type"] = "ListItem",
                    ["position"] = 1,
                    ["name"] = C["copy"]["blog"]["heading"].S,
                    ["item"] = $"{baseUrl}/blog",
                },
                new System.Text.Json.Nodes.JsonObject
                {
                    ["@type"] = "ListItem",
                    ["position"] = 2,
                    ["name"] = Post.Title,
                    ["item"] = postUrl,
                },
            },
        };

        var graph = new System.Text.Json.Nodes.JsonObject
        {
            ["@context"] = "https://schema.org",
            ["@graph"] = new System.Text.Json.Nodes.JsonArray { article, breadcrumb },
        };

        var json = graph.ToJsonString(new System.Text.Json.JsonSerializerOptions
        {
            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        });

        // `</script>` داخل عنوان یا متن می‌توانست تگ را زودتر ببندد. همان
        // محافظتی که JsonForScript برای داده‌ی دمو انجام می‌دهد.
        return JsonForScript.Escape(json);
    }

    private void FreshCaptcha()
    {
        Captcha = _guard.NewChallenge();
        FormStamp = _guard.StampNow();
        CaptchaAnswer = "";
    }

    private bool Load(string slug)
    {
        var post = _blog.BySlug(slug);
        if (post is null) return false;
        Post = post;
        BodyHtml = MiniMarkdown.ToHtml(post.Body);
        Comments = _blog.ApprovedFor(post.Id);
        return true;
    }

    public IActionResult OnGet(string slug)
    {
        if (!Load(slug)) return NotFound();
        FreshCaptcha();
        if (TempData[SentKey] is true) JustSubmitted = true;
        return Page();
    }

    public async Task<IActionResult> OnPostAsync(string slug)
    {
        if (!Load(slug)) return NotFound();

        var errors = C["copy"]["blog"]["errors"];
        var ip = ClientIp.Of(HttpContext, _config);

        // نوشته‌ای که نظرهایش بسته است نباید از راهِ POST هم نظر بگیرد —
        // پنهان کردنِ فرم در ویو به‌تنهایی یک محافظت نیست.
        if (!Post.CommentsOpen)
        {
            FreshCaptcha();
            FormError = C["copy"]["blog"]["commentsClosed"].S;
            return Page();
        }

        // ترتیب مثل فرم مشاوره: اول لایه‌های ارزانِ ضدّربات، بعد فیلدها، آخر نوشتن.
        if (FormGuard.HoneypotTripped(Honeypot) || _guard.FilledTooFast(StartedStamp))
        {
            _log.LogInformation("[blog] نظرِ مشکوک رد شد (تله/زمان) از {Ip}", ip);
            FormError = errors["spam"].S;
            FreshCaptcha();
            return Page();
        }

        if (!_guard.WithinSendLimit(ip, FormGuard.CommentBucket, FormGuard.MaxCommentsPerHour))
        {
            _log.LogInformation("[blog] سقفِ نظر پر شد برای {Ip}", ip);
            FormError = errors["tooMany"].S;
            FreshCaptcha();
            return Page();
        }

        var captcha = _guard.CheckCaptcha(CaptchaToken, CaptchaAnswer);
        if (captcha != FormGuard.CaptchaResult.Ok)
        {
            FieldErrors["captcha"] = captcha == FormGuard.CaptchaResult.Expired
                ? errors["captchaExpired"].S
                : errors["captcha"].S;
        }

        var name = (CommentName ?? "").Trim();
        var body = (CommentBody ?? "").Trim();
        if (name.Length < 2 || name.Length > MaxNameLength) FieldErrors["name"] = errors["name"].S;
        if (body.Length < 5) FieldErrors["body"] = errors["body"].S;
        else if (body.Length > MaxCommentLength) FieldErrors["body"] = errors["bodyLong"].S;

        if (FieldErrors.Count > 0)
        {
            FreshCaptcha();
            return Page();
        }

        try
        {
            await _blog.AddCommentAsync(Post.Id, name, body);
            _guard.CountSend(ip, FormGuard.CommentBucket);

            // POST → Redirect → GET، مثل فرم مشاوره: رفرشِ صفحه نباید نظر را
            // دوباره بفرستد (و با توکنِ یک‌بارمصرفِ کپچا، خطای گیج‌کننده بدهد).
            TempData[SentKey] = true;
            return Redirect($"/blog/{Uri.EscapeDataString(Post.Slug)}#comments");
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "[blog] نظر ثبت نشد");
            FormError = errors["failed"].S;
            FreshCaptcha();
            return Page();
        }
    }
}
