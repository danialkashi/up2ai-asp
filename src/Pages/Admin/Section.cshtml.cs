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

    /// <summary>
    /// مسیر موردی که همین الان اضافه شد — تا ویو بتواند رویش تأکید کند و
    /// لینک «برو به موردِ تازه» بدهد. بدون این، مورد تازه به انتهای فهرست
    /// می‌رفت و کاربر که وسط صفحه ایستاده بود فکر می‌کرد چیزی اضافه نشده
    /// یا جای اشتباهی اضافه شده.
    /// </summary>
    public string? NewItemPath { get; private set; }

    /// <summary>نام فارسی فهرستی که موردِ تازه به آن اضافه شد.</summary>
    public string? NewItemListLabel { get; private set; }

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
                    var template = (defArr is { Count: > 0 } ? defArr[0] : null)
                        ?? (arr.Count > 0 ? arr[0] : null)
                        ?? AdminLabels.TemplateFor(path[(path.LastIndexOf('.') + 1)..]);
                    if (template is not null)
                    {
                        var fresh = BlankLike(template);
                        EnsureUniqueId(fresh, arr);
                        arr.Add(fresh);
                        NewItemPath = $"{path}.{arr.Count - 1}";
                        NewItemListLabel = AdminLabels.LabelFor(path[(path.LastIndexOf('.') + 1)..]);
                    }
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
                // فهرستی که پیش‌فرضش خالی است هم باید بتواند آیتم بگیرد،
                // وگرنه آیتمِ تازه سرِ ذخیره بی‌صدا حذف می‌شود.
                var template = arr.Count > 0
                    ? arr[0]
                    : AdminLabels.TemplateFor(path[(path.LastIndexOf('.') + 1)..]);
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

                // فیلدهای قفل‌شده (شناسه، نوع پیش‌نمایش، آیکون، فرستنده) از پنل
                // ویرایش نمی‌شوند ولی باید در رفت‌وبرگشتِ فرم *بمانند* — وگرنه
                // موردی که تازه اضافه شده شناسه‌اش را سر ذخیره از دست می‌دهد و
                // با آیتم اول یکی می‌شود. پس مقدارِ فرم فقط وقتی پذیرفته می‌شود
                // که از فهرست مقدارهای شناخته‌شده باشد؛ هر چیز دیگری نادیده
                // گرفته می‌شود و پیش‌فرض سر جایش می‌ماند.
                if (AdminLabels.LockedKeys.Contains(key))
                {
                    var locked = Request.Form[$"f:{path}"].ToString();
                    return locked.Length > 0 && IsAllowedLocked(key, locked)
                        ? JsonValue.Create(locked)
                        : v.DeepClone();
                }

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

    /* --------------------------- فیلدهای قفل‌شده --------------------------- */

    /// <summary>
    /// مقدارهای مجازِ هر فیلد قفل‌شده، از روی خودِ محتوای پیش‌فرض جمع می‌شود.
    /// `preview` و `icon` فقط چند مقدار مشخص را می‌پذیرند و هر چیز دیگری آن
    /// کارت را بی‌تصویر می‌کند، پس فهرست مجاز باید از داده بیاید نه از حدس.
    /// </summary>
    private Dictionary<string, HashSet<string>>? _lockedVocab;

    private bool IsAllowedLocked(string key, string value)
    {
        // شناسه فقط باید یکتا و بی‌خطر باشد، نه از فهرستی از پیش تعیین‌شده —
        // چون آیتم تازه ذاتاً شناسه‌ی تازه می‌خواهد.
        if (key == "id")
            return value.Length <= 48 && value.All(ch => char.IsAsciiLetterOrDigit(ch) || ch is '-' or '_' or '.');

        if (_lockedVocab is null)
        {
            _lockedVocab = new Dictionary<string, HashSet<string>>();
            foreach (var k in AdminLabels.LockedKeys)
                _lockedVocab[k] = new HashSet<string>(StringComparer.Ordinal);
            CollectLocked(Store.Defaults, _lockedVocab);
        }

        return _lockedVocab.TryGetValue(key, out var set) && set.Contains(value);
    }

    private static void CollectLocked(JsonNode? node, Dictionary<string, HashSet<string>> into)
    {
        switch (node)
        {
            case JsonObject o:
                foreach (var pair in o)
                {
                    if (into.TryGetValue(pair.Key, out var set)
                        && pair.Value is JsonValue jv && jv.TryGetValue<string>(out var s))
                        set.Add(s);
                    CollectLocked(pair.Value, into);
                }
                break;
            case JsonArray a:
                foreach (var item in a) CollectLocked(item, into);
                break;
        }
    }

    /// <summary>
    /// به آیتم تازه یک شناسه‌ی یکتا می‌دهد. بدون این، آیتم تازه شناسه‌ی الگو را
    /// می‌گرفت و در دموی ایجنت دو تب با یک شناسه ساخته می‌شد — که یعنی کلیک روی
    /// تب دوم پنل اول را باز می‌کرد.
    /// </summary>
    private static void EnsureUniqueId(JsonNode? fresh, JsonArray siblings)
    {
        if (fresh is not JsonObject o
            || !o.TryGetPropertyValue("id", out var idNode)
            || idNode is not JsonValue idVal
            || !idVal.TryGetValue<string>(out var stem)) return;

        var taken = siblings.OfType<JsonObject>()
            .Select(s => s.TryGetPropertyValue("id", out var n) && n is JsonValue v
                         && v.TryGetValue<string>(out var sv) ? sv : "")
            .ToHashSet(StringComparer.Ordinal);

        if (stem.Length == 0) stem = "item";
        var candidate = stem;
        var i = 2;
        while (taken.Contains(candidate)) candidate = $"{stem}-{i++}";
        o["id"] = JsonValue.Create(candidate);
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
        {
            // فیلدهای قفل‌شده خالی نمی‌شوند: خالی‌شان کردن یعنی کارتِ تازه
            // بی‌تصویر و بی‌شناسه رندر شود. مقدار الگو را می‌گیرند و شناسه
            // بعداً یکتا می‌شود.
            outObj[pair.Key] = pair.Value is null ? null
                : AdminLabels.LockedKeys.Contains(pair.Key) ? pair.Value.DeepClone()
                : BlankLike(pair.Value);
        }
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
