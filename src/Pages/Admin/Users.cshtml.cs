using Microsoft.AspNetCore.Mvc;
using Up2Ai.Services;

namespace Up2Ai.Pages.Admin;

/// <summary>
/// کاربران پنل: ساخت، تغییر رمز، فعال/غیرفعال کردن و حذف.
///
/// تا وقتی هیچ کاربری ساخته نشده، ورود با همان رمزِ محیطی انجام می‌شود و
/// این صفحه راهِ عبور از آن حالت است. بعد از ساختنِ اولین کاربر، فرمِ ورود
/// فیلدِ نام کاربری هم می‌گیرد.
/// </summary>
public class UsersModel : AdminPageModel
{
    public UsersModel(ContentStore store, AdminAuth auth, AdminUserStore users)
        : base(store, auth, users) { }

    public List<AdminUser> All { get; private set; } = new();
    public string? Notice { get; private set; }
    public string? Error { get; private set; }

    /// <summary>
    /// آیا رمزِ محیطی هنوز کار می‌کند؟
    ///
    /// روی تصمیم‌ها اثر دارد: تا وقتی این راه باز است، حذفِ آخرین کاربر
    /// پنل را قفل نمی‌کند. صفحه هم همین را به کاربر می‌گوید تا بداند کجای
    /// کار است.
    /// </summary>
    public bool EnvLoginAvailable => !string.IsNullOrEmpty(Auth.Hash);

    /* ---- فرم افزودن کاربر ---- */
    [BindProperty] public string NewUsername { get; set; } = "";
    [BindProperty] public string NewPassword { get; set; } = "";
    [BindProperty] public string NewDisplayName { get; set; } = "";

    /* ---- فرم تغییر رمز ---- */
    [BindProperty] public string PasswordUserId { get; set; } = "";
    [BindProperty] public string NewPasswordForUser { get; set; } = "";

    /// <summary>کاربری که فرمِ تغییر رمزش باز است (از آدرس می‌آید).</summary>
    public string? PasswordFormFor { get; private set; }

    /// <summary>کاربری که تأییدِ حذفش باز است.</summary>
    public string? ConfirmDeleteId { get; private set; }

    private IActionResult Reload()
    {
        All = Users.List();
        return Page();
    }

    public IActionResult OnGet(string? changePassword, string? confirmDelete)
    {
        var guard = RequireAuth();
        if (guard is not null) return guard;

        PasswordFormFor = changePassword;
        ConfirmDeleteId = confirmDelete;
        if (TempData["users.notice"] is string n) Notice = n;
        return Reload();
    }

    public async Task<IActionResult> OnPostAddAsync()
    {
        var guard = RequireAuth();
        if (guard is not null) return guard;

        var result = await Users.AddAsync(NewUsername ?? "", NewPassword ?? "", NewDisplayName ?? "");
        if (!result.Ok)
        {
            Error = result.Error;
            return Reload();
        }

        TempData["users.notice"] = $"کاربر «{(NewUsername ?? "").Trim()}» ساخته شد.";
        return RedirectToPage("/Admin/Users");
    }

    public async Task<IActionResult> OnPostPasswordAsync()
    {
        var guard = RequireAuth();
        if (guard is not null) return guard;

        var result = await Users.SetPasswordAsync(PasswordUserId ?? "", NewPasswordForUser ?? "");
        if (!result.Ok)
        {
            Error = result.Error;
            PasswordFormFor = PasswordUserId;
            return Reload();
        }

        TempData["users.notice"] = "رمز عوض شد.";
        return RedirectToPage("/Admin/Users");
    }

    public async Task<IActionResult> OnPostToggleAsync(string id)
    {
        var guard = RequireAuth();
        if (guard is not null) return guard;

        var user = Users.ById(id);
        if (user is null) return RedirectToPage("/Admin/Users");

        var result = await Users.SetActiveAsync(id, !user.Active, EnvLoginAvailable);
        if (!result.Ok)
        {
            Error = result.Error;
            return Reload();
        }

        TempData["users.notice"] = user.Active ? "کاربر غیرفعال شد." : "کاربر فعال شد.";
        return RedirectToPage("/Admin/Users");
    }

    public async Task<IActionResult> OnPostDeleteAsync(string id)
    {
        var guard = RequireAuth();
        if (guard is not null) return guard;

        var result = await Users.DeleteAsync(id, EnvLoginAvailable);
        if (!result.Ok)
        {
            Error = result.Error;
            return Reload();
        }

        // اگر کاربر خودش را حذف کرده، کوکی‌اش دیگر به کسی اشاره نمی‌کند؛
        // پاکش می‌کنیم تا به‌جای رفتار عجیب، تمیز به صفحه‌ی ورود برگردد.
        if (id == CurrentUserId)
        {
            Response.Cookies.Delete(AdminAuth.CookieName, new CookieOptions { Path = AdminAuth.CookiePath });
            return RedirectToPage("/Admin/Login");
        }

        TempData["users.notice"] = "کاربر حذف شد.";
        return RedirectToPage("/Admin/Users");
    }
}
