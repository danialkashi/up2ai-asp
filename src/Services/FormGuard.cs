using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;

namespace Up2Ai.Services;

/// <summary>
/// محافظِ فرم‌های عمومی (فعلاً فقط فرم مشاوره): کپچا + محدودیت تعداد ارسال.
///
/// چرا خودمان و نه reCAPTCHA / hCaptcha:
///   • هر دو یک درخواست به سرورِ گوگل/کلادفلر لازم دارند. مخاطبِ این سایت
///     داخل ایران است و آن درخواست ممکن است اصلاً برنگردد — یعنی فرم برای
///     کاربرِ واقعی از کار می‌افتد، دقیقاً برعکسِ هدف.
///   • پروژه عمداً هیچ وابستگی NuGet ندارد و باید روی سروری بدون اینترنتِ
///     آزاد هم بالا بیاید.
///
/// چند لایه روی هم، چون هیچ‌کدام به‌تنهایی کافی نیست:
///
///   ۱) کپچای حسابِ ساده که *به‌صورت تصویر SVG* رسم می‌شود، نه متن. یعنی
///      رباتی که فقط HTML را regex می‌کند عدد را پیدا نمی‌کند.
///   ۲) توکنِ امضاشده (HMAC): پاسخِ درست هیچ‌وقت به مرورگر فرستاده نمی‌شود.
///      خودِ توکن حاملِ پاسخ است ولی امضا شده، پس دست‌کاری‌اش معلوم می‌شود.
///      عمرش پنج دقیقه است و هر توکن فقط *یک بار* پذیرفته می‌شود (وگرنه یک
///      بار حل کردن کافی بود و بقیه‌ی ارسال‌ها همان توکن را تکرار می‌کردند).
///   ۳) فیلدِ تله (honeypot): ورودیِ نامرئی‌ای که کاربر نمی‌بیند و پرش
///      نمی‌کند؛ رباتِ فرم‌پرکن معمولاً همه‌چیز را پر می‌کند.
///   ۴) کفِ زمانِ پر کردن: انسان کمتر از سه ثانیه این فرم را پر نمی‌کند.
///   ۵) سقفِ تعداد ارسالِ موفق از یک IP در ساعت.
///
/// صادق باشیم درباره‌ی حدِ این کار: یک مهاجمِ هدفمند که برای همین سایت
/// اسکریپت بنویسد می‌تواند SVG را OCR کند یا از روی aria-label جواب را
/// دربیاورد. هدفِ این لایه‌ها ربات‌های عمومیِ اسپم است، که عملاً همه‌ی
/// اسپمِ چنین فرمی از آن‌هاست. اگر روزی حمله‌ی هدفمند دیدی، قدم بعدی
/// کپچای تصویریِ سخت‌تر یا سرویس بیرونی است.
///
/// همه‌ی حالت‌ها در حافظه‌ی همین فرآیند است — مثل سدِّ ورودِ پنل مدیریت
/// (<see cref="AdminAuth"/>). برای سایتی که روی یک سرور اجرا می‌شود کافی
/// است؛ اگر روزی چند نمونه‌ای شد، این بخش باید به یک انبارِ مشترک برود.
/// </summary>
public sealed class FormGuard
{
    /// <summary>نام فیلدهای فرم — یک‌جا، تا ویو و سرور از هم نیفتند.</summary>
    public const string TokenField = "cap_t";
    public const string AnswerField = "cap_a";
    public const string HoneypotField = "website"; // اسمی که ربات وسوسه شود پرش کند
    public const string StartedField = "cap_s";

    private const int TokenLifetimeSeconds = 5 * 60;
    private const int MinFillSeconds = 3;
    private const int MaxPerHour = 5;

    private readonly byte[] _key;

    // توکن‌های مصرف‌شده، تا یک کپچای حل‌شده دو بار جواب ندهد.
    private readonly ConcurrentDictionary<string, long> _used = new();

    // شمارشِ ارسالِ موفق per-IP، به‌صورت پنجره‌ی ساده‌ی یک‌ساعته.
    private readonly ConcurrentDictionary<string, (int Count, long WindowStart)> _sends = new();

    public FormGuard(IConfiguration config)
    {
        // ترتیب: کلید اختصاصی، بعد رازِ نشستِ پنل، و در نهایت یک کلید تصادفیِ
        // همین اجرا. حالت سوم یعنی توکن‌ها با ری‌استارت باطل می‌شوند — که چون
        // عمرشان پنج دقیقه است اثر عملی‌اش ناچیز است و سایت را هم مجبور
        // نمی‌کند برای یک فرم، تنظیماتِ اجباری داشته باشد.
        var configured = config["FORM_CAPTCHA_SECRET"] ?? config["ADMIN_SESSION_SECRET"];
        _key = string.IsNullOrWhiteSpace(configured)
            ? RandomNumberGenerator.GetBytes(32)
            : Encoding.UTF8.GetBytes(configured);
    }

