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

    protected AdminPageModel(ContentStore store, AdminAuth auth)
    {
        Store = store;
        Auth = auth;
    }

    public Cv C { get; private set; }

    /// <summary>آیا پیکربندی ورود کامل است؟ اگر نه، پنل حتی فرم ورود هم نشان نمی‌دهد.</summary>
    public bool Configured => Auth.IsConfigured;

    public List<string> MissingConfig => Auth.MissingConfig();

    public bool Authed { get; private set; }

    public override void OnPageHandlerExecuting(Microsoft.AspNetCore.Mvc.Filters.PageHandlerExecutingContext context)
    {
        C = new Cv(Store.Get());
        ViewData["Content"] = C;
        ViewData["NoIndex"] = true;
        Authed = Configured && Auth.ReadToken(Request.Cookies[AdminAuth.CookieName]);
        ViewData["Authed"] = Authed;
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
