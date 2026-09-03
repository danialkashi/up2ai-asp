using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;

namespace Up2Ai.Services;

/// <summary>
/// احراز هویت پنل مدیریت.
///
/// ┌──────────────────────────────────────────────────────────────────────┐
/// │ رمز عبور هیچ‌جا در این مخزن نیست و نباید باشد.                       │
/// │                                                                      │
/// │ فقط *هش* رمز در متغیر محیطی `ADMIN_PASSWORD_HASH` می‌نشیند، و آن هش  │
/// │ را خودت با این دستور از روی رمز خودت می‌سازی:                        │
/// │                                                                      │
/// │     dotnet run -- hash-password                                      │
/// │                                                                      │
/// │ اگر این متغیر (یا `ADMIN_SESSION_SECRET`) تنظیم نشده باشد، پنل اصلاً  │
/// │ فرم ورود نشان نمی‌دهد — یعنی هیچ‌وقت رمز پیش‌فرضی وجود ندارد که کسی   │
/// │ حدسش بزند.                                                           │
/// └──────────────────────────────────────────────────────────────────────┘
///
/// تفاوت با نسخه‌ی Node: آن‌جا هش با scrypt ساخته می‌شد، که در .NET به‌صورت
/// داخلی وجود ندارد. این‌جا PBKDF2-SHA256 استفاده شده که در خود فریم‌ورک هست
/// (بدون هیچ پکیج بیرونی). یعنی هشِ قبلی دیگر کار نمی‌کند و باید یک بار رمز
/// را با دستور بالا دوباره بسازی — این تنها تغییر عملیِ این مهاجرت است.
/// </summary>
public sealed class AdminAuth
{
    public const string CookieName = "up2ai_admin";
    public const string CookiePath = "/admin";
    private const int MaxAgeSeconds = 60 * 60 * 12; // ۱۲ ساعت
    private const int KeyLength = 32;
    private const int Iterations = 210_000; // توصیه‌ی OWASP برای PBKDF2-SHA256

    private readonly IConfiguration _config;

    public AdminAuth(IConfiguration config) => _config = config;

    public string? Hash => _config["ADMIN_PASSWORD_HASH"]?.Trim();
    public string? Secret => _config["ADMIN_SESSION_SECRET"]?.Trim();

    /// <summary>متغیرهایی که تنظیم نشده‌اند — اگر خالی نبود، پنل حتی فرم ورود هم نشان نمی‌دهد.</summary>
    public List<string> MissingConfig()
    {
        var missing = new List<string>();
        if (string.IsNullOrEmpty(Hash)) missing.Add("ADMIN_PASSWORD_HASH");
        // راز کوتاه به‌اندازه‌ی نبودنش بد است، پس همان‌جا رد می‌شود.
        if (string.IsNullOrEmpty(Secret) || Secret!.Length < 32) missing.Add("ADMIN_SESSION_SECRET");
        return missing;
    }

    public bool IsConfigured => MissingConfig().Count == 0;

    /* ---------------------------------- رمز ---------------------------------- */

    /// <summary>فرمت: `pbkdf2:&lt;iterations&gt;:&lt;salt hex&gt;:&lt;key hex&gt;`</summary>
    public static string HashPassword(string password)
    {
        var salt = RandomNumberGenerator.GetBytes(16);
        var key = Rfc2898DeriveBytes.Pbkdf2(
            Encoding.UTF8.GetBytes(password), salt, Iterations, HashAlgorithmName.SHA256, KeyLength);
        return $"pbkdf2:{Iterations}:{Convert.ToHexString(salt).ToLowerInvariant()}:{Convert.ToHexString(key).ToLowerInvariant()}";
    }

    public static bool VerifyPassword(string password, string stored)
    {
        var parts = stored.Split(':');
        if (parts.Length != 4 || parts[0] != "pbkdf2") return false;
        if (!int.TryParse(parts[1], out var iterations) || iterations < 1000) return false;

        byte[] salt, expected;
        try
        {
            salt = Convert.FromHexString(parts[2]);
            expected = Convert.FromHexString(parts[3]);
        }
        catch (FormatException) { return false; }

        if (expected.Length != KeyLength) return false;
        var actual = Rfc2898DeriveBytes.Pbkdf2(
            Encoding.UTF8.GetBytes(password), salt, iterations, HashAlgorithmName.SHA256, KeyLength);
        // مقایسه‌ی زمان‌ثابت: با مقایسه‌ی معمولی، طول تطابق از روی زمان پاسخ لو می‌رود.
        return CryptographicOperations.FixedTimeEquals(actual, expected);
    }

    /* --------------------------------- نشست --------------------------------- */

    // base64url بدون padding — معادل `Buffer.toString("base64url")` در Node،
    // که فرمت توکن نسخه‌ی قبلی بود. (.NET 8 هنوز Base64Url داخلی ندارد.)
    private static string B64(byte[] b) =>
        Convert.ToBase64String(b).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static byte[] FromB64(ReadOnlySpan<char> s)
    {
        var t = new string(s).Replace('-', '+').Replace('_', '/');
        return Convert.FromBase64String(t.PadRight(t.Length + (4 - t.Length % 4) % 4, '='));
    }

