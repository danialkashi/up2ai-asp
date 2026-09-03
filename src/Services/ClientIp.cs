namespace Up2Ai.Services;

/// <summary>
/// آدرس IP بازدیدکننده — یک تعریف برای کل برنامه.
///
/// چرا اصلاً مسئله است: هر جایی که چیزی را «به ازای هر کاربر» می‌شماریم
/// (سدِّ حدسِ رمز پنل، سقفِ ارسال فرم) به این آدرس تکیه می‌کند. اگر اشتباه
/// حساب شود، یکی از این دو اتفاق می‌افتد و هر دو بد است:
///
///   • پشتِ پراکسی (nginx، کلادفلر، لیارا، آروان…) بدون خواندن هدر، آدرسِ
///     *همه‌ی* بازدیدکننده‌ها یکی می‌شود: آدرسِ خودِ پراکسی. آن‌وقت پنج بار
///     رمز غلط از یک نفر، پنلِ مدیر را برای همه قفل می‌کند.
///
///   • بدون پراکسی، اگر هدر را کورکورانه باور کنیم، هر کسی می‌تواند
///     `X-Forwarded-For` جعلی بفرستد و هر شمارنده‌ای را دور بزند — کافی است
///     هر بار یک آدرسِ ساختگی بگذارد. (سقفِ ۵ ارسال در ساعت عملاً بی‌اثر
///     می‌شد و جدولِ شمارش هم با هر آدرسِ تازه بی‌نهایت بزرگ می‌شد.)
///
/// پس هدر فقط وقتی خوانده می‌شود که صاحبِ سایت *صریحاً* گفته باشد پشت
/// پراکسی است:
///
///     TRUST_PROXY_HEADERS=true
///
/// و در غیر این‌صورت همان آدرسِ اتصال ملاک است. پیش‌فرض امن است، و هر دو
/// شمارنده‌ی سایت از همین یک تابع می‌خوانند تا هیچ‌وقت دو تعریف نداشته باشیم.
/// </summary>
public static class ClientIp
{
    public static bool TrustProxyHeaders(IConfiguration config) =>
        string.Equals(config["TRUST_PROXY_HEADERS"]?.Trim(), "true", StringComparison.OrdinalIgnoreCase);

    public static string Of(HttpContext http, IConfiguration config)
    {
        if (TrustProxyHeaders(config))
        {
            // اولین مقدار در X-Forwarded-For نزدیک‌ترین آدرس به خودِ کاربر است.
            var fwd = http.Request.Headers["X-Forwarded-For"].ToString();
            if (!string.IsNullOrWhiteSpace(fwd))
            {
                var first = fwd.Split(',')[0].Trim();
                if (first.Length > 0) return Trim(first);
            }

            var real = http.Request.Headers["X-Real-IP"].ToString().Trim();
            if (real.Length > 0) return Trim(real);
        }

        return http.Connection.RemoteIpAddress?.ToString() ?? "unknown";
    }

    /// <summary>سقفِ طول، تا یک هدرِ خیلی بلند نتواند کلیدهای غول‌پیکر بسازد.</summary>
    private static string Trim(string ip) => ip.Length <= 64 ? ip : ip[..64];
}
