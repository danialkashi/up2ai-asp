using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Up2Ai.Services;

namespace Up2Ai.Pages.Admin;

/// <summary>
/// پایه‌ی همه‌ی صفحه‌های پنل.
///
/// `/admin/login` هم عمداً از همین پایه استفاده می‌کند ولی `RequireAuth` را
/// صدا نمی‌زند — این‌طوری هیچ مسیر ادمینی نمی‌ماند که از قلم بیفتد.
///
/// هر handler (چه GET چه POST) *خودش* `RequireAuth` را صدا می‌زند و به گارد
/// لایوت اکتفا نمی‌کند: یک POST یک نقطه‌ی ورودی مستقل است و می‌شود مستقیم
/// صدایش زد، بدون این‌که از صفحه‌ی محافظت‌شده رد شده باشی.
/// </summary>
public abstract class AdminPageModel : PageModel
{
    protected readonly ContentStore Store;
    public AdminAuth Auth { get; }
    protected readonly AdminUserStore Users;

    protected AdminPageModel(ContentStore store, AdminAuth auth, AdminUserStore users)
    {
        Store = store;
        Auth = auth;
        Users = users;
    }

    public Cv C { get; private set; }

    /// <summary>آیا پیکربندی ورود کامل است؟ اگر نه، پنل حتی فرم ورود هم نشان نمی‌دهد.</summary>
    public bool Configured => Auth.IsConfigured;

    public List<string> MissingConfig => Auth.MissingConfig();

    public bool Authed { get; private set; }

    /// <summary>شناسه‌ی کاربرِ واردشده — یا <see cref="AdminUserStore.EnvUserId"/> برای ورودِ محیطی.</summary>
    public string? CurrentUserId { get; private set; }

    /// <summary>کاربرِ واردشده، اگر کاربرِ واقعیِ انبار باشد (نه ورودِ محیطی).</summary>
    public AdminUser? CurrentUser { get; private set; }

    /// <summary>نامی که در هدر پنل نشان داده می‌شود.</summary>
    public string CurrentUserLabel => CurrentUser?.Label
        ?? (CurrentUserId == AdminUserStore.EnvUserId ? "مدیر سایت" : "");

    public override void OnPageHandlerExecuting(Microsoft.AspNetCore.Mvc.Filters.PageHandlerExecutingContext context)
    {
        C = new Cv(Store.Get());
        ViewData["Content"] = C;
        ViewData["NoIndex"] = true;

        CurrentUserId = Configured ? Auth.ReadToken(Request.Cookies[AdminAuth.CookieName]) : null;
        CurrentUser = CurrentUserId is not null && CurrentUserId != AdminUserStore.EnvUserId
            ? Users.ById(CurrentUserId)
            : null;

        // کاربری که حذف یا غیرفعال شده نباید با کوکیِ قبلی‌اش داخل بماند.
        // بدون این بررسی، «حذف کاربر» تا انقضای کوکی (۱۲ ساعت) هیچ اثری
        // نداشت — یعنی دقیقاً وقتی که به آن نیاز داری کار نمی‌کرد.
        if (CurrentUserId is not null && CurrentUserId != AdminUserStore.EnvUserId
            && (CurrentUser is null || !CurrentUser.Active))
        {
            CurrentUserId = null;
            CurrentUser = null;
        }

        Authed = CurrentUserId is not null;
        ViewData["Authed"] = Authed;
        ViewData["UserLabel"] = CurrentUserLabel;
        base.OnPageHandlerExecuting(context);
    }

    /// <summary>
    /// اگر وارد نشده باشد یک ری‌دایرکت برمی‌گرداند؛ وگرنه null.
    /// الگوی استفاده در هر handler:
    ///     var guard = RequireAuth(); if (guard is not null) return guard;
    /// </summary>
    protected IActionResult? RequireAuth() =>
        Authed ? null : RedirectToPage("/Admin/Login");
}