    /* ------------------------------ کپچا ------------------------------ */

    public sealed record Challenge(string Token, int A, int B, string Question);

    /// <summary>یک پرسشِ تازه می‌سازد. پاسخ فقط داخلِ توکنِ امضاشده می‌رود.</summary>
    public Challenge NewChallenge()
    {
        // جمعِ دو عددِ یک‌رقمیِ بزرگ‌تر از یک: نتیجه همیشه دورقمی و بدون
        // ابهامِ «صفر»، و برای هر کاربری بدون ماشین‌حساب حل‌شدنی.
        var a = RandomNumberGenerator.GetInt32(2, 10);
        var b = RandomNumberGenerator.GetInt32(2, 10);
        var expires = DateTimeOffset.UtcNow.ToUnixTimeSeconds() + TokenLifetimeSeconds;
        var nonce = Convert.ToHexString(RandomNumberGenerator.GetBytes(6));
        var payload = $"{a + b}:{expires}:{nonce}";
        return new Challenge($"{B64(Encoding.UTF8.GetBytes(payload))}.{Sign(payload)}", a, b,
            $"{PersianYear.ToPersianDigits(a.ToString())} + {PersianYear.ToPersianDigits(b.ToString())}");
    }

    public enum CaptchaResult { Ok, Wrong, Expired, Malformed }

    /// <summary>پاسخِ کاربر را با توکن می‌سنجد و توکن را مصرف‌شده علامت می‌زند.</summary>
    public CaptchaResult CheckCaptcha(string? token, string? answer)
    {
        if (string.IsNullOrWhiteSpace(token) || string.IsNullOrWhiteSpace(answer))
            return CaptchaResult.Malformed;

        var dot = token.IndexOf('.');
        if (dot <= 0 || dot == token.Length - 1) return CaptchaResult.Malformed;

        string payload;
        try { payload = Encoding.UTF8.GetString(FromB64(token.AsSpan(0, dot))); }
        catch (FormatException) { return CaptchaResult.Malformed; }

        // امضا اول بررسی می‌شود: تا امضا درست نباشد، به محتوای payload
        // اصلاً اعتماد نمی‌کنیم.
        var want = Encoding.UTF8.GetBytes(Sign(payload));
        var got = Encoding.UTF8.GetBytes(token[(dot + 1)..]);
        if (want.Length != got.Length || !CryptographicOperations.FixedTimeEquals(want, got))
            return CaptchaResult.Malformed;

        var parts = payload.Split(':');
        if (parts.Length != 3
            || !int.TryParse(parts[0], out var expected)
            || !long.TryParse(parts[1], out var expires))
            return CaptchaResult.Malformed;

        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        if (expires <= now) return CaptchaResult.Expired;

        // یک‌بارمصرف: اگر قبلاً همین توکن پذیرفته شده، دوباره قبول نیست.
        SweepUsed(now);
        if (!_used.TryAdd(token, expires)) return CaptchaResult.Expired;

        // کاربر ممکن است رقم فارسی تایپ کند (یا صفحه‌کلیدش خودش فارسی بدهد).
        var normalized = NormalizeDigits(answer.Trim());
        return int.TryParse(normalized, out var given) && given == expected
            ? CaptchaResult.Ok
            : CaptchaResult.Wrong;
    }

    /* ------------------------- تله و کفِ زمان ------------------------- */

    /// <summary>فیلدِ تله باید خالی بماند؛ پرشدنش یعنی فرم را آدم پر نکرده.</summary>
    public static bool HoneypotTripped(string? value) => !string.IsNullOrWhiteSpace(value);

    /// <summary>لحظه‌ی باز شدن فرم، برای سنجشِ سرعتِ پر کردن.</summary>
    public string StampNow()
    {
        var t = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString();
        return $"{B64(Encoding.UTF8.GetBytes(t))}.{Sign(t)}";
    }

    /// <summary>آیا فرم غیرطبیعی سریع پر شده؟ مهر که نامعتبر باشد هم مشکوک است.</summary>
    public bool FilledTooFast(string? stamp)
    {
        if (string.IsNullOrWhiteSpace(stamp)) return true;
        var dot = stamp.IndexOf('.');
        if (dot <= 0 || dot == stamp.Length - 1) return true;

        string t;
        try { t = Encoding.UTF8.GetString(FromB64(stamp.AsSpan(0, dot))); }
        catch (FormatException) { return true; }

        var want = Encoding.UTF8.GetBytes(Sign(t));
        var got = Encoding.UTF8.GetBytes(stamp[(dot + 1)..]);
        if (want.Length != got.Length || !CryptographicOperations.FixedTimeEquals(want, got)) return true;

        return !long.TryParse(t, out var started)
               || DateTimeOffset.UtcNow.ToUnixTimeSeconds() - started < MinFillSeconds;
    }

