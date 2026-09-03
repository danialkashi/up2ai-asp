using System.Security;
using System.Text;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Up2Ai.Services;

namespace Up2Ai.Pages.Blog;

/// <summary>
/// فید RSS وبلاگ.
///
/// چرا ارزشش را دارد: چند خط کد است و دو فایده‌ی مشخص دارد — خواننده‌های
/// حرفه‌ای می‌توانند نوشته‌ها را دنبال کنند، و ابزارهای جمع‌آوریِ محتوا
/// (از جمله بعضی خزنده‌ها) نوشته‌ی تازه را زودتر می‌بینند.
///
/// XML دستی ساخته می‌شود و نه با کتابخانه: پروژه وابستگی NuGet ندارد و
/// این ساختار آن‌قدر ساده است که ارزشِ یکی را نداشته باشد. هر متنی که از
/// محتوا می‌آید با <see cref="SecurityElement.Escape"/> اسکیپ می‌شود —
/// یک `&amp;` ساده در عنوان کافی است تا کلِ فید نامعتبر شود.
/// </summary>
public class BlogFeedModel : PageModel
{
    /// <summary>بیست نوشته‌ی آخر — بیشتر از این، فیدی می‌شود که کسی تا تهش نمی‌رود.</summary>
    private const int MaxItems = 20;

    private readonly ContentStore _store;
    private readonly BlogStore _blog;

    public BlogFeedModel(ContentStore store, BlogStore blog)
    {
        _store = store;
        _blog = blog;
    }

    public IActionResult OnGet()
    {
        var c = new Cv(_store.Get());
        var site = c["site"];
        var b = c["copy"]["blog"];
        var baseUrl = site["url"].S.TrimEnd('/');
        var posts = _blog.Published().Take(MaxItems).ToList();

        var xml = new StringBuilder();
        xml.Append("<?xml version=\"1.0\" encoding=\"UTF-8\"?>\n");
        xml.Append("<rss version=\"2.0\" xmlns:atom=\"http://www.w3.org/2005/Atom\">\n<channel>\n");
        xml.Append($"<title>{Esc($"{b["heading"].S} — {site["name"].S}")}</title>\n");
        xml.Append($"<link>{Esc($"{baseUrl}/blog")}</link>\n");
        xml.Append($"<description>{Esc(b["lead"].S)}</description>\n");
        xml.Append("<language>fa-IR</language>\n");
        xml.Append($"<atom:link href=\"{Esc($"{baseUrl}/blog/feed.xml")}\" rel=\"self\" type=\"application/rss+xml\" />\n");

        foreach (var post in posts)
        {
            var link = $"{baseUrl}/blog/{Uri.EscapeDataString(post.Slug)}";
            var description = post.Excerpt.Length > 0
                ? post.Excerpt
                : MiniMarkdown.ToPlainText(post.Body, 300);
            xml.Append("<item>\n");
            xml.Append($"<title>{Esc(post.Title)}</title>\n");
            xml.Append($"<link>{Esc(link)}</link>\n");
            // guid دائمی است و از شناسه‌ی نوشته می‌آید نه از آدرس: اگر روزی
            // اسلاگ عوض شود، خواننده‌ها همان نوشته را دوباره «تازه» نمی‌بینند.
            xml.Append($"<guid isPermaLink=\"false\">{Esc(post.Id)}</guid>\n");
            xml.Append($"<description>{Esc(description)}</description>\n");
            var published = PersianYear.ParseIso(post.PublishedAt);
            if (published is not null)
                xml.Append($"<pubDate>{published.Value.ToString("r", System.Globalization.CultureInfo.InvariantCulture)}</pubDate>\n");
            xml.Append("</item>\n");
        }

        xml.Append("</channel>\n</rss>\n");
        return Content(xml.ToString(), "application/rss+xml; charset=utf-8");
    }

    private static string Esc(string s) => SecurityElement.Escape(s) ?? "";
}
