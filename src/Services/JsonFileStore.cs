using System.Collections.Concurrent;
using System.Text.Json;

namespace Up2Ai.Services;

/// <summary>
/// یک فهرست از رکوردها که در یک فایل JSON نگه داشته می‌شود.
///
/// چرا این کلاس هست: <see cref="LeadStore"/> همین کار را برای لیدها می‌کرد و
/// حالا سه فهرستِ دیگر هم داریم (پست‌ها، نظرها، کاربران پنل). به‌جای چهار
/// نسخه‌ی کپی‌شده از منطقِ «قفل بگیر، بخوان، عوض کن، اتمی بنویس»، همه‌ی آن
/// منطق یک جا نشسته و هر انبار فقط می‌گوید چه نوعی و کدام فایل.
///
/// همان دو قفلِ LeadStore این‌جا هم هست، چون هر دو مسئله واقعی‌اند:
///
///   • قفلِ نخی (SemaphoreSlim) برای درخواست‌های هم‌زمانِ *همین* پردازه —
///     ASP.NET درخواست‌ها را موازی اجرا می‌کند و بدون این، دو نوشتنِ هم‌زمان
///     یکی‌شان را بی‌صدا دور می‌ریخت.
///   • قفلِ فایلی (FileShare.None) برای *چند پردازه* — مثلاً وقتی سایت با
///     دو نمونه بالا آمده یا هنگام استقرارِ بدونِ قطعی، دو نسخه هم‌زمان
///     زنده‌اند.
///
/// نوشتن هم اتمی است: اول در فایل موقت، بعد Move. اگر برق وسطِ کار برود،
/// فایل اصلی یا کاملِ قبلی است یا کاملِ جدید — هیچ‌وقت نیمه‌نوشته.
///
/// حدِّ این کلاس را هم صادقانه بگوییم: کلِ فهرست را در حافظه می‌خواند و
/// می‌نویسد. برای چند صد پست و چند هزار نظر (یعنی چیزی که این سایت واقعاً
/// می‌بیند) کاملاً بی‌مسئله است. اگر روزی حجم داده از این بیشتر شد، جای
/// درستِ عوض کردن همین یک کلاس است و نه صفحه‌ها — انبارها فقط از همین API
/// استفاده می‌کنند.
/// </summary>
public sealed class JsonFileStore<T> where T : class
{
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> Gates = new(StringComparer.Ordinal);

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    private readonly string _dataDir;
    private readonly string _fileName;
    private readonly Func<T, bool> _isValid;
    private readonly ILogger _log;

    /// <param name="isValid">
    /// رکوردِ خراب (مثلاً فایلی که دستی ویرایش شده) نباید کل فهرست را از کار
    /// بیندازد؛ فقط خودش کنار گذاشته می‌شود.
    /// </param>
    public JsonFileStore(string dataDir, string fileName, Func<T, bool> isValid, ILogger log)
    {
        _dataDir = dataDir;
        _fileName = fileName;
        _isValid = isValid;
        _log = log;
    }

    private string Path_ => Path.Combine(_dataDir, _fileName);

    private SemaphoreSlim Gate => Gates.GetOrAdd(Path_, _ => new SemaphoreSlim(1, 1));

    /// <summary>خواندنِ بدون قفل. برای نمایش کافی است.</summary>
    public List<T> Read()
    {
        try
        {
            if (!File.Exists(Path_)) return new List<T>();
            var parsed = JsonSerializer.Deserialize<List<T>>(File.ReadAllText(Path_));
            if (parsed is null) return new List<T>();
            return parsed.Where(x => x is not null && _isValid(x)).ToList();
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "[store] فایل {File} خوانده نشد", _fileName);
            return new List<T>();
        }
    }

    private void WriteUnlocked(List<T> items)
    {
        Directory.CreateDirectory(_dataDir);
        var tmp = $"{Path_}.{Environment.ProcessId}.tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(items, JsonOpts));
        File.Move(tmp, Path_, overwrite: true);
    }

    /// <summary>
    /// فهرست را زیر قفل می‌خواند، به <paramref name="body"/> می‌دهد، و اگر
    /// آن تابع <c>true</c> برگرداند نتیجه را می‌نویسد.
    ///
    /// نوشتنِ مشروط عمدی است: عملیاتی مثل «حذفِ چیزی که وجود ندارد» نباید
    /// فایل را بی‌دلیل بازنویسی کند.
    /// </summary>
    public async Task<TResult> MutateAsync<TResult>(Func<List<T>, (bool Save, TResult Result)> body)
    {
        var gate = Gate;
        await gate.WaitAsync();
        try
        {
            Directory.CreateDirectory(_dataDir);
            var lockPath = Path.Combine(_dataDir, $".{_fileName}.lock");
            for (var attempt = 0; attempt < 50; attempt++)
            {
                try
                {
                    using var handle = new FileStream(
                        lockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
                    return Run(body);
                }
                catch (IOException)
                {
                    await Task.Delay(20);
                }
            }
            // بعد از یک ثانیه تلاش، بدون قفلِ بین‌پردازه‌ای ادامه می‌دهیم تا کارِ
            // کاربر گم نشود؛ قفل نخیِ داخل همین پردازه هنوز برقرار است.
            _log.LogWarning("[store] قفل فایل {File} گرفته نشد، بدون قفل بین‌پردازه‌ای ادامه داده شد", _fileName);
            return Run(body);
        }
        finally
        {
            gate.Release();
        }
    }

    private TResult Run<TResult>(Func<List<T>, (bool Save, TResult Result)> body)
    {
        var items = Read();
        var (save, result) = body(items);
        if (save) WriteUnlocked(items);
        return result;
    }
}
