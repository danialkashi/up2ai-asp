using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Mvc;
using Up2Ai.Services;

namespace Up2Ai.Pages.Admin;

/// <summary>فهرست بخش‌های قابل ویرایش، با نشانه‌ی این‌که کدام از پیش‌فرض فاصله گرفته.</summary>
public class IndexModel : AdminPageModel
{
    private readonly LeadStore _leads;

    public IndexModel(ContentStore store, AdminAuth auth, LeadStore leads)
        : base(store, auth) => _leads = leads;

    public int LeadCount { get; private set; }
    public int UnhandledLeads { get; private set; }
    public List<string> Rejected { get; private set; } = new();
    public List<string> SectionKeys { get; private set; } = new();
    private JsonNode? _content;

    public IActionResult OnGet()
    {
        var guard = RequireAuth();
        if (guard is not null) return guard;

        var (content, report) = Store.GetWithReport();
        _content = content;
        Rejected = report.Rejected;

        SectionKeys = Store.Defaults is JsonObject defs
            ? defs.Select(p => p.Key).ToList()
            : new List<string>();

        var leads = _leads.List();
        LeadCount = leads.Count;
        UnhandledLeads = leads.Count(l => !l.Handled);
        return Page();
    }

    /// <summary>آیا این بخش از پیش‌فرضش فاصله گرفته؟</summary>
    public bool IsEdited(string key)
    {
        var cur = (_content as JsonObject)?[key]?.ToJsonString();
        var def = (Store.Defaults as JsonObject)?[key]?.ToJsonString();
        return cur != def;
    }
}