    private static string Sign(string payload, string secret)
    {
        using var mac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
        return B64(mac.ComputeHash(Encoding.UTF8.GetBytes(payload)));
    }

    /// <summary>
    /// توکنِ نشست برای یک کاربرِ مشخص.
    ///
    /// شناسه‌ی کاربر داخلِ خودِ توکن (و زیر همان امضا) می‌نشیند تا هر صفحه‌ی
    /// پنل بداند چه کسی وارد شده — بدون این، با چند کاربر هیچ راهی نبود
    /// بفهمیم نویسنده‌ی یک تغییر کیست، و «تغییر رمزِ من» هم معنایی نداشت.
    /// </summary>
    public string MakeToken(string userId)
    {
        var exp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() + MaxAgeSeconds * 1000L;
        var payload = $"{exp}|{userId}";
        return $"{B64(Encoding.UTF8.GetBytes(payload))}.{Sign(payload, Secret!)}";
    }

    /// <summary>
    /// شناسه‌ی کاربرِ داخل توکن، یا <c>null</c> اگر توکن معتبر/تازه نباشد.
    ///
    /// توکن‌های نسخه‌ی قبلی فقط تاریخِ انقضا داشتند. آن‌ها هم پذیرفته می‌شوند
    /// و به کاربرِ محیطی نسبت داده می‌شوند — وگرنه با انتشارِ این نسخه هر کسی
    /// که وارد بود بی‌دلیل بیرون می‌افتاد.
    /// </summary>
    public string? ReadToken(string? token)
    {
        if (!IsConfigured || string.IsNullOrEmpty(token)) return null;
        var dot = token.IndexOf('.');
        if (dot <= 0 || dot == token.Length - 1) return null;

        string payload;
        try
        {
            payload = Encoding.UTF8.GetString(FromB64(token.AsSpan(0, dot)));
        }
        catch (FormatException) { return null; }

        var want = Encoding.UTF8.GetBytes(Sign(payload, Secret!));
        var got = Encoding.UTF8.GetBytes(token[(dot + 1)..]);
        if (want.Length != got.Length || !CryptographicOperations.FixedTimeEquals(want, got)) return null;

        var bar = payload.IndexOf('|');
        var expPart = bar < 0 ? payload : payload[..bar];
        var userId = bar < 0 ? AdminUserStore.EnvUserId : payload[(bar + 1)..];

        if (!long.TryParse(expPart, out var ms) || ms <= DateTimeOffset.UtcNow.ToUnixTimeMilliseconds())
            return null;
        return userId.Length > 0 ? userId : AdminUserStore.EnvUserId;
    }

    public CookieOptions CookieOptions(bool isProduction) => new()
    {
        HttpOnly = true,
        SameSite = SameSiteMode.Lax,
        Secure = isProduction,
        Path = CookiePath,
        MaxAge = TimeSpan.FromSeconds(MaxAgeSeconds),
    };

    /* --------------------------- محدودیت تلاش ورود --------------------------- */

    /// <summary>
    /// سدّ ساده‌ی حدس رمز. در حافظه‌ی همین فرآیند است، پس با ری‌استارت پاک
    /// می‌شود — برای پنلی که یک نفر ازش استفاده می‌کند کافی است و هیچ وابستگی
    /// اضافه‌ای نمی‌خواهد.
    /// </summary>
    private static readonly ConcurrentDictionary<string, (int Count, long Until)> Attempts = new();
    private const int MaxTries = 5;
    private const long LockMs = 10 * 60 * 1000;

    public static (bool Allowed, int RetryInSec) ThrottleCheck(string key)
    {
        if (Attempts.TryGetValue(key, out var rec))
        {
            var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            if (rec.Until > now)
                return (false, (int)Math.Ceiling((rec.Until - now) / 1000.0));
        }
        return (true, 0);
    }

    public static void ThrottleFail(string key)
    {
        Attempts.AddOrUpdate(key,
            _ => (1, 0L),
            (_, rec) =>
            {
                var count = rec.Count + 1;
                return count >= MaxTries
                    ? (0, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() + LockMs)
                    : (count, rec.Until);
            });

        // پاک‌سازیِ تنبل. بدون این، هر آدرسی که یک بار رمز غلط زده تا پایان
        // عمرِ فرآیند یک ردیف در حافظه نگه می‌داشت — یعنی یک password-spray
        // از هزاران آدرس، حافظه را بی‌سقف بالا می‌برد. ردیف‌هایی که نه قفل‌اند
        // و نه تازه، دیگر ارزشی ندارند.
        if (Attempts.Count <= 1000) return;
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        foreach (var pair in Attempts)
            if (pair.Value.Until <= now - LockMs) Attempts.TryRemove(pair.Key, out _);
    }

    public static void ThrottleReset(string key) => Attempts.TryRemove(key, out _);
}
