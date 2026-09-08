using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Up2Ai.Areas.Admin.Models;
using Up2Ai.Services;

namespace Up2Ai.Areas.Admin.Controllers;

[Area("Admin")]
[Authorize]
public class DashboardController : Controller
{
    private readonly ContentStore _store;
    private readonly AdminUserStore _users;
    private readonly LeadStore _leads;
    private readonly BlogStore _blog;

    public DashboardController(ContentStore store, AdminUserStore users,
                               LeadStore leads, BlogStore blog)
    {
        _store = store;
        _users = users;
        _leads = leads;
        _blog = blog;
    }

    private Cv LoadContent()
    {
        var content = new Cv(_store.Get());
        ViewData["Content"] = content;
        ViewData["NoIndex"] = true;
        ViewData["Authed"] = User.Identity?.IsAuthenticated ?? false;

        // Load current user info
        var userId = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
        var currentUser = userId != null ? _users.ById(userId) : null;

        ViewData["CurrentUserId"] = userId;
        ViewData["CurrentUser"] = currentUser;
        ViewData["UserLabel"] = currentUser?.Label ?? "Admin";

        return content;
    }

    /// <summary>Admin dashboard - overview of site content</summary>
    public IActionResult Index()
    {
        var content = LoadContent();

        var model = new DashboardViewModel
        {
            PostCount = 0,
            DraftCount = 0,
            PendingComments = 0,
            LeadCount = 0,
            UnhandledLeads = 0,
        };

        var (contentNode, report) = _store.GetWithReport();
        model.Rejected = report.Rejected;
        model.C = new Cv(contentNode);

        // Get section keys from defaults
        var defs = _store.Defaults as System.Text.Json.Nodes.JsonObject;
        model.SectionKeys = defs?.Select(p => p.Key).ToList() ?? new List<string>();

        var leads = _leads.List();
        model.LeadCount = leads.Count;
        model.UnhandledLeads = leads.Count(l => !l.Handled);

        var posts = _blog.All();
        model.PostCount = posts.Count;
        model.DraftCount = posts.Count(p => !p.Published);
        model.PendingComments = _blog.PendingCount();

        return View(model);
    }
}
