using System.Text.RegularExpressions;

namespace Up2Ai.Services.Pg;

/// <summary>
/// جدول‌های سایت و ساختنشان.
///
/// همه‌ی دستورها if not exists دارند، پس اجرای دوباره‌شان بی‌خطر است و
/// برنامه می‌تواند هر بار که بالا می‌آید schema را تضمین کند — بدون ابزارِ
/// مهاجرتِ جداگانه.
///
/// ستون‌های «تولیدشده» (generated ... stored) عمدی‌اند: برنامه فقط doc
/// را می‌نویسد، ولی تیم می‌تواند مستقیم SQL بزند و ستون‌های خوانا ببیند —
/// مثلاً select name, reach, at from leads where handled = 'false' order by at desc.
/// این ستون‌ها هیچ‌وقت با doc از هم عقب نمی‌افتند چون خودِ پستگرس محاسبه‌شان می‌کند.
/// </summary>
public static class PgSchema
{
      public const string Leads = "leads";
      public const string Posts = "posts";
      public const string Comments = "comments";
      public const string AdminUsers = "admin_users";
      public const string Content = "site_content";

      private static readonly Regex NameRule = new("^[a-z_][a-z0-9_]*$", RegexOptions.Compiled);

      public static string SafeName(string name) =>
                NameRule.IsMatch(name) ? name : throw new ArgumentException($"نام جدول نامعتبر: {name}", nameof(name));

      private static string RecordTable(string table, params string[] generated)
      {
                var extra = generated.Length > 0 ? ",\n  " + string.Join(",\n  ", generated) : "";
                return $"""
                          create table if not exists {table} (
                                      id         text primary key,
                                      ord        bigint not null default 0,
                                      doc        jsonb not null,
                                      updated_at timestamptz not null default now(){extra}
                                    );
                create index if not exists {table}_ord_idx on {table} (ord);
                """;
        }

      public static string CreateAll() => string.Join("\n", new[]
                                                      {
                                                                RecordTable(Leads,
                                                                                        "name     text generated always as (doc->>'name') stored",
                                                                                        "reach    text generated always as (doc->>'reach') stored",
                                                                                        "business text generated always as (doc->>'business') stored",
                                                                                        "service  text generated always as (doc->>'service') stored",
                                                                                        "at       text generated always as (doc->>'at') stored",
                                                                                        "handled  text generated always as (doc->>'handled') stored"),
                                                                $"create index if not exists {Leads}_at_idx on {Leads} (at desc);",

                                                                RecordTable(Posts,
                                                                                        "slug      text generated always as (doc->>'slug') stored",
                                                                                        "title     text generated always as (doc->>'title') stored",
                                                                                        "published text generated always as (doc->>'published') stored"),
                                                                $"create unique index if not exists {Posts}_slug_idx on {Posts} (slug);",

                                                                RecordTable(Comments,
                                                                                        "post_id  text generated always as (doc->>'postId') stored",
                                                                                        "approved text generated always as (doc->>'approved') stored",
                                                                                        "at       text generated always as (doc->>'at') stored"),
                                                                $"create index if not exists {Comments}_post_idx on {Comments} (post_id);",

                                                                RecordTable(AdminUsers,
                                                                                        "username text generated always as (doc->>'username') stored",
                                                                                        "active   text generated always as (doc->>'active') stored"),
                                                                $"create unique index if not exists {AdminUsers}_username_idx on {AdminUsers} (lower(username));",

                                                                $"""
                                                                          create table if not exists {Content} (
                                                                                      key        text primary key,
                                                                                      doc        jsonb not null,
                                                                                      updated_at timestamptz not null default now()
                                                                                    );
                                                                """,
                                                        });
}
