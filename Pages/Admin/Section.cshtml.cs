using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Mvc;
using Up2Ai.Services;

namespace Up2Ai.Pages.Admin;

/// <summary>
/// ویرایشگر عمومی یک بخش از محتوا.
///
/// فرم از روی *شکل پیش‌فرض* ساخته می‌شود، نه از روی یک فهرست دستیِ فیلدها.
/// دلیلش این است که هر وقت به محتوا فیلدی اضافه شود، بدون دست‌زدن به پنل
/// خودش این‌جا ظاهر می‌شود — وگرنه پنل و محتوا خیلی زود از هم می‌افتند.
///
/// ┌────────────────────────────────────────────────────────────────────────┐
/// │ تفاوت با نسخه‌ی ری‌اکت: آن‌جا مقدارِ در حال ویرایش در state مرورگر بود و │
/// │ افزودن/حذف/جابه‌جایی بدون رفت‌وبرگشت به سرور انجام می‌شد. این‌جا همان    │
/// │ نقش را خودِ فیلدهای فرم بازی می‌کنند: هر ورودی به نام مسیرش در JSON     │
/// │ نام‌گذاری شده (`f:copy.hero.title`)، و طول هر فهرست در یک فیلد پنهان    │
/// │ (`n:faq.faqs`) نگه داشته می‌شود. پس سرور می‌تواند در هر POST کل مقدارِ  │
/// │ در حال ویرایش را دقیقاً بازبسازد.                                      │
/// │                                                                        │
/// │ نتیجه‌ی خوبش: پنل بدون جاوااسکریپت هم کامل کار می‌کند. افزودن و حذف و   │
/// │ جابه‌جایی هم — مثل قبل — تا وقتی «ذخیره» نزنی روی دیسک نمی‌نشیند.       │
/// └────────────────────────────────────────────────────────────────────────┘
/// </summary>
public class SectionModel : AdminPageModel
{
    public SectionModel(ContentStore store, AdminAuth auth) : base(store, auth) { }

    public string SectionKey { get; private set; } = "";
    public AdminLabels.SectionMeta Meta { get; private set; } = new("", "");

    /// <summary>مقدارِ در حال ویرایش (ممکن است هنوز ذخیره نشده باشد).</summary>
    public JsonNode Working { get; private set; } = new JsonObject();

    /// <summary>مقدار پیش‌فرض همین بخش — برای نشان دادن «تغییر کرده» و دکمه‌ی «برگردان».</summary>
    public JsonNode Defaults { get; private set; } = new JsonObject();

    public string? Saved { get; private set; }
    public string? Error { get; private set; }
    public List<string> Rejected { get; private set; } = new();

    /// <summary>آیا مقدار فعلی با پیش‌فرض فرق دارد؟ (برای فعال بودن دکمه‌ی بازگشت به پیش‌فرض)</summary>
    public bool ChangedFromDefault => Working.ToJsonString() != Defaults.ToJsonString();

    private bool Load(string section)
    {
        if (Store.Defaults is not JsonObject defs || !defs.ContainsKey(section)) return false;
        SectionKey = section;
        Meta = AdminLabels.Sections.TryGetValue(section, out var m) ? m : new AdminLabels.SectionMeta(section, "");
        Defaults = defs[section]!.DeepClone();
        Working = ((JsonObject)Store.Get())[section]!.DeepClone();
        return true;
    }

    public IActionResult OnGet(string section)
    {
        var guard = RequireAuth();
        if (guard is not null) return guard;
        if (!Load(section)) return NotFound();

        // پیام موفقیت بعد از ری‌دایرکتِ ذخیره
        if (TempData["saved"] is string s) Saved = s;
        if (TempData["rejected"] is string r && r.Length > 0)
            Rejected = r.Split('').ToList();
        return Page();
    }

    /// <summary>ذخیره‌ی بخش. ورودی قبل از نوشتن از ادغام شکل‌محور رد می‌شود.</summary>
    public IActionResult OnPost(string section)
    {
        var guard = RequireAuth();
        if (guard is not null) return guard;
        if (!Load(section)) return NotFound();

        Working = RebuildFromForm();

        try
        {
            var whole = (JsonObject)Store.Get().DeepClone();
            whole[section] = Working.DeepClone();
            var report = Store.Save(whole);

            // فقط ردشده‌های همین بخش را نشان بده
            var mine = report.Rejected
                .Where(p => p == section || p.StartsWith(section + ".") || p.StartsWith(section + "["))
                .ToList();

            TempData["saved"] = Meta.Title;
            TempData["rejected"] = string.Join('', mine);
            return RedirectToPage("/Admin/Section", new { section });
        }
        catch (Exception)
        {
            Error = "ذخیره انجام نشد.";
            return Page();
        }
    }

    /// <summary>بازگشت این بخش به پیش‌فرض.</summary>
    public IActionResult OnPostReset(string section)
    {
        var guard = RequireAuth();
        if (guard is not null) return guard;
        if (!Load(section)) return NotFound();

        var whole = (JsonObject)Store.Get().DeepClone();
        whole[section] = Defaults.DeepClone();
        Store.Save(whole);
        TempData["saved"] = Meta.Title;
        return RedirectToPage("/Admin/Section", new { section });
    }

