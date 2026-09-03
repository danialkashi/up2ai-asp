using System.Diagnostics;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Up2Ai.Services;

namespace Up2Ai.Pages;

/// <summary>
/// صفحه‌ی خطای عمومی — تورِ ایمنیِ سایت.
///
/// دو چیز این‌جا حیاتی است و قبلاً هیچ‌کدام نبود:
///
///   ۱) این صفحه *خودش* نباید خطا بدهد. نسخه‌ی قبلی از `PageModel` ساده ارث
///      می‌برد و محتوا را لود نمی‌کرد؛ لایوت هم برای همان حالت آماده بود، ولی
///      پارشالِ JSON-LD مقدار را با کستِ سخت می‌خواند و روی null می‌ترکید.
///      نتیجه: هر خطای کوچکی در هر صفحه‌ای، به‌جای صفحه‌ی خطا یک ۵۰۰ی
///      کاملاً *سفید* می‌داد و متن خطای اصلی هم گم می‌شد.
///      (آزموده شده: `curl -i /Error` → 500 با بدنه‌ی صفر بایت.)
///
///   ۲) `NoIndex` باید ست شود، وگرنه صفحه‌ی خطا کاندید ایندکس شدن در گوگل
///      می‌ماند.
///
/// حالا محتوا لود می‌شود (پس هدر و فوتر و متنِ فارسی درست می‌آید) و اگر خودِ
/// لود محتوا هم خطا بدهد، لایوت و پارشال‌ها با مقدار خالی سالم رندر می‌شوند.
/// </summary>
[ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
[IgnoreAntiforgeryToken]
public class ErrorModel : ContentPageModel
{
    private readonly ILogger<ErrorModel> _logger;

    public ErrorModel(ContentStore store, ILogger<ErrorModel> logger) : base(store) => _logger = logger;

    public string? RequestId { get; private set; }

    public bool ShowRequestId => !string.IsNullOrEmpty(RequestId);

    /// <summary>کد وضعیت اصلی — تا ۴۰۴ متن خودش را بگیرد و ۵۰۰ متن خودش را.</summary>
    /// <remarks>نامش عمداً StatusCode نیست تا متدِ هم‌نامِ PageModel را پنهان نکند.</remarks>
    public int OriginalStatusCode { get; private set; } = 500;

    public bool IsNotFound => OriginalStatusCode == 404;

    /// <summary>
    /// لودِ محتوا برای *این* صفحه هرگز نباید خطا بدهد.
    ///
    /// این صفحه ممکن است دقیقاً به این دلیل باز شده باشد که خواندن محتوا
    /// شکست خورده (فایل پیش‌فرض گم شده، دیسک پر است، JSON خراب است). اگر
    /// این‌جا همان کار را دوباره امتحان کنیم و باز خطا بدهد، کاربر دوباره
    /// همان ۵۰۰ی خالی را می‌گیرد — یعنی تورِ ایمنی خودش پاره است. پس تلاش
    /// می‌کنیم؛ نشد، صفحه با متن‌های جای‌گزینِ خودش رندر می‌شود.
    /// </summary>
    public override void OnPageHandlerExecuting(Microsoft.AspNetCore.Mvc.Filters.PageHandlerExecutingContext context)
    {
        try
        {
            base.OnPageHandlerExecuting(context);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[error] محتوای سایت برای صفحه‌ی خطا هم خوانده نشد");
        }
    }

    public void OnGet()
    {
        ViewData["NoIndex"] = true;
        RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier;

        // وقتی از `UseStatusCodePagesWithReExecute` می‌آییم، کد اصلی این‌جاست.
        var reExecute = HttpContext.Features.Get<IStatusCodeReExecuteFeature>();
        if (reExecute is not null)
        {
            OriginalStatusCode = HttpContext.Response.StatusCode;
            _logger.LogInformation("[error] {Status} برای {Path}", OriginalStatusCode, reExecute.OriginalPath);
        }
        else
        {
            var handler = HttpContext.Features.Get<IExceptionHandlerPathFeature>();
            if (handler?.Error is not null)
                _logger.LogError(handler.Error, "[error] خطای رسیدگی‌نشده در {Path}", handler.Path);
        }
    }
}
