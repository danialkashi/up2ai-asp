using Microsoft.AspNetCore.Mvc;
using Up2Ai.Services;

namespace Up2Ai.Pages.Admin;

/// <summary>
/// ورود به پنل.
///
/// دو راهِ ورود، به همین ترتیب:
///
///   ۱) کاربرانِ ساخته‌شده در پنل (نام کاربری + رمز) — راهِ عادی.
///   ۲) رمزِ محیطیِ `ADMIN_PASSWORD_HASH` — راهِ اولیه و راهِ نجات.
///
/// چرا دومی حذف نشد: تا وقتی هیچ کاربری ساخته نشده باشد، تنها راهِ ورود
/// همان است؛ و اگر روزی رمزِ همه‌ی کاربرها فراموش شود، صاحبِ سایت با
/// دسترسی به سرور می‌تواند از همان راه برگردد. حذفش یعنی یک قفلِ بی‌کلید.
/// </summary>
public class LoginModel : AdminPageModel
{
    private readonly IWebHostEnvironment _env;
    private readonly IConfiguration _config;
    private readonly AdminUserStore _users;

    public LoginModel(ContentStore store, AdminAuth auth, AdminUserStore users,
                      IWebHostEnvironment env, IConfiguration config)
        : base(store, auth, users)
    {
        _env = env;
        _config = config;
        _users = users;
    }

    // نام فیلد عمداً کوچک است تا با HTML نسخه‌ی قبلی مو‌به‌مو یکی بماند
    // (اسکریپت‌های تست هم `input[name="password"]` را می‌گیرند).
    [BindProperty(Name = "password")] public string Password { get; set; } = "";
    [BindProperty(Name = "username")] public string Username { get; set; } = "";

    public string? Error { get; private set; }

    /// <summary>
    /// آیا فرم باید فیلد نام کاربری داشته باشد؟
    ///
    /// تا وقتی هیچ کاربری ساخته نشده، فرم دقیقاً همان فرمِ تک‌فیلدیِ قبلی
    /// می‌ماند — کسی که فقط یک رمز داشت، نباید ناگهان با فیلدِ تازه‌ای
    /// روبه‌رو شود که نمی‌داند چه بنویسد.
    /// </summary>
    public bool ShowUsername => !_users.IsEmpty();

    public IActionResult OnGet()
    {
        // اگر از قبل وارد است، دوباره فرم ورود نشان نده.
        if (Authed) return RedirectToPage("/Admin/Index");
        return Page();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        if (!Configured) return Page();

        // کلید محدودیت تلاش: آی‌پی درخواست، با همان تعریفی که فرم تماس هم
        // استفاده می‌کند. قبلاً این‌جا مستقیم RemoteIpAddress خوانده می‌شد:
        // پشتِ پراکسی آن آدرس برای همه یکی است، پس پنج رمزِ غلطِ یک مهاجم،
        // ورودِ خودِ مدیر را هم ده دقیقه می‌بست.
        var key = ClientIp.Of(HttpContext, _config);
        var (allowed, retryInSec) = AdminAuth.ThrottleCheck(key);
        if (!allowed)
        {
            var minutes = (int)Math.Ceiling(retryInSec / 60.0);
            Error = $"تلاش‌های ناموفق زیاد بود. {PersianYear.ToPersianDigits(minutes.ToString())} دقیقه‌ی دیگر دوباره امتحان کن.";
            return Page();
        }

        var password = Password ?? "";
        var username = (Username ?? "").Trim();
        string? userId = null;

        if (password.Length > 0)
        {
            if (username.Length > 0)
            {
                var user = _users.Verify(username, password);
                if (user is not null)
                {
                    userId = user.Id;
                    await _users.TouchLoginAsync(user.Id);
                }
            }
            // رمزِ محیطی: وقتی نام کاربری داده نشده (فرمِ تک‌فیلدیِ قدیمی) یا
            // وقتی نام کاربری با هیچ کاربری نخواند. این‌طور، صاحبِ سایت حتی
            // بعد از ساختنِ کاربرها هم راهِ برگشت دارد.
            if (userId is null
                && !string.IsNullOrEmpty(Auth.Hash)
                && AdminAuth.VerifyPassword(password, Auth.Hash!))
            {
                userId = AdminUserStore.EnvUserId;
            }
        }

        if (userId is null)
        {
            AdminAuth.ThrottleFail(key);
            // پیام عمداً نمی‌گوید کدام‌یک غلط بوده: «کاربر پیدا نشد» به مهاجم
            // می‌گفت کدام نام کاربری واقعی است.
            Error = ShowUsername ? "نام کاربری یا رمز درست نیست." : "رمز درست نیست.";
            return Page();
        }

        AdminAuth.ThrottleReset(key);
        Response.Cookies.Append(AdminAuth.CookieName, Auth.MakeToken(userId),
            Auth.CookieOptions(!_env.IsDevelopment()));
        return RedirectToPage("/Admin/Index");
    }

    public IActionResult OnPostLogout()
    {
        Response.Cookies.Delete(AdminAuth.CookieName, new CookieOptions { Path = AdminAuth.CookiePath });
        return RedirectToPage("/Admin/Login");
    }
}
