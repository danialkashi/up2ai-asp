using Up2Ai.Services.Pg;

namespace Up2Ai.Services;

/// <summary>
/// تصمیم می‌گیرد هر انبار روی فایل بنشیند یا روی پستگرس.
///
/// قاعده ساده است: اگر رشته‌ی اتصال دیتابیس داده شده باشد، همه‌چیز در
/// پستگرس؛ وگرنه همان فایل‌های <c>data/*.json</c> مثل قبل. هیچ حالت نیمه‌ای
/// وجود ندارد تا کسی سهواً نصفِ داده را این‌جا و نصفش را آن‌جا نگذارد.
///
/// رشته‌ی اتصال از یکی از این‌ها خوانده می‌شود (به همین ترتیب):
///   UP2AI_DATABASE_URL   — نام صریح خودمان
///   DATABASE_URL         — قراردادِ رایج هاست‌های ابری
/// </summary>
public sealed class StorageFactory
{
    private readonly string _dataDir;
    private readonly PgClient? _db;
    private readonly ILoggerFactory _logs;

    public StorageFactory(string dataDir, PgClient? db, ILoggerFactory logs)
    {
        _dataDir = dataDir;
        _db = db;
        _logs = logs;
    }

    public bool UsesPostgres => _db is not null;

    /// <summary>اتصالِ مشترک — فقط برای ساختِ جدول‌ها هنگام راه‌اندازی.</summary>
    public PgClient? Db => _db;

    /// <summary>برای صفحه‌ی خانه‌ی پنل و لاگِ راه‌اندازی.</summary>
    public string Describe() => _db is null
        ? $"فایل‌های JSON در {_dataDir}"
        : $"پستگرس — {_db.Info.Safe}";

    public IRecordStore<T> Records<T>(
        string fileName, string table, Func<T, string> idOf, Func<T, bool> isValid, ILogger log)
        where T : class =>
        _db is null
            ? new JsonFileStore<T>(_dataDir, fileName, isValid, log)
            : new PgRecordStore<T>(_db, table, idOf, isValid, log);

    public IContentPersistence Content() =>
        _db is null
            ? new FileContentPersistence(_dataDir, _logs.CreateLogger<FileContentPersistence>())
            : new PgContentPersistence(_db, _logs.CreateLogger<PgContentPersistence>());

    /// <summary>
    /// رشته‌ی اتصال را از پیکربندی می‌خواند. اگر چیزی نبود، null یعنی «همان
    /// فایل‌ها».
    /// </summary>
    public static string? ConnectionStringFrom(IConfiguration config) =>
        FirstNonEmpty(config["UP2AI_DATABASE_URL"], config["DATABASE_URL"]);

    private static string? FirstNonEmpty(params string?[] values) =>
        values.FirstOrDefault(v => !string.IsNullOrWhiteSpace(v))?.Trim();
}