    /* -------------------------- سقفِ ارسال -------------------------- */

    /// <summary>
    /// سطل‌های جدا برای فرم‌های مختلف.
    ///
    /// چرا جدا: اگر یک شمارنده‌ی مشترک باشد، کسی که سه نظر روی وبلاگ گذاشته
    /// دیگر نمی‌تواند درخواست مشاوره بفرستد — یعنی یک محدودیتِ ضدّاسپم،
    /// جلوی همان کاری را می‌گیرد که کلِ سایت برایش ساخته شده. هر فرم سهمیه‌ی
    /// خودش را دارد.
    /// </summary>
    public const string LeadBucket = "lead";
    public const string CommentBucket = "comment";

    /// <summary>سقفِ نظر عمداً کمتر از سقفِ لید است: نظرِ پشتِ سرِ هم الگوی اسپم است، نه استفاده‌ی عادی.</summary>
    public const int MaxCommentsPerHour = 3;

    /// <summary>آیا این IP هنوز اجازه‌ی ارسال دارد؟ (فقط ارسالِ *موفق* شمرده می‌شود.)</summary>
    public bool WithinSendLimit(string ip, string bucket = LeadBucket, int? maxPerHour = null)
    {
        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var rec = _sends.GetValueOrDefault(Key(bucket, ip));
        if (rec.WindowStart == 0 || now - rec.WindowStart >= 3600) return true;
        return rec.Count < (maxPerHour ?? MaxPerHour);
    }

    public void CountSend(string ip, string bucket = LeadBucket)
    {
        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        _sends.AddOrUpdate(Key(bucket, ip),
            _ => (1, now),
            (_, rec) => now - rec.WindowStart >= 3600 ? (1, now) : (rec.Count + 1, rec.WindowStart));

        // پاک‌سازیِ تنبل: بدون این، هر IP برای همیشه یک ردیف در حافظه می‌ماند.
        if (_sends.Count > 5000)
            foreach (var pair in _sends)
                if (now - pair.Value.WindowStart >= 3600) _sends.TryRemove(pair.Key, out _);
    }

    /// <summary>کلیدِ شمارنده: سطل و IP با هم، تا سهمیه‌ی فرم‌ها قاطی نشود.</summary>
    private static string Key(string bucket, string ip) => $"{bucket}|{ip}";

    /* --------------------------- کمکی‌ها --------------------------- */

    private const int UsedSweepAt = 2_000;
    private const int UsedHardCap = 50_000;

    private void SweepUsed(long now)
    {
        if (_used.Count < UsedSweepAt) return;

        foreach (var pair in _used)
            if (pair.Value <= now) _used.TryRemove(pair.Key, out _);

        // شیر اطمینان: جاروی بالا فقط توکن‌های *منقضی* را برمی‌دارد. اگر کسی
        // با سرعتِ زیاد فرم بگیرد و بفرستد، می‌شود ده‌ها هزار توکنِ هنوز
        // معتبر داشت که هیچ‌کدام جارو نمی‌شوند و حافظه بالا می‌رود. در آن حالت
        // کل جدول پاک می‌شود؛ بدترین اثرش این است که چند کاربر باید کپچا را
        // دوباره حل کنند — که از پر شدن حافظه‌ی سرور به‌مراتب بهتر است.
        if (_used.Count > UsedHardCap) _used.Clear();
    }

    /// <summary>رقم‌های فارسی و عربی را به لاتین برمی‌گرداند.</summary>
    public static string NormalizeDigits(string s)
    {
        var buf = new char[s.Length];
        for (var i = 0; i < s.Length; i++)
        {
            var ch = s[i];
            if (ch >= '۰' && ch <= '۹') ch = (char)('0' + (ch - '۰'));      // فارسی U+06F0
            else if (ch >= '٠' && ch <= '٩') ch = (char)('0' + (ch - '٠')); // عربی  U+0660
            buf[i] = ch;
        }
        return new string(buf);
    }

    private string Sign(string payload)
    {
        using var mac = new HMACSHA256(_key);
        return B64(mac.ComputeHash(Encoding.UTF8.GetBytes(payload)));
    }

    private static string B64(byte[] b) =>
        Convert.ToBase64String(b).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static byte[] FromB64(ReadOnlySpan<char> s)
    {
        var t = new string(s).Replace('-', '+').Replace('_', '/');
        return Convert.FromBase64String(t.PadRight(t.Length + (4 - t.Length % 4) % 4, '='));
    }
}
