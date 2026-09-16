using System.Globalization;
using Microsoft.AspNetCore.Mvc;
using Up2Ai.Pages;
using Up2Ai.Services;

namespace Up2Ai.Pages.Admin;

/// <summary>
/// صندوق لید — همه‌ی درخواست‌هایی که فرم تماس ذخیره کرده.
///
/// فیلتر وضعیت با یک query param است، نه state سمت کلاینت —
/// یعنی این صفحه هیچ جاوااسکریپتی لازم ندارد و حتی بدون آن هم کامل کار
/// می‌کند، مثل بقیه‌ی پنل. تمام اقدامات (تغییر وضعیت، یادداشت، حذف) هم
/// فرم POST باشند و نه دکمه‌ی جاوااسکریپتی.
///
/// هر handler *خودش* دوباره ورود را بررسی می‌کند و به گارد لایوت اکتفا
/// نمی‌کند: یک POST یک نقطه‌ی ورودی مستقل است و می‌شود مستقیم صدایش زد،
/// بدون این‌که از صفحه‌ی محافظت‌شده رد شده باشی.
/// </summary>
public class LeadsModel : AdminPageModel
{
    private readonly LeadStore _leads;

    public LeadsModel(ContentStore store, AdminAuth auth, LeadStore leads)
        : base(store, auth) => _leads = leads;

    /// <summary>مقدار خام `?filter=` — عیناً نگه داشته می‌شود تا بعد از POST همان صفحه برگردد.</summary>
    public string? Filter { get; private set; }

    public string CurrentStatus { get; private set; } = "";

    public List<Lead> Leads { get; private set; } = new();

    public Dictionary<string, int> StatusCounts { get; private set; } = new();

    public IActionResult OnGet(string? filter)
    {
        var guard = RequireAuth();
        if (guard is not null) return guard;

        Filter = filter;
        CurrentStatus = filter ?? "";

        var all = _leads.List();
        
        // Count leads by status
        StatusCounts = new()
        {
            { "", all.Count },  // "All"
            { LeadStatus.New, all.Count(l => l.Status == LeadStatus.New) },
            { LeadStatus.Contacted, all.Count(l => l.Status == LeadStatus.Contacted) },
            { LeadStatus.FollowUp, all.Count(l => l.Status == LeadStatus.FollowUp) },
            { LeadStatus.Done, all.Count(l => l.Status == LeadStatus.Done) },
        };

        // Filter by status
        Leads = string.IsNullOrEmpty(filter) 
            ? all 
            : all.Where(l => l.Status == filter).ToList();

        return Page();
    }

    /// <summary>تغییر وضعیت لید.</summary>
    public async Task<IActionResult> OnPostSetStatusAsync(string? id, string? status, string? filter)
    {
        var guard = RequireAuth();
        if (guard is not null) return guard;

        if (!string.IsNullOrEmpty(id) && !string.IsNullOrEmpty(status) && LeadStatus.IsValid(status))
            await _leads.SetStatusAsync(id, status);
        
        return Back(filter);
    }

    /// <summary>علامت‌گذاری لید به‌عنوان تماس‌گرفته‌شده و به‌روز رسانی زمان تماس.</summary>
    public async Task<IActionResult> OnPostMarkContactedAsync(string? id, string? filter)
    {
        var guard = RequireAuth();
        if (guard is not null) return guard;

        if (!string.IsNullOrEmpty(id))
            await _leads.SetContactedAsync(id);
        
        return Back(filter);
    }

    /// <summary>ذخیره‌ی یادداشت‌های داخلی برای لید.</summary>
    public async Task<IActionResult> OnPostSetNotesAsync(string? id, string? notes, string? filter)
    {
        var guard = RequireAuth();
        if (guard is not null) return guard;

        if (!string.IsNullOrEmpty(id))
            await _leads.SetNotesAsync(id, notes);
        
        return Back(filter);
    }

    /// <summary>حذف یک لید.</summary>
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
            return ToPersianDigits(local.ToString("d MMMM yyyy'، 'H:mm", Fa));
        }
        catch (Exception)
        {
            return at;
        }
    }

    public static string StatusToPersian(string status) => status switch
    {
        LeadStatus.New => "جدید",
        LeadStatus.Contacted => "تماس گرفته شد",
        LeadStatus.FollowUp => "پیگیری مورد نیاز",
        LeadStatus.Done => "انجام شده",
        _ => status,
    };

    public static string StatusToColor(string status) => status switch
    {
        LeadStatus.New => "bg-blue-100 text-blue-800",
        LeadStatus.Contacted => "bg-green-100 text-green-800",
        LeadStatus.FollowUp => "bg-amber-100 text-amber-900",
        LeadStatus.Done => "bg-gray-100 text-gray-800",
        _ => "bg-gray-100 text-gray-800",
    };

    private static string ToPersianDigits(string s)
    {
        var buf = s.ToCharArray();
        for (var i = 0; i < buf.Length; i++)
            if (buf[i] >= '0' && buf[i] <= '9')
                buf[i] = (char)('۰' + (buf[i] - '0'));
        return new string(buf);
    }
}
