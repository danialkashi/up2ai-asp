using Microsoft.AspNetCore.Mvc;
using System.Security.Cryptography;
using System.Text;
using Up2Ai.Services;

namespace Up2Ai.Pages;

public class IndexModel : ContentPageModel
{
    private readonly LeadStore _leads;
    private readonly ILogger<IndexModel> _log;
    private readonly AdminAuth _auth;

    public IndexModel(ContentStore store, LeadStore leads, ILogger<IndexModel> log, AdminAuth auth) : base(store)
    {
        _leads = leads;
        _log = log;
        _auth = auth;
    }

    /* ---- حالت فرم تماس بعد از ارسال (برای رندر سمت سرور) ---- */
    public bool Submitted { get; private set; }
    public string? FormError { get; private set; }
    public Dictionary<string, string> FieldErrors { get; } = new();

    [BindProperty] public string Name { get; set; } = "";
    [BindProperty] public string Reach { get; set; } = "";
    [BindProperty] public string Business { get; set; } = "";
    [BindProperty] public string Service { get; set; } = "";
    [BindProperty] public string Need { get; set; } = "";

    /* ---- کپچا ریاضی ---- */
    [BindProperty(Name = "captcha_answer")] public string CaptchaAnswer { get; set; } = "";
    [BindProperty(Name = "captcha_token")] public string CaptchaToken { get; set; } = "";
    public string CaptchaQuestion { get; private set; } = "";

    /// <summary>سناریوهای دمو به‌صورت JSON، برای جاوااسکریپت صفحه.</summary>
    public string DemoJson =>
        C["demo"]["scenarios"].Node?.ToJsonString(new System.Text.Json.JsonSerializerOptions
        {
            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        }) ?? "[]";

    public void OnGet() 
    { 
        // Restore success state from TempData
        if (TempData["contact_submitted"] is string submitted && submitted == "true")
        {
            Submitted = true;
            TempData.Remove("contact_submitted");
        }

        // Restore errors from TempData (PRG pattern)
        if (TempData["contact_errors"] is string errorsJson)
        {
            try
            {
                var errors = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, string>>(errorsJson);
                if (errors != null)
                {
                    foreach (var err in errors)
                    {
                        FieldErrors[err.Key] = err.Value;
                    }
                }
                // Keep TempData for this request and the next (so refresh doesn't lose data)
                TempData.Keep("contact_errors");
            }
            catch { }
        }

        // Restore form values from TempData
        if (TempData["contact_name"] is string name) Name = name;
        if (TempData["contact_reach"] is string reach) Reach = reach;
        if (TempData["contact_business"] is string business) Business = business;
        if (TempData["contact_service"] is string service) Service = service;
        if (TempData["contact_need"] is string need) Need = need;

        GenerateCaptcha();
    }

    /// <summary>
    /// ثبت لید. عمداً بدون احراز هویت است (هر بازدیدکننده باید بتواند فرم را
    /// بفرستد)، ولی اعتبارسنجی همان قوانین سمت کلاینت را این‌جا هم تکرار
    /// می‌کند — چون کلاینت را نمی‌شود امن دانست.
    /// </summary>
    public async Task<IActionResult> OnPostAsync()
    {
        LoadContent();

        // Validate CAPTCHA first
        if (!ValidateCaptcha(CaptchaAnswer, CaptchaToken))
        {
            FieldErrors["captcha"] = "کپچا درست نیست";
            // Store errors and values in TempData for PRG redirect
            StoreErrorsInTempData();
            return RedirectToPage();
        }

        var name = (Name ?? "").Trim();
        var reach = (Reach ?? "").Trim();
        var business = (Business ?? "").Trim();
        var service = (Service ?? "").Trim();
        var need = (Need ?? "").Trim();

        var errors = C["copy"]["contact"]["errors"];
        if (name.Length < 2) FieldErrors["name"] = errors["name"].S;
        if (reach.Length < 5) FieldErrors["reach"] = errors["reach"].S;
        if (need.Length < 10) FieldErrors["need"] = errors["need"].S;

        if (FieldErrors.Count > 0)
        {
            // Store errors and values in TempData for PRG redirect
            StoreErrorsInTempData();
            return RedirectToPage();
        }

        try
        {
            await _leads.AddAsync(name, reach, business, service, need);
            // Redirect to GET to show success - completes PRG pattern
            TempData["contact_submitted"] = "true";
            return RedirectToPage();
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "[leads] ثبت نشد");
            FormError = C["copy"]["contact"]["errorNote"].S;
            StoreErrorsInTempData();
            return RedirectToPage();
        }
    }

    private void StoreErrorsInTempData()
    {
        if (FieldErrors.Count > 0)
        {
            TempData["contact_errors"] = System.Text.Json.JsonSerializer.Serialize(FieldErrors);
        }
        TempData["contact_name"] = Name;
        TempData["contact_reach"] = Reach;
        TempData["contact_business"] = Business;
        TempData["contact_service"] = Service;
        TempData["contact_need"] = Need;
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
        var sig = SignWithSecret(payloadBytes, Encoding.UTF8.GetBytes(_auth.Secret!));
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

        var wantSig = SignWithSecret(payloadBytes, Encoding.UTF8.GetBytes(_auth.Secret!));
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
