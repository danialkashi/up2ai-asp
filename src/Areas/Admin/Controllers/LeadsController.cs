using System.Globalization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Up2Ai.Areas.Admin.Models;
using Up2Ai.Services;

namespace Up2Ai.Areas.Admin.Controllers;

[Area("Admin")]
[Authorize]
public class LeadsController : Controller
{
    private readonly ContentStore _store;
    private readonly AdminUserStore _users;
    private readonly LeadStore _leads;

    public LeadsController(ContentStore store, AdminUserStore users,
                           LeadStore leads)
    {
        _store = store;
        _users = users;
        _leads = leads;
    }

    private void LoadViewData()
    {
        var content = new Cv(_store.Get());
        ViewData["Content"] = content;
        ViewData["NoIndex"] = true;
        ViewData["Authed"] = User.Identity?.IsAuthenticated ?? false;

        var userId = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
        var currentUser = userId != null ? _users.ById(userId) : null;
        ViewData["CurrentUserId"] = userId;
        ViewData["CurrentUser"] = currentUser;
        ViewData["UserLabel"] = currentUser?.Label ?? "Admin";
    }

    /// <summary>List all leads (contact form submissions)</summary>
    [HttpGet("/admin/leads")]
    public IActionResult Index(string? filter)
    {
        LoadViewData();

        var all = _leads.List();
        var onlyUnhandled = filter == "unhandled";
        var leads = onlyUnhandled ? all.Where(l => !l.Handled).ToList() : all;

        var model = new LeadsListViewModel
        {
            Filter = filter,
            OnlyUnhandled = onlyUnhandled,
            Leads = leads,
            TotalCount = all.Count,
            UnhandledCount = all.Count(l => !l.Handled)
        };

        return View(model);
    }

    /// <summary>Toggle lead handled status</summary>
    [HttpPost("leads/{id}/toggle")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Toggle(string id, bool handled, string? filter)
    {
        LoadViewData();

        if (!string.IsNullOrEmpty(id))
            await _leads.SetHandledAsync(id, handled);

        return RedirectToAction("Index", new { filter = string.IsNullOrEmpty(filter) ? (string?)null : filter });
    }

    /// <summary>Delete a lead</summary>
    [HttpPost("leads/{id}/delete")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(string id, string? filter)
    {
        LoadViewData();

        if (!string.IsNullOrEmpty(id))
            await _leads.DeleteAsync(id);

        return RedirectToAction("Index", new { filter = string.IsNullOrEmpty(filter) ? (string?)null : filter });
    }

    /// <summary>Export leads as CSV</summary>
    [HttpGet("/admin/leads/export")]
    public IActionResult Export()
    {
        var csv = LeadStore.ToCsv(_leads.List());
        var name = $"up2ai-leads-{DateTime.Now:yyyy-MM-dd}.csv";
        return File(System.Text.Encoding.UTF8.GetBytes(csv), "text/csv; charset=utf-8", name);
    }
}
