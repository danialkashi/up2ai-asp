using System.Text;
using Microsoft.AspNetCore.Mvc;
using Up2Ai.Services;

namespace Up2Ai.Pages.Admin;

/// <summary>
/// خروجی CSV صندوق لید.
///
/// این یک صفحه نیست، یک مسیر دانلود است — ولی باید همان گاردِ ورود را داشته
/// باشد: بدون آن، هر کسی با دانستن آدرس می‌توانست کل لیدها را بردارد.
/// </summary>
public class LeadsExportModel : AdminPageModel
{
    private readonly LeadStore _leads;

    public LeadsExportModel(ContentStore store, AdminAuth auth, LeadStore leads)
        : base(store, auth) => _leads = leads;

    public IActionResult OnGet()
    {
        var guard = RequireAuth();
        if (guard is not null) return guard;

        var csv = LeadStore.ToCsv(_leads.List());
        var name = $"up2ai-leads-{DateTime.Now:yyyy-MM-dd}.csv";
        return File(Encoding.UTF8.GetBytes(csv), "text/csv; charset=utf-8", name);
    }
}
