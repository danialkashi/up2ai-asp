namespace Up2Ai.Services;

/// <summary>
/// درست کردنِ لینک‌های منو برای صفحه‌هایی غیر از صفحه‌ی اصلی.
///
/// منوی سایت از محتوا می‌آید و بیشترِ آیتم‌هایش لنگرِ داخلِ صفحه‌ی اصلی‌اند
/// («#services»، «#contact»). تا وقتی سایت تک‌صفحه بود این کافی بود؛ با
/// اضافه شدنِ وبلاگ دیگر نیست: کلیکِ «خدمات» در صفحه‌ی یک نوشته، مرورگر را
/// دنبالِ لنگری در همان صفحه می‌گرداند که وجود ندارد، و عملاً هیچ اتفاقی
/// نمی‌افتد.
///
/// پس هر لنگر، بیرون از صفحه‌ی اصلی، به «/#…» تبدیل می‌شود — یعنی اول برو
/// خانه، بعد همان‌جا اسکرول کن. لینک‌های معمولی (/blog، https://…) دست
/// نمی‌خورند.
/// </summary>
public static class SiteLinks
{
    public static bool IsHome(string? path) =>
        string.IsNullOrEmpty(path) || path == "/" || path.Equals("/index", StringComparison.OrdinalIgnoreCase);

    public static string Resolve(string href, string? currentPath) =>
        href.StartsWith('#') && !IsHome(currentPath) ? "/" + href : href;
}
