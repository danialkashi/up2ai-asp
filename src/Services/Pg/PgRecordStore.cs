using System.Text.Json;

namespace Up2Ai.Services.Pg;

/// <summary>
/// همان قراردادِ <see cref="IRecordStore{T}"/>، ولی روی یک جدول پستگرس.
///
/// هر رکورد یک سطر است: <c>id</c> کلید اصلی، <c>ord</c> جای رکورد در فهرست
/// (تا ترتیب دقیقاً همان چیزی بماند که نسخه‌ی فایلی داشت) و <c>doc</c> خودِ
/// رکورد به‌صورت jsonb.
///
/// چرا jsonb و نه ستون به ازای هر فیلد؟ چون مدل‌های برنامه همین حالا هم با
/// JSON سریالایز می‌شوند و این‌طور هیچ نگاشتِ دستی‌ای بین کد و جدول نمی‌ماند
/// که از هم عقب بیفتد. برای این‌که تیم بتواند مستقیم SQL بزند، ستون‌های
/// «تولیدشده» (generated) روی فیلدهای پرکاربرد ساخته می‌شوند — مثلاً
/// <c>select name, reach, at from leads order by at desc</c> کار می‌کند،
/// بدون آن‌که برنامه چیزی جز doc بنویسد. (نگاه کن به <see cref="PgSchema"/>.)
///
/// قفل: هر نوشتن داخل یک تراکنش با <c>LOCK TABLE … IN EXCLUSIVE MODE</c>
/// انجام می‌شود. این دقیقاً همان تضمینی است که قفلِ فایلی می‌داد — دو نمونه‌ی
/// هم‌زمانِ برنامه نمی‌توانند نوشتنِ هم را دور بریزند — با این تفاوت که
/// این‌جا واقعاً بین چند سرور هم کار می‌کند.
/// </summary>
public sealed class PgRecordStore<T> : IRecordStore<T> where T : class
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    private readonly PgClient _db;
    private readonly string _table;
    private readonly Func<T, string> _idOf;
    private readonly Func<T, bool> _isValid;
    private readonly ILogger _log;

    public PgRecordStore(PgClient db, string table, Func<T, string> idOf, Func<T, bool> isValid, ILogger log)
    {
        _db = db;
        _table = PgSchema.SafeName(table);
        _idOf = idOf;
        _isValid = isValid;
        _log = log;
    }

    public List<T> Read()
    {
        try
        {
            // ASP.NET Core همگام‌کننده‌ی نخ ندارد، پس بلاک کردن این‌جا بن‌بست
            // نمی‌سازد؛ Task.Run فقط برای اطمینان از اجرا روی استخر نخ است.
            var rows = Task.Run(() => _db.QueryAsync($"select doc from {_table} order by ord")).GetAwaiter().GetResult();
            return Materialize(rows.Select(r => r.GetString(0)));
        }
        catch (Exception ex)
        {
            // همان رفتار نسخه‌ی فایلی: خطای خواندن نباید صفحه را بخواباند.
            _log.LogError(ex, "[pg] خواندن جدول {Table} ناموفق بود", _table);
            return new List<T>();
        }
    }

    private List<T> Materialize(IEnumerable<string?> docs)
    {
        var list = new List<T>();
        foreach (var doc in docs)
        {
            if (doc is null) continue;
            try
            {
                var item = JsonSerializer.Deserialize<T>(doc);
                if (item is not null && _isValid(item)) list.Add(item);
            }
            catch (JsonException ex)
            {
                // رکوردِ خراب (مثلاً دست‌کاری دستی در دیتابیس) فقط خودش کنار
                // گذاشته می‌شود، نه کل فهرست.
                _log.LogError(ex, "[pg] یک سطر خرابِ {Table} نادیده گرفته شد", _table);
            }
        }
        return list;
    }

    public async Task<TResult> MutateAsync<TResult>(Func<List<T>, (bool Save, TResult Result)> body)
    {
        using var lease = await _db.LeaseAsync();
        var session = lease.Session;
        var committed = false;
        try
        {
            await session.SimpleQueryAsync("begin");
            // قفلِ نوشتن: خواندن آزاد می‌ماند، نوشتنِ هم‌زمان صف می‌شود.
            await session.SimpleQueryAsync($"lock table {_table} in exclusive mode");

            var rows = await session.QueryAsync($"select id, doc, ord from {_table} order by ord");
            var before = new Dictionary<string, (string Doc, long Ord)>(StringComparer.Ordinal);
            foreach (var r in rows)
            {
                var id = r.GetString("id");
                if (id is not null) before[id] = (r.GetString("doc") ?? "", r.GetLong("ord"));
            }

            var items = Materialize(rows.Select(r => r.GetString("doc")));

            // شناسه‌ی سطرهایی که واقعاً خوانده شدند. حذف فقط بین همین‌ها معنا
            // دارد: اگر سطری خراب باشد و از Materialize رد نشود، *نباید* به
            // بهانه‌ی «در فهرست نیست» پاک شود. (نسخه‌ی فایلی هم چنین سطری را
            // دور می‌ریخت؛ روی دیتابیس این یعنی از دست رفتنِ داده‌ی واقعی.)
            var known = new HashSet<string>(items.Select(_idOf), StringComparer.Ordinal);

            var (save, result) = body(items);

            if (save)
            {
                var seen = new HashSet<string>(StringComparer.Ordinal);
                for (var i = 0; i < items.Count; i++)
                {
                    var id = _idOf(items[i]);
                    if (id.Length == 0)
                    {
                        _log.LogWarning("[pg] رکوردی بدون شناسه در {Table} نادیده گرفته شد", _table);
                        continue;
                    }
                    seen.Add(id);
                    var doc = JsonSerializer.Serialize(items[i], JsonOpts);

                    // فقط چیزی که واقعاً عوض شده نوشته می‌شود — هم سریع‌تر است
                    // هم updated_at سطرهای دست‌نخورده را جابه‌جا نمی‌کند.
                    if (before.TryGetValue(id, out var old) && old.Ord == i && JsonEquals(old.Doc, doc))
                        continue;

                    await session.ExecuteAsync(
                        $"insert into {_table} (id, ord, doc, updated_at) values ($1, $2, $3::jsonb, now()) " +
                        "on conflict (id) do update set ord = excluded.ord, doc = excluded.doc, updated_at = now()",
                        id, (long)i, doc);
                }

                foreach (var goneId in known.Where(k => !seen.Contains(k)).ToList())
                {
                    await session.ExecuteAsync($"delete from {_table} where id = $1", goneId);
                }
            }

            await session.SimpleQueryAsync("commit");
            committed = true;
            return result;
        }
        finally
        {
            if (!committed)
            {
                try { await session.SimpleQueryAsync("rollback"); }
                catch { lease.MarkBroken(); }
            }
        }
    }

    /// <summary>
    /// مقایسه‌ی دو JSON بدون توجه به ترتیب/فاصله. لازم است چون jsonb پستگرس
    /// متن را عیناً نگه نمی‌دارد: کلیدها را مرتب و فاصله‌ها را حذف می‌کند، پس
    /// مقایسه‌ی رشته‌ای خام همیشه «تغییر کرده» می‌گفت و هر ذخیره همه‌ی سطرها
    /// را دوباره می‌نوشت.
    /// </summary>
    private static bool JsonEquals(string a, string b)
    {
        try
        {
            using var da = JsonDocument.Parse(a);
            using var db = JsonDocument.Parse(b);
            return JsonElementEquals(da.RootElement, db.RootElement);
        }
        catch (JsonException) { return false; }
    }

    private static bool JsonElementEquals(JsonElement a, JsonElement b)
    {
        if (a.ValueKind != b.ValueKind) return false;
        switch (a.ValueKind)
        {
            case JsonValueKind.Object:
                var ap = a.EnumerateObject().OrderBy(x => x.Name, StringComparer.Ordinal).ToList();
                var bp = b.EnumerateObject().OrderBy(x => x.Name, StringComparer.Ordinal).ToList();
                if (ap.Count != bp.Count) return false;
                for (var i = 0; i < ap.Count; i++)
                {
                    if (ap[i].Name != bp[i].Name) return false;
                    if (!JsonElementEquals(ap[i].Value, bp[i].Value)) return false;
                }
                return true;

            case JsonValueKind.Array:
                var aa = a.EnumerateArray().ToList();
                var ba = b.EnumerateArray().ToList();
                if (aa.Count != ba.Count) return false;
                for (var i = 0; i < aa.Count; i++)
                    if (!JsonElementEquals(aa[i], ba[i])) return false;
                return true;

            case JsonValueKind.String: return a.GetString() == b.GetString();
            case JsonValueKind.Number: return a.GetRawText() == b.GetRawText();
            default: return true;   // true / false / null — خودِ ValueKind کافی است
        }
    }
}
