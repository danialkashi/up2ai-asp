using System.Globalization;
using Microsoft.AspNetCore.Mvc;
using Up2Ai.Pages;
using Up2Ai.Services;

namespace Up2Ai.Pages.Admin;

/// <summary>
/// صندوق لید — همه‌ی درخواست‌هایی که فرم تماس ذخیره کرده.
///
/// فیلتر «فقط پیگیری‌نشده‌ها» با یک query param است، نه state سمت کلاینت —
/// یعنی این صفحه هیچ جاوااسکریپتی لازم ندارد و حتی بدون آن هم کامل کار
/// می‌کند، مثل بقیه‌ی پنل. همین دلیل باعث می‌شود «پیگیری شد» و «حذف» هم
/// فرم POST باشند و نه دکمه‌ی جاوااسکریپتی.
///
/// هر handler *خودش* دوباره ورود را بررسی می‌کند و به گارد لایوت اکتفا
/// نمی‌کند: یک POST یک نقطه‌ی ورودی مستقل است و می‌شود مستقیم صدایش زد،
/// بدون این‌که از صفحه‌ی محافظت‌شده رد شده باشی.
/// </summary>
public class LeadsModel : AdminPageModel
{
    private readonly LeadStore _leads;

    public LeadsModel(ContentStore store, AdminAuth auth, AdminUserStore users, LeadStore leads)
        : base(store, auth, users) => _leads = leads;

    /// <summary>مقدار خام `?filter=` — عیناً نگه داشته می‌شود تا بعد از POST همان صفحه برگردد.</summary>
    public string? Filter { get; private set; }

    public bool OnlyUnhandled { get; private set; }

    public List<Lead> Leads { get; private set; } = new();

    public int TotalCount { get; private set; }

    public int UnhandledCount { get; private set; }

    public IActionResult OnGet(string? filter)
    {
        var guard = RequireAuth();
        if (guard is not null) return guard;

        Filter = filter;
        OnlyUnhandled = filter == "unhandled";

        var all = _leads.List();
        TotalCount = all.Count;
        UnhandledCount = all.Count(l => !l.Handled);
        Leads = OnlyUnhandled ? all.Where(l => !l.Handled).ToList() : all;

        return Page();
    }

    /// <summary>علامت‌گذاری/برداشتن «پیگیری شد» — فقط از پنل مدیریت.</summary>
    public async Task<IActionResult> OnPostToggleAsync(string? id, bool handled, string? filter)
    {
        var guard = RequireAuth();
        if (guard is not null) return guard;

        if (!string.IsNullOrEmpty(id)) await _leads.SetHandledAsync(id, handled);
        return Back(filter);
    }

    /// <summary>حذف یک لید — فقط از پنل مدیریت (مثلاً برای ورودی‌های آزمایشی/اسپم).</summary>
    public async Task<IActionResult> OnPostDeleteAsync(string? id, string? filter)
    {
        var guard = RequireAuth();
        if (guard is not null) return guard;

        if (!string.IsNullOrEmpty(id)) await _leads.DeleteAsync(id);
        return Back(filter);
    }

    /// <summary>
    /// نسخه‌ی Next بعد از اکشن با `revalidatePath` روی همان URL می‌ماند. این‌جا
    /// چون POST است، الگوی POST-Redirect-GET لازم است تا رفرشِ مرورگر عمل را
    /// دوباره اجرا نکند — و فیلتر فعلی باید در ری‌دایرکت حفظ شود.
    /// </summary>
    private IActionResult Back(string? filter) =>
        RedirectToPage("/Admin/Leads", new { filter = string.IsNullOrEmpty(filter) ? null : filter });

    /* ------------------------------- تاریخ ------------------------------- */

    // معادل `new Date(at).toLocaleString("fa-IR", { dateStyle: "medium", timeStyle: "short" })`
    // در نسخه‌ی Next: تقویم جلالی، ماهِ نام‌دار، ساعت ۲۴ساعته و ارقام فارسی.
    // .NET خودش جای‌گزینی ارقام را انجام نمی‌دهد، پس آخرین قدم دستی است.
    private static readonly CultureInfo Fa = MakeFa();

    private static CultureInfo MakeFa()
    {
        var c = (CultureInfo)CultureInfo.GetCultureInfo("fa-IR").Clone();
        if (c.DateTimeFormat.Calendar is not PersianCalendar)
        {
            var persian = c.OptionalCalendars.FirstOrDefault(x => x is PersianCalendar);
            if (persian is not null)
            {
                try { c.DateTimeFormat.Calendar = persian; }
                catch (ArgumentException) { /* تقویم پشتیبانی نشد؛ با پیش‌فرض ادامه می‌دهیم */ }
            }
        }
        return c;
    }

    public static string FormatAt(string at)
    {
        if (!DateTime.TryParse(at, CultureInfo.InvariantCulture,
                DateTimeStyles.RoundtripKind, out var dt))
            return at;

        var local = dt.Kind == DateTimeKind.Utc ? dt.ToLocalTime() : dt;
        try
        {
            // «، » همان جداکننده‌ای است که CLDR برای فارسی بین تاریخ و ساعت می‌گذارد.
            return ToPersianDigits(local.ToString("d MMMM yyyy'، 'H:mm", Fa));
        }
        catch (Exception)
        {
            return at;
        }
    }

    private static string ToPersianDigits(string s)
    {
        var buf = s.ToCharArray();
        for (var i = 0; i < buf.Length; i++)
            if (buf[i] >= '0' && buf[i] <= '9')
                buf[i] = (char)('۰' + (buf[i] - '0'));
        return new string(buf);
    }
}
