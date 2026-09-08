using Up2Ai.Services.Pg;

namespace Up2Ai.Services;

/// <summary>
/// ذخیره‌سازی حالا فقط روی پستگرس است.
///
/// رشته‌ی اتصال الزامی است — با این نسخه دیگر JSON در بالای وب‌اپ استفاده نمی‌شود.
/// برای انتقال داده‌های قدیمی، ابزار تخصصی استفاده می‌شود.
///
/// رشته‌ی اتصال از یکی از این‌ها خوانده می‌شود:
///   ConnectionStrings:DefaultConnection   — استاندارد ASP.NET
///   DATABASE_URL                           — قراردادِ رایج هاست‌های ابری
/// </summary>
public sealed class StorageFactory
{
    private readonly PgClient _db;
    private readonly ILoggerFactory _logs;

    public StorageFactory(PgClient db, ILoggerFactory logs)
    {
        _db = db;
        _logs = logs;
    }

    /// <summary>اتصالِ مشترک — برای صفحه‌ی خانه‌ی پنل و لاگِ راه‌اندازی.</summary>
    public PgClient Db => _db;

    /// <summary>برای صفحه‌ی خانه‌ی پنل و لاگِ راه‌اندازی.</summary>
    public string Describe() => $"PostgreSQL — {_db.Info.Safe}";

    public IRecordStore<T> Records<T>(
        string table, Func<T, string> idOf, Func<T, bool> isValid, ILogger log)
        where T : class =>
        new PgRecordStore<T>(_db, table, idOf, isValid, log);

    public IContentPersistence Content() =>
        new PgContentPersistence(_db, _logs.CreateLogger<PgContentPersistence>());

    /// <summary>
    /// رشته‌ی اتصال را از پیکربندی می‌خواند. الزامی است.
    /// </summary>
    public static string? ConnectionStringFrom(IConfiguration config) =>
        FirstNonEmpty(config["ConnectionStrings:DefaultConnection"], config["DATABASE_URL"]);

    private static string? FirstNonEmpty(params string?[] values) =>
        values.FirstOrDefault(v => !string.IsNullOrWhiteSpace(v))?.Trim();
}
