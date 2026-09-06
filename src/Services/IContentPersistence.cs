using Up2Ai.Services.Pg;

namespace Up2Ai.Services;

/// <summary>
/// جایی که ویرایش‌های محتوای سایت نگه داشته می‌شود — فایل یا پستگرس.
///
/// <see cref="ContentStore"/> منطقِ ادغام با پیش‌فرض‌ها و کش را دارد و اصلاً
/// نمی‌داند داده کجا می‌نشیند؛ فقط همین سه کار را می‌خواهد.
/// </summary>
public interface IContentPersistence
{
    /// <summary>
    /// نشانه‌ی نسخه. اگر عوض شود یعنی یک نفرِ دیگر (یا نمونه‌ی دیگری از
    /// برنامه) محتوا را عوض کرده و کش باید دور ریخته شود.
    /// </summary>
    string Stamp();

    /// <summary>متن خامِ ویرایش‌ها، یا null اگر هنوز چیزی ذخیره نشده.</summary>
    string? ReadRaw();

    void Write(string json);

    /// <summary>برای پیام‌های پنل و لاگ — «فایل …» یا «پستگرس …».</summary>
    string Describe();
}

/// <summary>ذخیره در <c>data/content.json</c> — رفتار پیش‌فرض و بدون وابستگی.</summary>
public sealed class FileContentPersistence : IContentPersistence
{
    private readonly string _dataDir;
    private readonly ILogger _log;

    public FileContentPersistence(string dataDir, ILogger log)
    {
        _dataDir = dataDir;
        _log = log;
    }

    public string Path_ => System.IO.Path.Combine(_dataDir, "content.json");

    public string Stamp() =>
        File.Exists(Path_) ? File.GetLastWriteTimeUtc(Path_).Ticks.ToString() : "";

    public string? ReadRaw()
    {
        try
        {
            return File.Exists(Path_) ? File.ReadAllText(Path_) : null;
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "[content] فایل ویرایش‌ها خوانده نشد");
            return null;
        }
    }

    public void Write(string json)
    {
        Directory.CreateDirectory(_dataDir);
        // نوشتن اتمی: اول موقت، بعد Move. نامِ یکتا تا دو ذخیره‌ی هم‌زمان روی
        // یک فایل موقت ننویسند.
        var tmp = $"{Path_}.{Environment.ProcessId}.{Guid.NewGuid():N}.tmp";
        try
        {
            File.WriteAllText(tmp, json);
            File.Move(tmp, Path_, overwrite: true);
        }
        catch
        {
            try { if (File.Exists(tmp)) File.Delete(tmp); } catch { /* بی‌اهمیت */ }
            throw;
        }
    }

    public string Describe() => $"فایل {Path_}";
}

/// <summary>ذخیره در یک سطرِ جدول <c>site_content</c>.</summary>
public sealed class PgContentPersistence : IContentPersistence
{
    private const string Key = "site";

    private readonly PgClient _db;
    private readonly ILogger _log;

    public PgContentPersistence(PgClient db, ILogger log)
    {
        _db = db;
        _log = log;
    }

    public string Stamp()
    {
        try
        {
            var rows = Run(_db.QueryAsync(
                $"select updated_at from {PgSchema.Content} where key = $1", Key));
            return rows.Count > 0 ? rows[0].GetString(0) ?? "" : "";
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "[content] زمان آخرین تغییر از دیتابیس خوانده نشد");
            return "";
        }
    }

    public string? ReadRaw()
    {
        try
        {
            var rows = Run(_db.QueryAsync($"select doc from {PgSchema.Content} where key = $1", Key));
            return rows.Count > 0 ? rows[0].GetString(0) : null;
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "[content] ویرایش‌ها از دیتابیس خوانده نشد");
            return null;
        }
    }

    public void Write(string json) => Run(_db.ExecuteAsync(
        $"insert into {PgSchema.Content} (key, doc, updated_at) values ($1, $2::jsonb, now()) " +
        "on conflict (key) do update set doc = excluded.doc, updated_at = now()", Key, json));

    private static TResult Run<TResult>(Task<TResult> task) =>
        Task.Run(() => task).GetAwaiter().GetResult();

    public string Describe() => "پستگرس (جدول site_content)";
}
