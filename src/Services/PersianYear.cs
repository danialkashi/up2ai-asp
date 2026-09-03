using System.Globalization;

namespace Up2Ai.Services;

/// <summary>
/// سالِ جاری به هجری شمسی، با رقم‌های فارسی.
///
/// چرا اصلاً کد لازم دارد: پاورقی سایت خط کپی‌رایت را با جای‌گیر {year}
/// می‌سازد و مقدارِ آن قبلاً یک رشته‌ی ثابت («۱۴۰۴») داخل محتوا بود. یعنی
/// اول فروردین، سایت تا وقتی یک نفر یادش بیفتد از پنل دستی عوضش کند، سال
/// گذشته را نشان می‌داد.
///
/// دو نکته‌ی ریز که این‌جا رعایت شده:
///
///   ۱) تبدیل تاریخ با <see cref="PersianCalendar"/> خودِ فریم‌ورک انجام
///      می‌شود، نه با فرمولِ دستی — پس کبیسه و مرزِ سال درست است.
///
///   ۲) مبنای محاسبه وقتِ *ایران* است، نه UTC و نه ساعتِ سرور. سروری که
///      روی UTC است، در فاصله‌ی ۲۰:۳۰ تا ۲۴:۰۰ به وقت تهران هنوز «دیروز»
///      است؛ درست سرِ تحویل سال، همان چند ساعت یعنی نمایشِ سالِ اشتباه.
///      اگر پایگاه‌داده‌ی مناطق زمانی روی سیستم نبود (نصب‌های خیلی مینیمالِ
///      لینوکس)، به‌جای خطا روی +۳:۳۰ ثابت برمی‌گردد.
/// </summary>
public static class PersianYear
{
    private static readonly PersianCalendar Cal = new();

    private static readonly TimeSpan IranFallbackOffset = TimeSpan.FromMinutes(210); // +۳:۳۰

    /// <summary>سالِ شمسیِ همین لحظه به رقم فارسی — مثل «۱۴۰۵».</summary>
    public static string Now() => ToPersianDigits(Cal.GetYear(NowInIran()).ToString(CultureInfo.InvariantCulture));

    private static DateTime NowInIran()
    {
        foreach (var id in new[] { "Iran Standard Time", "Asia/Tehran" })
        {
            try
            {
                return TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, TimeZoneInfo.FindSystemTimeZoneById(id)).DateTime;
            }
            catch (TimeZoneNotFoundException) { }
            catch (InvalidTimeZoneException) { }
        }
        return DateTimeOffset.UtcNow.ToOffset(IranFallbackOffset).DateTime;
    }

    private static readonly string[] Months =
    {
        "فروردین", "اردیبهشت", "خرداد", "تیر", "مرداد", "شهریور",
        "مهر", "آبان", "آذر", "دی", "بهمن", "اسفند",
    };

    /// <summary>
    /// تاریخِ یک زمانِ ذخیره‌شده (ISO/UTC) به شکلِ خواندنیِ فارسی — «۱۴ مهر ۱۴۰۴».
    ///
    /// همان‌جا هم به وقتِ ایران تبدیل می‌شود، وگرنه نوشته‌ای که ساعت ۲۲ به
    /// وقت تهران منتشر شده با تاریخِ دیروز نشان داده می‌شد.
    /// </summary>
    public static string FormatDate(string? iso)
    {
        var utc = ParseIso(iso);
        if (utc is null) return "";
        var local = ToIran(utc.Value);
        var day = ToPersianDigits(Cal.GetDayOfMonth(local).ToString(CultureInfo.InvariantCulture));
        var month = Months[Cal.GetMonth(local) - 1];
        var year = ToPersianDigits(Cal.GetYear(local).ToString(CultureInfo.InvariantCulture));
        return $"{day} {month} {year}";
    }

    /// <summary>
    /// زمانِ ذخیره‌شده به <see cref="DateTime"/> (UTC)، یا null اگر خوانده نشد.
    ///
    /// همه‌ی زمان‌ها با «Z» ذخیره می‌شوند، ولی فایلِ داده را آدم هم می‌تواند
    /// ویرایش کند؛ پس اگر منطقه‌ی زمانی نداشت، UTC فرض می‌شود و اگر محلی
    /// بود، تبدیل می‌شود. (ترکیب کردن RoundtripKind با AdjustToUniversal
    /// مجاز نیست و استثنا می‌دهد — همان اشتباهی که اول این‌جا بود.)
    /// </summary>
    public static DateTime? ParseIso(string? iso)
    {
        if (!DateTime.TryParse(iso, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var dt))
            return null;

        return dt.Kind switch
        {
            DateTimeKind.Utc => dt,
            DateTimeKind.Local => dt.ToUniversalTime(),
            _ => DateTime.SpecifyKind(dt, DateTimeKind.Utc),
        };
    }

    private static DateTime ToIran(DateTime utc)
    {
        foreach (var id in new[] { "Iran Standard Time", "Asia/Tehran" })
        {
            try
            {
                return TimeZoneInfo.ConvertTime(new DateTimeOffset(utc, TimeSpan.Zero),
                    TimeZoneInfo.FindSystemTimeZoneById(id)).DateTime;
            }
            catch (TimeZoneNotFoundException) { }
            catch (InvalidTimeZoneException) { }
        }
        return new DateTimeOffset(utc, TimeSpan.Zero).ToOffset(IranFallbackOffset).DateTime;
    }

    /// <summary>۰۱۲۳۴۵۶۷۸۹ → ۰۱۲۳۴۵۶۷۸۹ (ارقام فارسی، U+06F0…U+06F9).</summary>
    public static string ToPersianDigits(string s)
    {
        var buf = new char[s.Length];
        for (var i = 0; i < s.Length; i++)
            buf[i] = s[i] >= '0' && s[i] <= '9' ? (char)('۰' + (s[i] - '0')) : s[i];
        return new string(buf);
    }
}
