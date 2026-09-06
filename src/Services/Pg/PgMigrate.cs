using System.Text.Json;
using System.Text.Json.Nodes;

namespace Up2Ai.Services.Pg;

/// <summary>
/// انتقال یک‌باره‌ی داده‌های موجود از فایل‌های <c>data/*.json</c> به پستگرس.
///
///     dotnet run -- migrate-to-postgres            # انتقال
///     dotnet run -- migrate-to-postgres --force    # حتی اگر جدول‌ها خالی نباشند
///
/// عمداً یک دستور جداست و هنگام بالا آمدن سایت خودکار اجرا نمی‌شود: انتقال
/// داده کاری است که آدم باید یک بار، آگاهانه، و با بکاپ در دست انجام بدهد.
///
/// فایل‌ها بعد از انتقال دست‌نخورده می‌مانند؛ اگر چیزی درست نبود، کافی است
/// رشته‌ی اتصال را بردارید تا سایت دوباره از همان فایل‌ها بخواند.
/// </summary>
public static class PgMigrate
{
    public static async Task<int> RunAsync(string dataDir, string connectionString, bool force)
    {
        Console.OutputEncoding = System.Text.Encoding.UTF8;

        PgConnectionInfo info;
        try
        {
            info = PgConnectionInfo.Parse(connectionString);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"رشته‌ی اتصال خوانده نشد: {ex.Message}");
            return 1;
        }

        Console.WriteLine($"مقصد: {info.Safe}");
        Console.WriteLine($"مبدأ: {dataDir}");
        Console.WriteLine();

        using var loggerFactory = LoggerFactory.Create(b => b.AddSimpleConsole());
        await using var db = new PgClient(info, loggerFactory.CreateLogger("migrate"));

        try
        {
            var version = await db.PingAsync();
            Console.WriteLine($"اتصال برقرار شد: {version.Split(',')[0]}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"اتصال به پستگرس ممکن نشد: {ex.Message}");
            return 1;
        }

        await db.ExecuteScriptAsync(PgSchema.CreateAll());
        Console.WriteLine("جدول‌ها ساخته (یا تأیید) شدند.");

        var tables = new[] { PgSchema.Leads, PgSchema.Posts, PgSchema.Comments, PgSchema.AdminUsers };
        if (!force)
        {
            foreach (var t in tables)
            {
                var rows = await db.QueryAsync($"select count(*) from {t}");
                if (rows.Count > 0 && rows[0].GetString(0) is not "0")
                {
                    Console.WriteLine($"جدول {t} خالی نیست. اگر مطمئنی، دوباره با --force اجرا کن.");
                    return 1;
                }
            }
        }

        var total = 0;
        total += await CopyListAsync(db, Path.Combine(dataDir, "leads.json"), PgSchema.Leads, "id");
        total += await CopyListAsync(db, Path.Combine(dataDir, "posts.json"), PgSchema.Posts, "id");
        total += await CopyListAsync(db, Path.Combine(dataDir, "comments.json"), PgSchema.Comments, "id");
        total += await CopyListAsync(db, Path.Combine(dataDir, "admin-users.json"), PgSchema.AdminUsers, "id");
        total += await CopyContentAsync(db, Path.Combine(dataDir, "content.json"));

        Console.WriteLine();
        Console.WriteLine($"تمام شد — {total} رکورد منتقل شد.");
        Console.WriteLine("حالا UP2AI_DATABASE_URL را در محیط سرور بگذار و برنامه را دوباره اجرا کن.");
        Console.WriteLine("فایل‌های JSON دست‌نخورده ماندند؛ به‌عنوان بکاپ نگهشان دار.");
        return 0;
    }

    private static async Task<int> CopyListAsync(PgClient db, string path, string table, string idField)
    {
        if (!File.Exists(path))
        {
            Console.WriteLine($"{table}: فایلی نبود، رد شد.");
            return 0;
        }

        JsonArray? items;
        try
        {
            items = JsonNode.Parse(await File.ReadAllTextAsync(path)) as JsonArray;
        }
        catch (JsonException ex)
        {
            Console.WriteLine($"{table}: فایل خراب است ({ex.Message}) — رد شد.");
            return 0;
        }
        if (items is null)
        {
            Console.WriteLine($"{table}: محتوای فایل فهرست نبود — رد شد.");
            return 0;
        }

        var count = 0;
        for (var i = 0; i < items.Count; i++)
        {
            var node = items[i];
            var id = node?[idField]?.GetValue<string>();
            if (node is null || string.IsNullOrEmpty(id))
            {
                Console.WriteLine($"{table}: رکورد بدون شناسه در جایگاه {i} رد شد.");
                continue;
            }
            await db.ExecuteAsync(
                $"insert into {table} (id, ord, doc, updated_at) values ($1, $2, $3::jsonb, now()) " +
                "on conflict (id) do update set ord = excluded.ord, doc = excluded.doc, updated_at = now()",
                id, (long)i, node.ToJsonString());
            count++;
        }
        Console.WriteLine($"{table}: {count} رکورد.");
        return count;
    }

    private static async Task<int> CopyContentAsync(PgClient db, string path)
    {
        if (!File.Exists(path))
        {
            Console.WriteLine($"{PgSchema.Content}: فایلی نبود (یعنی محتوا هنوز ویرایش نشده) — رد شد.");
            return 0;
        }
        var json = await File.ReadAllTextAsync(path);
        try { JsonNode.Parse(json); }
        catch (JsonException ex)
        {
            Console.WriteLine($"{PgSchema.Content}: فایل خراب است ({ex.Message}) — رد شد.");
            return 0;
        }

        await db.ExecuteAsync(
            $"insert into {PgSchema.Content} (key, doc, updated_at) values ('site', $1::jsonb, now()) " +
            "on conflict (key) do update set doc = excluded.doc, updated_at = now()", json);
        Console.WriteLine($"{PgSchema.Content}: ۱ سند.");
        return 1;
    }
}
