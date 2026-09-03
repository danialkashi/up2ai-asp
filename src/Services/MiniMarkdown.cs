using System.Text;
using System.Text.RegularExpressions;

namespace Up2Ai.Services;

/// <summary>
/// تبدیلِ متنِ نوشته به HTML — یک زیرمجموعه‌ی کوچک از مارک‌داون.
///
/// چرا نه HTML خام و نه یک کتابخانه:
///
///   • HTML خام یعنی هر چیزی که در پنل تایپ شود مستقیم داخل صفحه می‌رود.
///     امروز فقط مدیر به پنل دسترسی دارد، ولی اگر فردا رمزِ یک کاربر لو
///     برود، «ویرایش یک نوشته» تبدیل می‌شود به «اجرای اسکریپت روی مرورگرِ
///     همه‌ی بازدیدکننده‌ها». پس ورودی *همیشه* اسکیپ می‌شود و فقط همین چند
///     نشانه‌ی مشخص به تگ تبدیل می‌شوند.
///   • کتابخانه یعنی وابستگی NuGet، و پروژه عمداً هیچ وابستگی ندارد تا روی
///     سرورِ بدون اینترنتِ آزاد هم بیلد بگیرد.
///
/// آنچه پشتیبانی می‌شود (عمداً کم، تا کسی لازم نباشد مارک‌داون یاد بگیرد):
///
///     ## تیتر           ### تیترِ کوچک‌تر
///     - فهرست           ۱. فهرستِ شماره‌دار
///     &gt; نقل‌قول
///     **پررنگ**   *مورب*   `کد`   [متن](آدرس)
///     ---               (خطِ جداکننده)
///
/// خطِ خالی یعنی پاراگرافِ تازه. همین.
/// </summary>
public static class MiniMarkdown
{
    // کلاس‌ها این‌جا و نه در CSS: خروجی این تابع داخل صفحه‌ای می‌نشیند که
    // Tailwind دارد، و افزونه‌ی typography نصب نیست. با کلاسِ صریح، متنِ
    // نوشته دقیقاً همان تایپوگرافیِ بقیه‌ی سایت را می‌گیرد.
    private const string P = "class=\"mt-5 text-[15px] leading-[2.1] text-on-light\"";
    private const string H2 = "class=\"mt-10 text-[1.35rem] font-bold leading-[1.6] text-on-light sm:text-[1.5rem]\"";
    private const string H3 = "class=\"mt-8 text-[1.1rem] font-bold leading-[1.7] text-on-light\"";
    private const string UL = "class=\"mt-5 flex list-disc flex-col gap-2 ps-6 text-[15px] leading-[2] text-on-light marker:text-brand\"";
    private const string OL = "class=\"mt-5 flex list-fa flex-col gap-2 ps-6 text-[15px] leading-[2] text-on-light marker:text-brand\"";
    private const string QUOTE = "class=\"mt-6 border-s-4 border-brand/40 bg-brand/[0.04] px-5 py-3 text-[15px] leading-[2] text-on-light\"";
    private const string HR = "class=\"mt-8 border-hairline\"";
    private const string CODE = "class=\"rounded-md bg-surface-2 px-1.5 py-0.5 text-[13.5px] text-brand-ink\"";
    private const string PRE = "class=\"mt-6 overflow-x-auto rounded-xl bg-ink p-4 text-[13px] leading-[1.9] text-on-dark\"";
    private const string LINK = "class=\"text-brand-ink underline underline-offset-4 hover:text-brand\"";

    private static readonly Regex CodeSpan = new(@"`([^`\n]+)`", RegexOptions.Compiled);
    private static readonly Regex Link = new(@"\[([^\]\n]+)\]\(([^)\s]+)\)", RegexOptions.Compiled);
    private static readonly Regex Bold = new(@"\*\*([^*\n]+)\*\*", RegexOptions.Compiled);
    private static readonly Regex Italic = new(@"(?<![*\w])\*([^*\n]+)\*(?![*\w])", RegexOptions.Compiled);

