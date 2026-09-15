using Microsoft.AspNetCore.Mvc;
using System.Security.Cryptography;
using System.Text;
using Up2Ai.Services;

namespace Up2Ai.Pages.Admin;

public class LoginModel : AdminPageModel
{
    private readonly IWebHostEnvironment _env;

    [BindProperty(Name = "captcha_answer")] public string CaptchaAnswer { get; set; } = "";
    [BindProperty(Name = "captcha_token")] public string CaptchaToken { get; set; } = "";
    public string CaptchaQuestion { get; private set; } = "";

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
        if (Configured) GenerateCaptcha();
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
            GenerateCaptcha();
            return Page();
        }

        if (!ValidateCaptcha(CaptchaAnswer, CaptchaToken))
        {
            AdminAuth.ThrottleFail(key);
            Error = "کپچا درست نیست.";
            GenerateCaptcha();
            return Page();
        }

        if (string.IsNullOrEmpty(Password) || !AdminAuth.VerifyPassword(Password, Auth.Hash!))
        {
            AdminAuth.ThrottleFail(key);
            Error = "رمز درست نیست.";
            GenerateCaptcha();
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
        return RedirectToPage("/admin/account/login");
    }

    private void GenerateCaptcha()
    {
        // اعداد کوچک برای کپچای ساده: جمع دو عدد تک‌رقمی
        var a = RandomNumberGenerator.GetInt32(2, 10);
        var b = RandomNumberGenerator.GetInt32(2, 10);
        var answer = a + b;

        var expires = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() + 60_000; // اعتبار ۶۰ ثانیه
        var payload = $"{answer}:{expires}";

        // توکنِ امضا شده: base64url(payload).base64url(sig)
        var payloadBytes = Encoding.UTF8.GetBytes(payload);
        var sig = SignWithSecret(payloadBytes, Encoding.UTF8.GetBytes(Auth.Secret!));
        CaptchaToken = Base64Url(payloadBytes) + "." + Base64Url(sig);

        // سوال با ارقام فارسی
        CaptchaQuestion = $"لطفاً حاصل {ToPersianDigits(a.ToString())} + {ToPersianDigits(b.ToString())} را وارد کنید";
    }

    private bool ValidateCaptcha(string answerText, string token)
    {
        if (string.IsNullOrEmpty(token) || string.IsNullOrEmpty(answerText)) return false;
        var parts = token.Split('.');
        if (parts.Length != 2) return false;

        byte[] payloadBytes, sigBytes;
        try
        {
            payloadBytes = FromBase64Url(parts[0]);
            sigBytes = FromBase64Url(parts[1]);
        }
        catch
        {
            return false;
        }

        var wantSig = SignWithSecret(payloadBytes, Encoding.UTF8.GetBytes(Auth.Secret!));
        if (wantSig.Length != sigBytes.Length || !CryptographicOperations.FixedTimeEquals(wantSig, sigBytes))
            return false;

        var payload = Encoding.UTF8.GetString(payloadBytes);
        var seg = payload.Split(':');
        if (seg.Length != 2) return false;
        if (!int.TryParse(seg[0], out var expected)) return false;
        if (!long.TryParse(seg[1], out var expiry)) return false;
        if (DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() > expiry) return false;

        // نرمال‌سازی ارقام (فارسی/عربی → لاتین)
        var normalized = NormalizeDigits(answerText.Trim());
        if (!int.TryParse(normalized, out var given)) return false;
        return given == expected;
    }

    private static string NormalizeDigits(string s)
    {
        var sb = new StringBuilder(s.Length);
        foreach (var c in s)
        {
            if (c >= '۰' && c <= '۹') sb.Append((char)('0' + (c - '۰')));
            else if (c >= '٠' && c <= '٩') sb.Append((char)('0' + (c - '٠')));
            else sb.Append(c);
        }
        return sb.ToString();
    }

    private static byte[] FromBase64Url(string s)
    {
        var t = s.Replace('-', '+').Replace('_', '/');
        return Convert.FromBase64String(t.PadRight(t.Length + (4 - t.Length % 4) % 4, '='));
    }

    private static string Base64Url(byte[] b) => Convert.ToBase64String(b).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static byte[] SignWithSecret(byte[] payload, byte[] secret)
    {
        using var mac = new HMACSHA256(secret);
        return mac.ComputeHash(payload);
    }

    private static string ToPersianDigits(string s)
    {
        var buf = s.ToCharArray();
        for (var i = 0; i < buf.Length; i++)
            if (buf[i] >= '0' && buf[i] <= '9') buf[i] = (char)('۰' + (buf[i] - '0'));
        return new string(buf);
    }
}
