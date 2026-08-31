using Microsoft.AspNetCore.Mvc;
using Up2Ai.Services;

namespace Up2Ai.Pages.Admin;

public class LoginModel : AdminPageModel
{
    private readonly IWebHostEnvironment _env;

    public LoginModel(ContentStore store, AdminAuth auth, IWebHostEnvironment env)
        : base(store, auth) => _env = env;

    // نام فیلد عمداً کوچک است تا با HTML نسخه‌ی قبلی مو‌به‌مو یکی بماند
    // (اسکریپت‌های تست هم `input[name="password"]` را می‌گیرند).
    [BindProperty(Name = "password")] public string Password { get; set; } = "";

    public string? Error { get; private set; }

    public IActionResult OnGet()
    {
        // اگر از قبل وارد است، دوباره فرم ورود نشان نده.
        if (Authed) return RedirectToPage("/Admin/Index");
        return Page();
    }

    public IActionResult OnPost()
    {
        if (!Configured) return Page();

        // کلید محدودیت تلاش: آی‌پی درخواست. برای پنلی که یک نفر ازش استفاده
        // می‌کند کافی است.
        var key = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        var (allowed, retryInSec) = AdminAuth.ThrottleCheck(key);
        if (!allowed)
        {
            var minutes = (int)Math.Ceiling(retryInSec / 60.0);
            Error = $"تلاش‌های ناموفق زیاد بود. {ToPersianDigits(minutes.ToString())} دقیقه‌ی دیگر دوباره امتحان کن.";
            return Page();
        }

        if (string.IsNullOrEmpty(Password) || !AdminAuth.VerifyPassword(Password, Auth.Hash!))
        {
            AdminAuth.ThrottleFail(key);
            Error = "رمز درست نیست.";
            return Page();
        }

        AdminAuth.ThrottleReset(key);
        Response.Cookies.Append(AdminAuth.CookieName, Auth.MakeToken(),
            Auth.CookieOptions(!_env.IsDevelopment()));
        return RedirectToPage("/Admin/Index");
    }

    public IActionResult OnPostLogout()
    {
        Response.Cookies.Delete(AdminAuth.CookieName, new CookieOptions { Path = AdminAuth.CookiePath });
        return RedirectToPage("/Admin/Login");
    }

    private static string ToPersianDigits(string s)
    {
        var buf = s.ToCharArray();
        for (var i = 0; i < buf.Length; i++)
            if (buf[i] >= '0' && buf[i] <= '9') buf[i] = (char)('۰' + (buf[i] - '0'));
        return new string(buf);
    }
}