    /// <summary>
    /// عملیات ساختاری روی فهرست‌ها: افزودن، حذف، بالا، پایین.
    /// هیچ‌کدام ذخیره نمی‌کنند — فقط مقدارِ در حال ویرایش را عوض می‌کنند،
    /// دقیقاً مثل نسخه‌ی ری‌اکت.
    /// </summary>
    public IActionResult OnPostList(string section, string op, string path, int index)
    {
        var guard = RequireAuth();
        if (guard is not null) return guard;
        if (!Load(section)) return NotFound();

        Working = RebuildFromForm();
        var arr = Resolve(Working, path) as JsonArray;
        var defArr = Resolve(Defaults, path) as JsonArray;

        if (arr is not null)
        {
            switch (op)
            {
                case "add":
                    // الگوی آیتم تازه: اولین آیتم پیش‌فرض، وگرنه اولین آیتم فعلی.
                    var template = (defArr is { Count: > 0 } ? defArr[0] : null) ?? (arr.Count > 0 ? arr[0] : null);
                    if (template is not null) arr.Add(BlankLike(template));
                    break;

                case "remove":
                    if (index >= 0 && index < arr.Count) arr.RemoveAt(index);
                    break;

                case "up":
                    if (index > 0 && index < arr.Count) Swap(arr, index, index - 1);
                    break;

                case "down":
                    if (index >= 0 && index < arr.Count - 1) Swap(arr, index, index + 1);
                    break;
            }
        }

        return Page();
    }

    private static void Swap(JsonArray a, int i, int j)
    {
        var x = a[i]?.DeepClone();
        var y = a[j]?.DeepClone();
        a[i] = y;
        a[j] = x;
    }

    /* ------------------------- بازسازی از روی فرم ------------------------- */

    /// <summary>
    /// کل مقدارِ در حال ویرایش را از فیلدهای فرم بازمی‌سازد.
    ///
    /// شکل را *پیش‌فرض* تعیین می‌کند (نه فرم)، پس هیچ کلید ناشناخته‌ای از یک
    /// فرم دست‌کاری‌شده وارد داده نمی‌شود. فقط طول فهرست‌ها از فرم می‌آید،
    /// چون کاربر می‌تواند آیتم اضافه یا کم کند.
    /// </summary>
    private JsonNode RebuildFromForm() => Build(Defaults, SectionKey) ?? Defaults.DeepClone();

    private JsonNode? Build(JsonNode? shape, string path)
    {
        switch (shape)
        {
            case JsonObject obj:
            {
                var outObj = new JsonObject();
                foreach (var pair in obj)
                    outObj[pair.Key] = Build(pair.Value, $"{path}.{pair.Key}");
                return outObj;
            }

            case JsonArray arr:
            {
                var outArr = new JsonArray();
                var template = arr.Count > 0 ? arr[0] : null;
                var count = 0;
                if (int.TryParse(Request.Form[$"n:{path}"].ToString(), out var n)) count = Math.Max(0, n);

                for (var i = 0; i < count; i++)
                {
                    // شکل هر آیتم: پیش‌فرضِ هم‌اندیس اگر بود، وگرنه الگو (اولین آیتم).
                    var itemShape = i < arr.Count ? arr[i] : template;
                    if (itemShape is null) continue;
                    outArr.Add(Build(itemShape, $"{path}.{i}"));
                }
                return outArr;
            }

            case JsonValue v:
            {
                var key = path[(path.LastIndexOf('.') + 1)..];
                // فیلدهای فقط‌خواندنی هرگز از فرم خوانده نمی‌شوند — حتی اگر
                // کسی دستی در HTML بازشان کند، مقدار پیش‌فرض سر جایش می‌ماند.
                if (AdminLabels.ReadOnlyKeys.Contains(key)) return v.DeepClone();

                if (v.TryGetValue<string>(out _))
                {
                    var raw = Request.Form[$"f:{path}"];
                    return JsonValue.Create(raw.Count > 0 ? raw.ToString() : v.GetValue<string>());
                }
                if (v.TryGetValue<bool>(out var b))
                {
                    var raw = Request.Form[$"f:{path}"].ToString();
                    return JsonValue.Create(raw.Length > 0 ? raw is "true" or "on" : b);
                }
                if (v.TryGetValue<double>(out var d))
                {
                    var raw = Request.Form[$"f:{path}"].ToString();
                    return JsonValue.Create(double.TryParse(raw, out var parsed) ? parsed : d);
                }
                return v.DeepClone();
            }

            default:
                return shape?.DeepClone();
        }
    }

    /// <summary>آیتم خالی هم‌شکلِ الگو.</summary>
    private static JsonNode? BlankLike(JsonNode template) => template switch
    {
        JsonObject o => BlankObject(o),
        JsonArray a => a.Count > 0 && a[0] is not null
            ? new JsonArray(BlankLike(a[0]!))
            : new JsonArray(),
        JsonValue v when v.TryGetValue<string>(out _) => JsonValue.Create(""),
        JsonValue v when v.TryGetValue<bool>(out _) => JsonValue.Create(false),
        JsonValue v when v.TryGetValue<double>(out _) => JsonValue.Create(0),
        _ => JsonValue.Create(""),
    };

    private static JsonObject BlankObject(JsonObject o)
    {
        var outObj = new JsonObject();
        foreach (var pair in o)
            outObj[pair.Key] = pair.Value is null ? null : BlankLike(pair.Value);
        return outObj;
    }

    /// <summary>یک مسیر نقطه‌ای («faq.faqs») را داخل درخت پیدا می‌کند.</summary>
    private JsonNode? Resolve(JsonNode root, string path)
    {
        // مسیرها با نام بخش شروع می‌شوند؛ ریشه خودِ همان بخش است.
        var parts = path.Split('.');
        JsonNode? node = root;
        foreach (var part in parts.Skip(1))
        {
            if (node is JsonArray arr && int.TryParse(part, out var i))
                node = i >= 0 && i < arr.Count ? arr[i] : null;
            else if (node is JsonObject obj)
                node = obj.TryGetPropertyValue(part, out var v) ? v : null;
            else
                return null;
        }
        return node;
    }
}
