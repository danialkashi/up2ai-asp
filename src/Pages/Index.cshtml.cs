using Microsoft.AspNetCore.Mvc;
using Up2Ai.Services;

namespace Up2Ai.Pages;

public class IndexModel : ContentPageModel
{
    private readonly LeadStore _leads;
    private readonly FormGuard _guard;
    private readonly IConfiguration _config;
    private readonly ILogger<IndexModel> _log;

    public IndexModel(ContentStore store, LeadStore leads, FormGuard guard,
                      IConfiguration config, ILogger<IndexModel> log) : base(store)
    {
        _leads = leads;
        _guard = guard;
        _config = config;
        _log = log;
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

    /* ---- کپچا و ضدّاسپم ---- */
    [BindProperty(Name = FormGuard.TokenField)] public string CaptchaToken { get; set; } = "";
    [BindProperty(Name = FormGuard.AnswerField)] public string CaptchaAnswer { get; set; } = "";
    [BindProperty(Name = FormGuard.HoneypotField)] public string Honeypot { get; set; } = "";
    [BindProperty(Name = FormGuard.StartedField)] public string StartedStamp { get; set; } = "";

    /// <summary>پرسشِ کپچای همین رندر. هر بار که فرم نشان داده می‌شود تازه ساخته می‌شود.</summary>
    public FormGuard.Challenge Captcha { get; private set; } = null!;

    /// <summary>مهرِ زمانِ باز شدن فرم — برای تشخیصِ پرشدنِ غیرانسانیِ سریع.</summary>
    public string FormStamp { get; private set; } = "";

    /// <summary>یک کپچای تازه برای رندرِ بعدی. بعد از هر POST هم لازم است، چون توکن قبلی مصرف شده.</summary>
    private void FreshCaptcha()
    {
        Captcha = _guard.NewChallenge();
        FormStamp = _guard.StampNow();
        // مقدارِ قبلی نباید در فیلد بماند: پرسش عوض شده و پاسخ قبلی دیگر بی‌معناست.
        CaptchaAnswer = "";
    }

    /// <summary>
    /// IP کاربر برای شمارش — از همان تعریفی که سدِّ ورودِ پنل هم استفاده
    /// می‌کند (<see cref="Up2Ai.Services.ClientIp"/>)، تا این دو هیچ‌وقت دو
    /// برداشتِ متفاوت از «کاربر» نداشته باشند.
    /// </summary>
    private string RequestIp() => Up2Ai.Services.ClientIp.Of(HttpContext, _config);

    /// <summary>سناریوهای دمو به‌صورت JSON، برای جاوااسکریپت صفحه.</summary>
    public string DemoJson =>
        C["demo"]["scenarios"].Node?.ToJsonString(new System.Text.Json.JsonSerializerOptions
        {
            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        }) ?? "[]";

    // کلیدهای TempData برای الگوی PRG (پایین توضیح داده شده).
    private const string SentKey = "lead.sent";

    public void OnGet()
    {
        FreshCaptcha();

        // اگر تازه از یک ارسالِ موفق برگشته‌ایم، حالت موفقیت را نشان بده.
        if (TempData[SentKey] is not string blob) return;
        Submitted = true;
        // مقدارها فقط برای ساختنِ پیامِ آماده‌ی واتساپ لازم‌اند.
        var parts = blob.Split('\u001f');
        if (parts.Length == 5)
        {
            Name = parts[0]; Reach = parts[1]; Business = parts[2];
            Service = parts[3]; Need = parts[4];
        }
    }

    /// <summary>
    /// ثبت لید. عمداً بدون احراز هویت است (هر بازدیدکننده باید بتواند فرم را
    /// بفرستد)، ولی اعتبارسنجی همان قوانین سمت کلاینت را این‌جا هم تکرار
    /// می‌کند — چون کلاینت را نمی‌شود امن دانست.
    ///
    /// ترتیبِ بررسی‌ها عمدی است: اول لایه‌های ضدّربات که ارزان‌اند و به دیسک
    /// دست نمی‌زنند، بعد اعتبارسنجیِ فیلدها، و آخر نوشتن.
    /// </summary>
    public async Task<IActionResult> OnPostAsync()
    {
        LoadContent();

        var name = (Name ?? "").Trim();
        var reach = (Reach ?? "").Trim();
        var business = (Business ?? "").Trim();
        var service = (Service ?? "").Trim();
        var need = (Need ?? "").Trim();

        var cc = C["copy"]["contact"];
        var errors = cc["errors"];
        var ip = RequestIp();

        // ── لایه‌ی ۱: تله و کفِ زمان ─────────────────────────────────────────
        // هیچ‌کدام پیامِ اختصاصی نمی‌گیرند: به رباتی که تله را پر کرده نباید
        // گفت کدام فیلد لوش داد. همان خطای عمومیِ فرم کافی است.
        if (FormGuard.HoneypotTripped(Honeypot) || _guard.FilledTooFast(StartedStamp))
        {
            _log.LogInformation("[leads] ارسالِ مشکوک رد شد (تله/زمان) از {Ip}", ip);
            FormError = errors["spam"].S;
            FreshCaptcha();
            return Page();
        }

        // ── لایه‌ی ۲: سقفِ ارسال از یک IP ───────────────────────────────────
        if (!_guard.WithinSendLimit(ip))
        {
            _log.LogInformation("[leads] سقفِ ارسال پر شد برای {Ip}", ip);
            FormError = errors["tooMany"].S;
            FreshCaptcha();
            return Page();
        }

        // ── لایه‌ی ۳: کپچا ──────────────────────────────────────────────────
        var captcha = _guard.CheckCaptcha(CaptchaToken, CaptchaAnswer);
        if (captcha != FormGuard.CaptchaResult.Ok)
        {
            FieldErrors["captcha"] = captcha == FormGuard.CaptchaResult.Expired
                ? errors["captchaExpired"].S
                : errors["captcha"].S;
        }

        // ── لایه‌ی ۴: خودِ فیلدها ───────────────────────────────────────────
        if (name.Length < 2) FieldErrors["name"] = errors["name"].S;
        if (reach.Length < 5) FieldErrors["reach"] = errors["reach"].S;
        if (need.Length < 10) FieldErrors["need"] = errors["need"].S;

        if (FieldErrors.Count > 0)
        {
            // کپچا چه درست بوده چه غلط، توکنش مصرف شده — پس همیشه یکی تازه.
            FreshCaptcha();
            return Page();
        }

        try
        {
            await _leads.AddAsync(name, reach, business, service, need);
            _guard.CountSend(ip);

            // POST → Redirect → GET.
            //
            // بدون این، صفحه‌ی «ثبت شد» نتیجه‌ی مستقیمِ یک POST بود؛ یعنی
            // اگر کاربر همان صفحه را رفرش می‌کرد مرورگر فرم را دوباره
            // می‌فرستاد و — چون توکن کپچا یک‌بارمصرف است — روی صفحه‌ی تأیید
            // پیام «این پرسش منقضی شد» می‌دید. حالا رفرش فقط همان صفحه‌ی
            // تأیید را دوباره می‌آورد.
            TempData[SentKey] = string.Join('\u001f', name, reach, business, service, need);
            return RedirectToPage("/Index", pageHandler: null, routeValues: null, fragment: "contact");
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "[leads] ثبت نشد");
            FormError = cc["errorNote"].S;
            FreshCaptcha();
        }

        return Page();
    }
}
