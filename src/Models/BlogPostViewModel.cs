using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using Up2Ai.Services;

namespace Up2Ai.Models;

public class BlogPostViewModel
{
    public Cv C { get; set; } = new Cv(null);
    public Post Post { get; set; } = null!;
    public List<Comment> Comments { get; set; } = new();
    public string BodyHtml { get; set; } = "";

    public string CommentName { get; set; } = "";
    public string CommentBody { get; set; } = "";
    public string CaptchaToken { get; set; } = "";
    public string CaptchaAnswer { get; set; } = "";
    public string Honeypot { get; set; } = "";
    public string StartedStamp { get; set; } = "";

    public FormGuard.Challenge Captcha { get; set; } = null!;
    public string FormStamp { get; set; } = "";
    public string? FormError { get; set; }
    public Dictionary<string, string> FieldErrors { get; } = new();
    public bool JustSubmitted { get; set; }

    /// <summary>نوشته‌های مرتبط برای بلوکِ پایانِ صفحه — کنترلر پرش می‌کند.</summary>
    public List<Post> Related { get; set; } = new();

    /// <summary>
    /// داده‌ی ساخت‌یافته‌ی خودِ نوشته (schema.org/BlogPosting).
    ///
    /// تا حالا همیشه رشته‌ی خالی برمی‌گشت — تگِ ld+json روی صفحه بود ولی خالی؛
    /// یعنی گوگل نه تاریخ می‌دید، نه نویسنده، نه تصویر. حالا از همان
    /// <c>System.Text.Json.Nodes</c> ساخته می‌شود که پارشالِ _JsonLd استفاده
    /// می‌کند، با همان دو تله‌ی ثبت‌شده‌ی آن‌جا: گره‌ها فقط با
    /// <c>JsonValue.Create</c> (نه Add رشته‌ای) و سریال‌سازی با
    /// TypeInfoResolver، وگرنه موقع رندر استثنا می‌دهد.
    ///
    /// نکته‌ی جای‌گذاری: لایوتِ عمومی <c>_JsonLd</c> را فقط وقتی رندر می‌کند
    /// که صفحه noindex نباشد؛ این متد فقط از <c>Post.cshtml</c> صدا زده
    /// می‌شود و خروجی‌اش با مقاله‌ی Organization/WebSite در <c>@graph</c>
    /// همپوشانی ندارد — در <c>@graph</c> هویتِ سایت است، این‌جا خودِ نوشته.
    /// </summary>
    public string ArticleJsonLd(string baseUrl, string postUrl)
    {
        var site = C["site"];

        // image: بدون آن، Rich Results هشدار «missing field image» می‌دهد.
        var image = $"{baseUrl}/opengraph-image.png";

        // تله‌ی شناخته‌شده‌ی JsonNode: یک نمونه نمی‌تواند دو جا پدر داشته
        // باشد (author و publisher) — دفعه‌ی دوم هنگام انتساب استثنا می‌دهد.
        // هر بار گره‌ی تازه می‌سازیم.
        JsonObject OrgRef() => new()
        {
            ["@type"] = "Organization",
            ["@id"] = $"{baseUrl}/#organization",
        };

        var node = new JsonObject
        {
            ["@type"] = "BlogPosting",
            ["@id"] = $"{postUrl}#article",
            ["headline"] = Post.Title,
            ["description"] = Post.Excerpt.Length > 0
                ? Post.Excerpt
                : MiniMarkdown.BodyToPlainText(Post.Body, 160),
            ["image"] = JsonValue.Create(image),
            ["url"] = postUrl,
            ["inLanguage"] = "fa-IR",
            ["datePublished"] = Post.PublishedAt,
            // UpdatedAt خالی یعنی پست بعد از انتشار ویرایش نشده؛ تاریخِ
            // تکراریِ همان datePublished می‌گذاریم که Google ابجکت را
            // ناقص نبیند.
            ["dateModified"] = Post.UpdatedAt.Length > 0 ? Post.UpdatedAt : Post.PublishedAt,
            ["author"] = OrgRef(),
            ["publisher"] = OrgRef(),
            ["mainEntityOfPage"] = new JsonObject
            {
                ["@type"] = "WebPage",
                ["@id"] = postUrl,
            },
        };

        // برچسب‌ها وقتی پرند keyword می‌شوند.
        if (Post.Tags.Count > 0)
        {
            var keywords = new JsonArray();
            foreach (var t in Post.Tags) keywords.Add(JsonValue.Create(t));
            node["keywords"] = keywords;
        }

        // عددِ ساده‌ی نظرات — هر نظر یک Comment واقعی است ولی خروجیِ صفحه
        // فقط نظرات تأییدشده را نشان می‌دهد؛ شمارشِ داده‌ی ساخت‌یافته هم
        // باید همان را بشمارد.
        if (Comments.Count > 0)
        {
            node["commentCount"] = JsonValue.Create(Comments.Count);
        }

        return node.ToJsonString(new JsonSerializerOptions
        {
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
            TypeInfoResolver = new DefaultJsonTypeInfoResolver(),
        });
    }
}
