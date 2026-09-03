using System.Text.Json;
using System.Text.Json.Nodes;

namespace Up2Ai.Services;

/// <summary>
/// انبار محتوا.
///
/// ویرایش‌های پنل مدیریت در یک فایل JSON روی دیسک می‌نشینند و موقع رندر روی
/// پیش‌فرض‌ها ادغام می‌شوند. عمداً پایگاه‌داده نیست:
///
///   • محتوای یک سایت معرفی، چند کیلوبایت متن است نه داده‌ی رابطه‌ای.
///   • هیچ وابستگی بیرونی لازم ندارد، پس روی هر سروری که .NET دارد بالا می‌آید.
///   • پشتیبان‌گیری یعنی کپی یک فایل، و خودت هم می‌توانی دستی بازش کنی.
///
/// پیش‌فرض‌ها از `App_Data/defaults.json` خوانده می‌شوند — همان فایلی که در
/// نسخه‌ی قبلی به‌صورت ماژول‌های TypeScript بود. این فایل «منبع حقیقت شکل داده»
/// هم هست: هر مقداری که از دیسک خوانده می‌شود با همین ساختار مقایسه می‌شود و
/// هرچه شکلش نخواند دور ریخته می‌شود (نگاه کن به <see cref="ShapeMerge"/>).
///
/// نوشتن اتمیک است (نوشتن در فایل موقت و بعد rename) تا اگر وسط ذخیره برق
/// رفت، فایل نیمه‌نوشته باقی نماند.
/// </summary>
public sealed class ContentStore
{
    private readonly string _defaultsPath;
    private readonly string _dataDir;
    private readonly ILogger<ContentStore> _log;
    private readonly object _gate = new();

    private JsonNode? _defaults;

    // حافظه‌ی درون‌فرآیندی با کنترل زمان تغییر فایل: صفحه در هر درخواست رندر
    // می‌شود (تا ویرایش‌های پنل بلافاصله دیده شوند)، پس بدون این حافظه هر
    // بازدید یک خواندن و یک parse می‌شد. با آن، فقط وقتی فایل واقعاً عوض شده
    // دوباره خوانده می‌شود — یعنی درعمل بعد از هر ذخیره‌ی پنل، یک بار.
    private DateTime _overridesStamp;
    private JsonNode? _overridesCache;
    private JsonNode? _mergedCache;
    private MergeReport? _mergedReport;

    // آخرین باری که مهرِ زمانِ فایل واقعاً از دیسک پرسیده شد.
    private DateTime _stampCheckedAt = DateTime.MinValue;
    private static readonly TimeSpan StampDebounce = TimeSpan.FromSeconds(1);

    public ContentStore(IWebHostEnvironment env, IConfiguration config, ILogger<ContentStore> log)
    {
        _log = log;
        _defaultsPath = Path.Combine(env.ContentRootPath, "App_Data", "defaults.json");
        // همان قرارداد نسخه‌ی قبلی: اگر متغیر محیطی داده شد همان، وگرنه پوشه‌ی
        // `data/` کنار خود برنامه. روی هاستی که فایل‌سیستمش با هر دیپلوی پاک
        // می‌شود باید این متغیر به یک مسیر ماندگار اشاره کند.
        _dataDir = config["UP2AI_DATA_DIR"] ?? Path.Combine(env.ContentRootPath, "data");
    }

    public string ContentFilePath => Path.Combine(_dataDir, "content.json");

    /// <summary>پیش‌فرض‌های خام — شکل مجاز داده. پنل مدیریت فرم را از روی همین می‌سازد.</summary>
    public JsonNode Defaults
    {
        get
        {
            lock (_gate)
            {
                _defaults ??= JsonNode.Parse(File.ReadAllText(_defaultsPath))
                              ?? throw new InvalidOperationException("defaults.json خوانده نشد");
                return _defaults;
            }
        }
    }

    /// <summary>محتوای سایت برای رندر جاری (پیش‌فرض + ویرایش‌های معتبر).</summary>
    public JsonNode Get() => GetWithReport().Content;

    /// <summary>
    /// همان، ولی با گزارش این‌که چه چیزی رد شد — برای نمایش در پنل مدیریت.
    ///
    /// گزارش هم کش می‌شود، نه فقط محتوا. نسخه‌ی قبلی روی هر cache hit یک
    /// <c>MergeReport</c> *خالی* برمی‌گرداند؛ و چون
    /// <c>AdminPageModel.OnPageHandlerExecuting</c> پیش از هر handler یک بار
    /// <c>Get()</c> صدا می‌زند و کش را پر می‌کند، صفحه‌ی خانه‌ی پنل همیشه به
    /// cache hit می‌خورد و هشدارِ «این مقدارها با ساختار سایت نمی‌خواند»
    /// عملاً هیچ‌وقت نشان داده نمی‌شد — دقیقاً همان موقعی که لازم بود.
    /// </summary>
    public (JsonNode Content, MergeReport Report) GetWithReport()
    {
        lock (_gate)
        {
            if (_mergedCache is not null && !OverridesChanged())
                return (_mergedCache, _mergedReport ?? new MergeReport());

            _overridesCache = ReadOverrides();
            _overridesStamp = CurrentStamp();
            _stampCheckedAt = DateTime.UtcNow;
            var (content, report) = ShapeMerge.Merge(Defaults, _overridesCache);
            _mergedCache = content;
            _mergedReport = report;
            return (content, report);
        }
    }

