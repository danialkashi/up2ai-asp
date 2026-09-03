using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Up2Ai.Services;

namespace Up2Ai.Pages;

/// <summary>
/// `sitemap.xml` و `robots.txt`.
///
/// هر دو از محتوای زنده ساخته می‌شوند (نه یک فایل ثابت در wwwroot)، چون آدرس
/// دامنه از پنل مدیریت قابل تغییر است و این دو فایل باید همان لحظه با آن
/// هماهنگ شوند — همان کاری که `app/sitemap.ts` و `app/robots.ts` می‌کردند.
/// </summary>
public class SitemapModel : PageModel
{
    private readonly ContentStore _store;
    private readonly BlogStore _blog;

    public SitemapModel(ContentStore store, BlogStore blog)
    {
        _store = store;
        _blog = blog;
    }

    public IActionResult OnGet()
    {
        // آدرس از پنل مدیریت می‌آید، پس قبل از رفتن داخل XML اسکیپ می‌شود.
        // یک & ساده در آدرس کافی بود تا sitemap.xml نامعتبر شود و گوگل کل
        // فایل را دور بیندازد.
        var url = System.Security.SecurityElement.Escape(
            new Cv(_store.Get())["site"]["url"].S.TrimEnd('/')) ?? "";

        var xml = new System.Text.StringBuilder();
        xml.Append("<?xml version=\"1.0\" encoding=\"UTF-8\"?>\n");
        xml.Append("<urlset xmlns=\"http://www.sitemaps.org/schemas/sitemap/0.9\">\n");
        xml.Append($"<url>\n<loc>{url}/</loc>\n<changefreq>monthly</changefreq>\n<priority>1</priority>\n</url>\n");

        // فهرست وبلاگ و بعد خودِ نوشته‌ها.
        //
        // فقط نوشته‌های *منتشرشده* می‌آیند: آدرسِ یک پیش‌نویس در sitemap یعنی
        // گوگل سراغِ صفحه‌ای می‌رود که ۴۰۴ می‌دهد، و این به اعتبارِ خزشِ کل
        // سایت آسیب می‌زند.
        var posts = _blog.Published();
        xml.Append($"<url>\n<loc>{url}/blog</loc>\n<changefreq>weekly</changefreq>\n<priority>0.8</priority>\n</url>\n");

        foreach (var post in posts)
        {
            var loc = System.Security.SecurityElement.Escape($"{url}/blog/{Uri.EscapeDataString(post.Slug)}") ?? "";
            // lastmod به گوگل می‌گوید کدام نوشته واقعاً عوض شده و ارزشِ
            // خزشِ دوباره دارد.
            var stamp = PersianYear.ParseIso(post.UpdatedAt.Length > 0 ? post.UpdatedAt : post.PublishedAt);
            xml.Append($"<url>\n<loc>{loc}</loc>\n");
            if (stamp is not null)
                xml.Append($"<lastmod>{stamp.Value:yyyy-MM-dd}</lastmod>\n");
            xml.Append("<changefreq>monthly</changefreq>\n<priority>0.7</priority>\n</url>\n");
        }

        xml.Append("</urlset>\n");
        return Content(xml.ToString(), "application/xml; charset=utf-8");
    }
}

public class RobotsModel : PageModel
{
    private readonly ContentStore _store;
    public RobotsModel(ContentStore store) => _store = store;

    public IActionResult OnGet()
    {
        var url = new Cv(_store.Get())["site"]["url"].S.TrimEnd('/');
        // مسیر /admin عمداً مسدود است — یکی برای موتورهای مؤدب، و کنارش
        // متای noindex در خود صفحه‌های پنل برای بقیه.
        var txt =
            "User-Agent: *\n" +
            "Allow: /\n" +
            "Disallow: /admin\n" +
            "\n" +
            $"Sitemap: {url}/sitemap.xml\n";
        return Content(txt, "text/plain; charset=utf-8");
    }
}
