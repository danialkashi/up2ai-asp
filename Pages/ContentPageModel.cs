using Microsoft.AspNetCore.Mvc.RazorPages;
using Up2Ai.Services;

namespace Up2Ai.Pages;

/// <summary>
/// پایه‌ی هر صفحه‌ای که به محتوای سایت نیاز دارد.
///
/// محتوا در هر درخواست از انبار خوانده می‌شود (که خودش با کنترل زمان تغییر
/// فایل کش می‌کند)، پس ویرایش‌های پنل بلافاصله روی سایت دیده می‌شوند — بدون
/// بیلد دوباره. همین رفتار بود که در نسخه‌ی قبلی هم انتخاب شده بود.
/// </summary>
public abstract class ContentPageModel : PageModel
{
    protected readonly ContentStore Store;

    protected ContentPageModel(ContentStore store) => Store = store;

    public Cv C { get; private set; }

    protected void LoadContent()
    {
        C = new Cv(Store.Get());
        ViewData["Content"] = C;
    }

    public override void OnPageHandlerExecuting(Microsoft.AspNetCore.Mvc.Filters.PageHandlerExecutingContext context)
    {
        LoadContent();
        base.OnPageHandlerExecuting(context);
    }
}