    private DateTime CurrentStamp() =>
        File.Exists(ContentFilePath) ? File.GetLastWriteTimeUtc(ContentFilePath) : DateTime.MinValue;

    /// <summary>
    /// آیا فایل ویرایش‌ها عوض شده؟
    ///
    /// نسخه‌ی قبلی برای *هر* رندرِ هر صفحه دو syscall به فایل‌سیستم می‌زد
    /// (Exists + GetLastWriteTimeUtc) آن هم داخل قفلِ سراسری، یعنی همه‌ی
    /// درخواست‌های هم‌زمان پشت همان قفل صف می‌کشیدند. یک ثانیه فاصله بین
    /// بررسی‌ها این هزینه را تقریباً حذف می‌کند و در عمل هیچ فرقی هم نمی‌کند:
    /// بدترین حالت یعنی ویرایشِ پنل یک ثانیه دیرتر روی سایت دیده شود. (خودِ
    /// <see cref="Save"/> کش را فوراً باطل می‌کند، پس ادمین تغییرش را همان
    /// لحظه می‌بیند.)
    /// </summary>
    private bool OverridesChanged()
    {
        var now = DateTime.UtcNow;
        if (now - _stampCheckedAt < StampDebounce) return false;
        _stampCheckedAt = now;
        return CurrentStamp() != _overridesStamp;
    }

    private JsonNode? ReadOverrides()
    {
        try
        {
            if (!File.Exists(ContentFilePath)) return null; // هنوز چیزی ویرایش نشده — حالت عادی
            return JsonNode.Parse(File.ReadAllText(ContentFilePath));
        }
        catch (Exception ex)
        {
            // فایل خراب: سایت باید بالا بماند، پس با پیش‌فرض ادامه می‌دهیم و خطا
            // را فقط لاگ می‌کنیم.
            _log.LogError(ex, "[content] فایل ویرایش‌ها خوانده نشد");
            return null;
        }
    }

    /// <summary>
    /// ذخیره‌ی ویرایش‌ها. ورودی قبل از نوشتن از همان ادغام شکل‌محور رد می‌شود،
    /// پس چیزی که روی دیسک می‌نشیند همیشه معتبر است.
    /// </summary>
    public MergeReport Save(JsonNode next)
    {
        var (content, report) = ShapeMerge.Merge(Defaults, next);
        Directory.CreateDirectory(_dataDir);
        var json = content.ToJsonString(new JsonSerializerOptions
        {
            WriteIndented = true,
            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
            // لازم است تا گره‌هایی که از کد ساخته شده‌اند (نه از parse) هم
            // نوشته شوند؛ بدون آن سریال‌ساز استثنا می‌دهد.
            TypeInfoResolver = new System.Text.Json.Serialization.Metadata.DefaultJsonTypeInfoResolver(),
        });

        // نوشتن هم داخل همان قفلی که خواندن با آن هماهنگ می‌شود.
        //
        // قبلاً نامِ فایلِ موقت فقط شناسه‌ی *فرآیند* را داشت (یکی برای کل
        // برنامه) و نوشتن هم بیرون قفل بود. یعنی دو ذخیره‌ی هم‌زمان — که با
        // دوبار کلیک روی «ذخیره‌ی تغییرات» هم پیش می‌آید — روی یک فایل موقت
        // می‌نوشتند و یکی‌شان با IOException رد می‌شد («ذخیره انجام نشد»).
        // حالا نام یکتاست و کل کار سریالی است.
        lock (_gate)
        {
            var tmp = $"{ContentFilePath}.{Environment.ProcessId}.{Guid.NewGuid():N}.tmp";
            try
            {
                File.WriteAllText(tmp, json);
                File.Move(tmp, ContentFilePath, overwrite: true);
            }
            catch
            {
                // فایل موقتِ نیمه‌نوشته نباید در پوشه‌ی داده باقی بماند.
                try { if (File.Exists(tmp)) File.Delete(tmp); } catch { /* بی‌اهمیت */ }
                throw;
            }

            // خواندن بعدی حتماً از دیسک باشد — بدون منتظر ماندنِ آن یک ثانیه.
            _mergedCache = null;
            _mergedReport = null;
            _stampCheckedAt = DateTime.MinValue;
        }
        return report;
    }
}
