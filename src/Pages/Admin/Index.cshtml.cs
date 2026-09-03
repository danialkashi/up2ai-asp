using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Mvc;
using Up2Ai.Services;

namespace Up2Ai.Pages.Admin;

/// <summary>فهرست بخش‌های قابل ویرایش، با نشانه‌ی این‌که کدام از پیش‌فرض فاصله گرفته.</summary>
public class IndexModel : AdminPageModel
{
    private readonly LeadStore _leads;

    private readonly BlogStore _blog;

    public IndexModel(ContentStore store, AdminAuth auth, AdminUserStore users, LeadStore leads, BlogStore blog)
        : base(store, auth, users)
    {
        _leads = leads;
        _blog = blog;
    }

    /// <summary>شمارشِ وبلاگ برای کارت‌های خانه‌ی پنل.</summary>
    public int PostCount { get; private set; }
    public int DraftCount { get; private set; }
    public int PendingComments { get; private set; }

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

        var posts = _blog.All();
        PostCount = posts.Count;
        DraftCount = posts.Count(p => !p.Published);
        PendingComments = _blog.PendingCount();
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
