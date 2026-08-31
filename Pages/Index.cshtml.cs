using Microsoft.AspNetCore.Mvc;
using Up2Ai.Services;

namespace Up2Ai.Pages;

public class IndexModel : ContentPageModel
{
    private readonly LeadStore _leads;
    private readonly ILogger<IndexModel> _log;

    public IndexModel(ContentStore store, LeadStore leads, ILogger<IndexModel> log) : base(store)
    {
        _leads = leads;
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

    /// <summary>سناریوهای دمو به‌صورت JSON، برای جاوااسکریپت صفحه.</summary>
    public string DemoJson =>
        C["demo"]["scenarios"].Node?.ToJsonString(new System.Text.Json.JsonSerializerOptions
        {
            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        }) ?? "[]";

    public void OnGet() { }

    /// <summary>
    /// ثبت لید. عمداً بدون احراز هویت است (هر بازدیدکننده باید بتواند فرم را
    /// بفرستد)، ولی اعتبارسنجی همان قوانین سمت کلاینت را این‌جا هم تکرار
    /// می‌کند — چون کلاینت را نمی‌شود امن دانست.
    /// </summary>
    public async Task<IActionResult> OnPostAsync()
    {
        LoadContent();

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
            return Page();
        }

        try
        {
            await _leads.AddAsync(name, reach, business, service, need);
            Submitted = true;
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "[leads] ثبت نشد");
            FormError = C["copy"]["contact"]["errorNote"].S;
        }

        return Page();
    }
}
