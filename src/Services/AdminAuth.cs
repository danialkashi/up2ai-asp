using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;

namespace Up2Ai.Services;

/// <summary>
/// Admin authentication and password hashing.
///
/// Uses PBKDF2-SHA256 with 210,000 iterations (OWASP recommended).
/// All password hashes are stored in the database, keyed per user.
/// </summary>
public sealed class AdminAuth
{
    public const string CookieName = "up2ai_admin";
    public const string CookiePath = "/admin";
    private const int MaxAgeSeconds = 60 * 60 * 12; // ۱۲ ساعت
    private const int KeyLength = 32;
    private const int Iterations = 210_000; // توصیه‌ی OWASP برای PBKDF2-SHA256

    private readonly ILogger<AdminAuth> _log;

    public AdminAuth(ILogger<AdminAuth> log) => _log = log;

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

    /* --------------------------------- Session --------------------------------- */

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