    public static string ToHtml(string? markdown)
    {
        if (string.IsNullOrWhiteSpace(markdown)) return "";

        var lines = markdown.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
        var html = new StringBuilder();

        var paragraph = new List<string>();
        List<string>? bullets = null;
        List<string>? numbers = null;
        List<string>? quote = null;
        List<string>? code = null;

        void FlushParagraph()
        {
            if (paragraph.Count == 0) return;
            html.Append($"<p {P}>").Append(Inline(string.Join(" ", paragraph))).Append("</p>");
            paragraph.Clear();
        }

        void FlushList()
        {
            if (bullets is { Count: > 0 })
            {
                html.Append($"<ul {UL}>");
                foreach (var item in bullets) html.Append("<li>").Append(Inline(item)).Append("</li>");
                html.Append("</ul>");
            }
            bullets = null;

            if (numbers is { Count: > 0 })
            {
                html.Append($"<ol {OL}>");
                foreach (var item in numbers) html.Append("<li>").Append(Inline(item)).Append("</li>");
                html.Append("</ol>");
            }
            numbers = null;
        }

        void FlushQuote()
        {
            if (quote is { Count: > 0 })
                html.Append($"<blockquote {QUOTE}>").Append(Inline(string.Join(" ", quote))).Append("</blockquote>");
            quote = null;
        }

        // هر بلوکِ تازه، بلوکِ قبلی را می‌بندد.
        void FlushAll()
        {
            FlushParagraph();
            FlushList();
            FlushQuote();
        }

        foreach (var raw in lines)
        {
            var line = raw.TrimEnd();

            // ── بلوکِ کد: تا فنسِ بسته، هر چیزی عیناً و بدون تفسیر ──────────
            if (line.TrimStart().StartsWith("```", StringComparison.Ordinal))
            {
                if (code is null)
                {
                    FlushAll();
                    code = new List<string>();
                }
                else
                {
                    html.Append($"<pre {PRE}><code>").Append(Esc(string.Join("\n", code))).Append("</code></pre>");
                    code = null;
                }
                continue;
            }
            if (code is not null) { code.Add(raw); continue; }

            var trimmed = line.Trim();

            if (trimmed.Length == 0) { FlushAll(); continue; }

            if (trimmed is "---" or "***" or "___") { FlushAll(); html.Append($"<hr {HR} />"); continue; }

            if (trimmed.StartsWith("### ", StringComparison.Ordinal))
            {
                FlushAll();
                html.Append($"<h3 {H3}>").Append(Inline(trimmed[4..])).Append("</h3>");
                continue;
            }

            if (trimmed.StartsWith("## ", StringComparison.Ordinal))
            {
                FlushAll();
                html.Append($"<h2 {H2}>").Append(Inline(trimmed[3..])).Append("</h2>");
                continue;
            }

            // «# تیتر» هم پذیرفته می‌شود ولی h2 می‌شود: h1 صفحه عنوانِ خودِ
            // نوشته است و دو h1 در یک صفحه هم برای صفحه‌خوان و هم برای گوگل
            // پیامِ مبهمی می‌فرستد.
            if (trimmed.StartsWith("# ", StringComparison.Ordinal))
            {
                FlushAll();
                html.Append($"<h2 {H2}>").Append(Inline(trimmed[2..])).Append("</h2>");
                continue;
            }

            if (trimmed.StartsWith("> ", StringComparison.Ordinal) || trimmed == ">")
            {
                FlushParagraph();
                FlushList();
                quote ??= new List<string>();
                quote.Add(trimmed.Length > 1 ? trimmed[2..] : "");
                continue;
            }

            if (trimmed.StartsWith("- ", StringComparison.Ordinal) || trimmed.StartsWith("* ", StringComparison.Ordinal))
            {
                FlushParagraph();
                FlushQuote();
                if (numbers is not null) FlushList();
                bullets ??= new List<string>();
                bullets.Add(trimmed[2..]);
                continue;
            }

            // فهرستِ شماره‌دار: «۱. » یا «1. » — رقمِ فارسی هم پذیرفته می‌شود
            // چون کسی که فارسی می‌نویسد معمولاً با صفحه‌کلید فارسی شماره
            // می‌زند.
            var numbered = NumberedItem(trimmed);
            if (numbered is not null)
            {
                FlushParagraph();
                FlushQuote();
                if (bullets is not null) FlushList();
                numbers ??= new List<string>();
                numbers.Add(numbered);
                continue;
            }

            FlushList();
            FlushQuote();
            paragraph.Add(trimmed);
        }

        // فایل ممکن است وسطِ یک بلوک تمام شود (مثلاً فنسِ کد بسته نشده).
        if (code is not null)
            html.Append($"<pre {PRE}><code>").Append(Esc(string.Join("\n", code))).Append("</code></pre>");
        FlushAll();

        return html.ToString();
    }

