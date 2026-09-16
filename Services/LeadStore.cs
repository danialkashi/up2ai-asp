using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Up2Ai.Services;

/// <summary>Lead workflow status constants.</summary>
public static class LeadStatus
{
    public const string New = "new";
    public const string Contacted = "contacted";
    public const string FollowUp = "follow_up";
    public const string Done = "done";

    public static readonly string[] All = { New, Contacted, FollowUp, Done };

    public static bool IsValid(string? status) => All.Contains(status);
}

public sealed class Lead
{
    [JsonPropertyName("id")] public string Id { get; set; } = "";
    [JsonPropertyName("at")] public string At { get; set; } = "";           // ISO timestamp (created)
    [JsonPropertyName("name")] public string Name { get; set; } = "";
    [JsonPropertyName("reach")] public string Reach { get; set; } = "";
    [JsonPropertyName("business")] public string Business { get; set; } = "";
    [JsonPropertyName("service")] public string Service { get; set; } = "";
    [JsonPropertyName("need")] public string Need { get; set; } = "";
    
    // New workflow fields - NO DEFAULT VALUES, must be set explicitly
    [JsonPropertyName("status")] public string? Status { get; set; }
    [JsonPropertyName("lastContactedAt")] public string? LastContactedAt { get; set; }
    [JsonPropertyName("internalNotes")] public string? InternalNotes { get; set; }
    
    // Old field: deserializes from "handled" but doesn't serialize back
    [JsonPropertyName("handled")] [JsonIgnore(Condition = JsonIgnoreCondition.WhenWriting)]
    public bool Handled { get; set; }
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
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,  // Write all fields, even if they have default values
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
        // If file doesn't exist, that's normal on first run — return empty list
        if (!File.Exists(File_)) return new List<Lead>();

        try
        {
            var raw = File.ReadAllText(File_);
            var parsed = JsonSerializer.Deserialize<List<Lead>>(raw);
            if (parsed is null)
            {
                _log.LogError("[leads] Failed to deserialize leads.json: deserialization returned null");
                throw new InvalidOperationException("Invalid leads data structure");
            }
            
            // One-time migration: if raw JSON contains "handled" field, migrate to new format
            bool needsRewrite = raw.Contains("\"handled\"");
            
            foreach (var lead in parsed)
            {
                // Migrate: handled → status (only if status is empty/null)
                if (string.IsNullOrEmpty(lead.Status))
                {
                    lead.Status = lead.Handled ? LeadStatus.Done : LeadStatus.New;
                    needsRewrite = true;
                }
            }
            
            // Persist migration (one-time operation per file)
            if (needsRewrite)
            {
                WriteAllUnlocked(parsed);
                _log.LogInformation("[leads] Migrated {Count} leads to new workflow format", parsed.Count);
            }
            
            // Validate and filter
            return parsed.Where(l =>
            {
                if (IsValid(l)) return true;
                _log.LogWarning("[leads] Skipping invalid lead record: {LeadId}", l?.Id ?? "<null>");
                return false;
            }).ToList();
        }
        catch (JsonException ex)
        {
            _log.LogError(ex, "[leads] Failed to parse leads.json: invalid JSON format");
            throw new InvalidOperationException("Lead store data is corrupted", ex);
        }
        catch (IOException ex)
        {
            _log.LogError(ex, "[leads] I/O error reading leads.json");
            throw new InvalidOperationException("Unable to read lead store", ex);
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
                Status = LeadStatus.New,
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

    /// <summary>تغییر وضعیت لید.</summary>
    public Task<bool> SetStatusAsync(string id, string status) =>
        WithLockAsync(() =>
        {
            if (!LeadStatus.IsValid(status)) return false;
            var leads = ReadAllUnlocked();
            var lead = leads.FirstOrDefault(l => l.Id == id);
            if (lead is null) return false;
            lead.Status = status;
            WriteAllUnlocked(leads);
            return true;
        });

    /// <summary>علامت‌گذاری کردن «تماس گرفته شد» — به‌روز رسانی زمان آخرین تماس.</summary>
    public Task<bool> SetContactedAsync(string id) =>
        WithLockAsync(() =>
        {
            var leads = ReadAllUnlocked();
            var lead = leads.FirstOrDefault(l => l.Id == id);
            if (lead is null) return false;
            lead.LastContactedAt = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffZ", CultureInfo.InvariantCulture);
            WriteAllUnlocked(leads);
            return true;
        });

    /// <summary>ویرایش یادداشت‌های داخلی برای یک لید.</summary>
    public Task<bool> SetNotesAsync(string id, string? notes) =>
        WithLockAsync(() =>
        {
            var leads = ReadAllUnlocked();
            var lead = leads.FirstOrDefault(l => l.Id == id);
            if (lead is null) return false;
            
            // Limit notes to reasonable size
            var maxNotes = 5000;
            if (!string.IsNullOrEmpty(notes) && notes.Length > maxNotes)
                notes = notes.Substring(0, maxNotes);
            
            lead.InternalNotes = string.IsNullOrWhiteSpace(notes) ? null : notes.Trim();
            WriteAllUnlocked(leads);
            return true;
        });

    /// <summary>حذف یک لید — فقط از پنل مدیریت (مثلاً برای ورودی‌های آزمایشی/اسپم).</summary>
    public Task<bool> DeleteAsync(string id) =>
        WithLockAsync(() =>
        {
            var leads = ReadAllUnlocked();
            var next = leads.Where(l => l.Id != id).ToList();
            if (next.Count == leads.Count) return false;
            WriteAllUnlocked(next);
            return true;
        });

    /// <summary>خروجی CSV — با BOM تا اکسل فارسی را درست نشان بدهد.</summary>
    public static string ToCsv(IEnumerable<Lead> leads)
    {
        var fa = new CultureInfo("fa-IR");
        string Esc(string s) => "\"" + s.Replace("\"", "\"\"") + "\"";

        var headers = new[] { "تاریخ", "نام", "راه ارتباطی", "کسب‌وکار", "حوزه", "نیاز", "وضعیت", "آخرین تماس", "یادداشت" };
        var lines = new List<string> { string.Join(",", headers.Select(Esc)) };

        foreach (var l in leads)
        {
            var when = DateTime.TryParse(l.At, CultureInfo.InvariantCulture,
                DateTimeStyles.RoundtripKind, out var dt)
                ? dt.ToLocalTime().ToString(fa)
                : l.At;
            
            var lastContacted = string.IsNullOrEmpty(l.LastContactedAt) 
                ? "" 
                : (DateTime.TryParse(l.LastContactedAt, CultureInfo.InvariantCulture,
                    DateTimeStyles.RoundtripKind, out var ldt)
                    ? ldt.ToLocalTime().ToString(fa)
                    : l.LastContactedAt);
            
            lines.Add(string.Join(",", new[]
            {
                when, l.Name, l.Reach, l.Business, l.Service, l.Need, 
                StatusToPersian(l.Status), lastContacted, l.InternalNotes ?? "",
            }.Select(Esc)));
        }

        return "﻿" + string.Join("\r\n", lines);
    }

    private static string StatusToPersian(string status) => status switch
    {
        LeadStatus.New => "جدید",
        LeadStatus.Contacted => "تماس گرفته شد",
        LeadStatus.FollowUp => "پیگیری مورد نیاز",
        LeadStatus.Done => "انجام شده",
        _ => status,
    };
}
