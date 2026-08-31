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
    public SitemapModel(ContentStore store) => _store = store;

    public IActionResult OnGet()
    {
        var url = new Cv(_store.Get())["site"]["url"].S.TrimEnd('/');
        var xml =
            "<?xml version=\"1.0\" encoding=\"UTF-8\"?>\n" +
            "<urlset xmlns=\"http://www.sitemaps.org/schemas/sitemap/0.9\">\n" +
            "<url>\n" +
            $"<loc>{url}/</loc>\n" +
            "<changefreq>monthly</changefreq>\n" +
            "<priority>1</priority>\n" +
            "</url>\n" +
            "</urlset>\n";
        return Content(xml, "application/xml; charset=utf-8");
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
