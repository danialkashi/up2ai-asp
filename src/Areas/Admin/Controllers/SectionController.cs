using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Up2Ai.Areas.Admin.Models;
using Up2Ai.Services;

namespace Up2Ai.Areas.Admin.Controllers;

[Area("Admin")]
[Authorize]
public class SectionController : Controller
{
    private readonly ContentStore _store;
    private readonly AdminUserStore _users;

    public SectionController(ContentStore store, AdminUserStore users)
    {
        _store = store;
        _users = users;
    }

    private Cv LoadViewData()
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

        return content;
    }

    /// <summary>Find canonical section key (case-insensitive)</summary>
    private string? CanonicalKey(string section)
    {
        var defaults = _store.Defaults as JsonObject;
        if (defaults is null)
            return null;

        return defaults.Select(p => p.Key)
            .FirstOrDefault(k => string.Equals(k, section, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Edit a content section</summary>
    [HttpGet("/admin/section/{section}")]
    public IActionResult Index(string section)
    {
        var content = LoadViewData();

        // Redirect to canonical section name if case differs
        var canonical = CanonicalKey(section);
        if (canonical is not null && canonical != section)
            return RedirectToAction("Index", new { section = canonical });

        var defaults = _store.Defaults as JsonObject;
        var storeContent = _store.Get() as JsonObject;

        if (defaults is null || !defaults.ContainsKey(section))
            return NotFound();

        var model = new SectionEditViewModel
        {
            C = content,
            SectionKey = section,
            Meta = AdminLabels.Sections.TryGetValue(section, out var m) ? m : new AdminLabels.SectionMeta(section, ""),
            Defaults = defaults[section]!.DeepClone(),
            Working = storeContent?[section]?.DeepClone() ?? defaults[section]!.DeepClone(),
        };

        if (TempData["saved"] is string saved)
            model.Saved = saved;

        if (TempData["error"] is string error)
            model.Error = error;

        if (TempData["rejected"] is string rejected && rejected.Length > 0)
            model.Rejected = rejected.Split('|').ToList();

        return View(model);
    }

    /// <summary>Save section changes</summary>
    [HttpPost("section/{section}")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Save(string section, [FromForm] IFormCollection form)
    {
        LoadViewData();

        var canonical = CanonicalKey(section);
        if (canonical is not null && canonical != section)
            return RedirectToAction("Index", new { section = canonical });

        var defaults = _store.Defaults as JsonObject;
        if (defaults is null || !defaults.ContainsKey(section))
            return NotFound();

        try
        {
            // Rebuild working value from form
            var working = RebuildFromForm(defaults[section]!, section, form);

            // Validate section-specific fields
            var validation = ValidateSection(section, working);
            if (validation.HasErrors)
            {
                TempData["rejected"] = string.Join("|", validation.Errors);
                return RedirectToAction("Index", new { section });
            }

            // Save - need to merge into full content object
            var current = _store.Get() as JsonObject;
            if (current is null)
            {
                // If no content exists, start with defaults
                current = (JsonObject)defaults.DeepClone();
            }
            
            current[section] = working;
            _store.Save(current);

            TempData["saved"] = $"Section '{section}' saved.";
            return RedirectToAction("Index", new { section });
        }
        catch (Exception ex)
        {
            TempData["error"] = $"خطا در ذخیره: {ex.Message}";
            return RedirectToAction("Index", new { section });
        }
    }

    private (bool HasErrors, List<string> Errors) ValidateSection(string section, JsonNode? working)
    {
        var errors = new List<string>();

        if (section == "contact" && working is JsonObject contact)
        {
            // Validate WhatsApp number: if provided, must start with 98
            if (contact.TryGetPropertyValue("whatsapp", out var wp) && 
                wp is JsonValue wpVal && wpVal.TryGetValue<string>(out var whatsapp))
            {
                whatsapp = whatsapp?.Trim() ?? "";
                if (!string.IsNullOrEmpty(whatsapp) && !whatsapp.StartsWith("98"))
                {
                    errors.Add("f:contact.whatsapp");
                }
            }
        }

        return (!errors.Any(), errors);
    }

    /// <summary>List operation (add/remove/reorder items)</summary>
    [HttpPost("section/{section}/list")]
    [ValidateAntiForgeryToken]
    public IActionResult List(string section, string op, string path, int index)
    {
        var content = LoadViewData();

        var defaults = _store.Defaults as JsonObject;
        if (defaults is null || !defaults.ContainsKey(section))
            return NotFound();

        var model = new SectionEditViewModel
        {
            C = content,
            SectionKey = section,
            Meta = AdminLabels.Sections.TryGetValue(section, out var m) ? m : new AdminLabels.SectionMeta(section, ""),
            Defaults = defaults[section]!.DeepClone(),
            Working = _store.Get() is JsonObject storeContent ? storeContent[section]!.DeepClone() : defaults[section]!.DeepClone(),
        };

        var arr = Resolve(model.Working, path) as JsonArray;
        var defArr = Resolve(model.Defaults, path) as JsonArray;

        if (arr is not null)
        {
            switch (op)
            {
                case "add":
                    var template = (defArr is { Count: > 0 } ? defArr[0] : null)
                        ?? (arr.Count > 0 ? arr[0] : null)
                        ?? AdminLabels.TemplateFor(path[(path.LastIndexOf('.') + 1)..]);

                    if (template is not null)
                    {
                        var fresh = BlankLike(template);
                        arr.Add(fresh);
                        model.NewItemPath = $"{path}.{arr.Count - 1}";
                        model.NewItemListLabel = AdminLabels.LabelFor(path[(path.LastIndexOf('.') + 1)..]);
                    }
                    break;

                case "remove":
                    if (index >= 0 && index < arr.Count)
                        arr.RemoveAt(index);
                    break;

                case "up":
                    if (index > 0 && index < arr.Count)
                        Swap(arr, index, index - 1);
                    break;

                case "down":
                    if (index >= 0 && index < arr.Count - 1)
                        Swap(arr, index, index + 1);
                    break;
            }
        }

        return View("Index", model);
    }

    private static void Swap(JsonArray arr, int i, int j)
    {
        var x = arr[i]?.DeepClone();
        var y = arr[j]?.DeepClone();
        arr[i] = y;
        arr[j] = x;
    }

    private static JsonNode? Resolve(JsonNode? node, string path)
    {
        var parts = path.Split('.');
        foreach (var part in parts)
        {
            if (node is JsonObject obj && obj.TryGetPropertyValue(part, out var value))
                node = value;
            else if (node is JsonArray arr && int.TryParse(part, out var idx) && idx >= 0 && idx < arr.Count)
                node = arr[idx];
            else
                return null;
        }
        return node;
    }

    private static JsonNode BlankLike(JsonNode template)
    {
        return template switch
        {
            JsonObject obj => new JsonObject(obj.Select(p => new KeyValuePair<string, JsonNode?>(p.Key, BlankLike(p.Value!)))),
            JsonArray arr => new JsonArray(),
            JsonValue v => v.TryGetValue<string>(out _) ? JsonValue.Create("") : v.DeepClone(),
            _ => template.DeepClone()
        };
    }

    private static JsonNode RebuildFromForm(JsonNode shape, string path, IFormCollection form)
    {
        return Build(shape, path, form) ?? shape.DeepClone();
    }

    private static JsonNode? Build(JsonNode? shape, string path, IFormCollection form)
    {
        return shape switch
        {
            JsonObject obj => new JsonObject(
                obj.Select(p => new KeyValuePair<string, JsonNode?>(p.Key, Build(p.Value, $"{path}.{p.Key}", form)))
            ),
            JsonArray arr => BuildArray(arr, path, form),
            JsonValue v => BuildValue(v, path, form),
            _ => shape?.DeepClone()
        };
    }

    private static JsonArray BuildArray(JsonArray arr, string path, IFormCollection form)
    {
        var outArr = new JsonArray();
        var count = 0;

        if (int.TryParse(form[$"n:{path}"].ToString(), out var n))
            count = Math.Clamp(n, 0, 200);

        for (var i = 0; i < count; i++)
        {
            var itemShape = i < arr.Count ? arr[i] : (arr.Count > 0 ? arr[0] : null);
            if (itemShape is null) continue;
            var built = Build(itemShape, $"{path}.{i}", form);
            if (built is not null)
                outArr.Add(built);
        }

        return outArr;
    }

    private static JsonValue BuildValue(JsonValue v, string path, IFormCollection form)
    {
        var raw = form[$"f:{path}"].ToString();

        if (v.TryGetValue<string>(out _))
            return JsonValue.Create(raw.Length > 0 ? raw : v.GetValue<string>());

        if (v.TryGetValue<bool>(out var b))
            return JsonValue.Create(raw.Length > 0 ? raw is "true" or "on" : b);

        if (v.TryGetValue<double>(out var d))
            return JsonValue.Create(double.TryParse(raw, out var parsed) ? parsed : d);

        return v;
    }
}