    /// <summary>«۱. متن» یا «1) متن» → «متن»؛ اگر شماره‌دار نبود null.</summary>
    private static string? NumberedItem(string line)
    {
        var i = 0;
        while (i < line.Length && (char.IsAsciiDigit(line[i]) || (line[i] >= '۰' && line[i] <= '۹'))) i++;
        if (i == 0 || i + 1 >= line.Length) return null;
        if (line[i] is not ('.' or ')' or '-')) return null;
        if (line[i + 1] != ' ') return null;
        return line[(i + 2)..];
    }

    /// <summary>
    /// نشانه‌های درون‌خطی. ترتیب مهم است: اول کد (که محتوایش نباید تفسیر
    /// شود) کنار گذاشته می‌شود، بعد لینک، بعد پررنگ، بعد مورب — وگرنه
    /// ستاره‌ی داخلِ یک قطعه کد به تگ تبدیل می‌شد.
    /// </summary>
    private static string Inline(string text)
    {
        var codes = new List<string>();

        // اسکیپ اول از همه: از این خط به بعد هیچ &lt; خطرناکی در رشته نیست.
        var s = Esc(text);

        s = CodeSpan.Replace(s, m =>
        {
            codes.Add(m.Groups[1].Value);
            return $"\u0000{codes.Count - 1}\u0000";
        });

        s = Link.Replace(s, m =>
        {
            var label = m.Groups[1].Value;
            var href = m.Groups[2].Value;
            if (!SafeHref(href)) return m.Value; // آدرسِ مشکوک: همان متنِ خام بماند
            var external = href.StartsWith("http", StringComparison.OrdinalIgnoreCase);
            var extra = external ? " target=\"_blank\" rel=\"noopener noreferrer\"" : "";
            return $"<a href=\"{href}\" {LINK}{extra}>{label}</a>";
        });

        s = Bold.Replace(s, "<strong>$1</strong>");
        s = Italic.Replace(s, "<em>$1</em>");

        for (var i = 0; i < codes.Count; i++)
            s = s.Replace($"\u0000{i}\u0000", $"<code {CODE}>{codes[i]}</code>");

        return s;
    }

    /// <summary>
    /// فقط آدرس‌هایی که مقصدشان روشن است. هدفِ اصلی جلوگیری از
    /// `javascript:` است — که وگرنه یک لینکِ ساده در متنِ نوشته می‌شد راهِ
    /// اجرای اسکریپت.
    /// </summary>
    private static bool SafeHref(string href) =>
        href.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
        || href.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
        || href.StartsWith("mailto:", StringComparison.OrdinalIgnoreCase)
        || href.StartsWith("tel:", StringComparison.OrdinalIgnoreCase)
        || href.StartsWith('/')
        || href.StartsWith('#');

    private static string Esc(string s) => s
        .Replace("&", "&amp;")
        .Replace("<", "&lt;")
        .Replace(">", "&gt;")
        .Replace("\"", "&quot;");

    /// <summary>
    /// متنِ ساده‌ی نوشته — برای توضیحِ متا، خلاصه‌ی خودکار و فید RSS.
    /// نشانه‌های مارک‌داون برداشته می‌شوند تا در نتیجه‌ی گوگل ستاره و کروشه
    /// دیده نشود.
    /// </summary>
    public static string ToPlainText(string? markdown, int maxLength = 0)
    {
        if (string.IsNullOrWhiteSpace(markdown)) return "";

        var sb = new StringBuilder();
        foreach (var raw in markdown.Replace("\r\n", "\n").Split('\n'))
        {
            var line = raw.Trim();
            if (line.Length == 0 || line.StartsWith("```", StringComparison.Ordinal)) continue;
            line = line.TrimStart('#', '>', '-', '*', ' ');
            var item = NumberedItem(line);
            if (item is not null) line = item;
            line = CodeSpan.Replace(line, "$1");
            line = Link.Replace(line, "$1");
            line = Bold.Replace(line, "$1");
            line = Italic.Replace(line, "$1");
            if (line.Length == 0) continue;
            if (sb.Length > 0) sb.Append(' ');
            sb.Append(line);
            if (maxLength > 0 && sb.Length > maxLength) break;
        }

        var text = sb.ToString().Trim();
        if (maxLength <= 0 || text.Length <= maxLength) return text;

        // برشِ وسطِ کلمه زشت است: تا آخرین فاصله عقب می‌رویم.
        var cut = text[..maxLength];
        var space = cut.LastIndexOf(' ');
        if (space > maxLength / 2) cut = cut[..space];
        return cut.TrimEnd() + "…";
    }
}
