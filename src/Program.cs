using Up2Ai.Services;

// ─────────────────────────────────────────────────────────────────────────────
// حالت خط فرمان: ساخت هش رمز پنل مدیریت.
//
//     dotnet run -- hash-password
//
// رمز را می‌پرسد و هشش را چاپ می‌کند. خودِ رمز نه جایی ذخیره می‌شود، نه در
// history ترمینال می‌ماند (چون به‌عنوان آرگومان گرفته نمی‌شود)، و نه موقع تایپ
// روی صفحه دیده می‌شود.
// ─────────────────────────────────────────────────────────────────────────────
if (args.Length > 0 && args[0] == "hash-password")
{
    return HashPasswordCommand.Run();
}

var builder = WebApplication.CreateBuilder(args);

// متغیرهای محیطی بدون پیشوند هم خوانده شوند (ADMIN_PASSWORD_HASH و …)، و
// فایل `.env` کنار برنامه هم اگر بود — تا تجربه‌ی راه‌اندازی مثل قبل بماند.
DotEnv.LoadInto(builder.Configuration, Path.Combine(builder.Environment.ContentRootPath, ".env"));

// بدون این، انکودر پیش‌فرض Razor هر حرف غیرلاتین را به `&#x…;` تبدیل می‌کند:
// صفحه درست دیده می‌شود ولی حجم HTML چند برابر می‌شود و خروجی دیگر با نسخه‌ی
// قبلی یکی نیست. با این تنظیم، فارسی همان فارسی می‌ماند.
builder.Services.Configure<Microsoft.Extensions.WebEncoders.WebEncoderOptions>(o =>
    o.TextEncoderSettings = new System.Text.Encodings.Web.TextEncoderSettings(
        System.Text.Unicode.UnicodeRanges.All));

builder.Services.AddRazorPages();
builder.Services.AddSingleton<ContentStore>();
builder.Services.AddSingleton<LeadStore>();
builder.Services.AddSingleton<AdminAuth>();
builder.Services.AddSingleton<BlogStore>();
builder.Services.AddSingleton<AdminUserStore>();
// singleton چون شمارشِ IPها و توکن‌های مصرف‌شده باید بین درخواست‌ها بماند.
builder.Services.AddSingleton<FormGuard>();
builder.Services.AddAntiforgery();

// فشرده‌سازی پاسخ‌ها.
//
// صفحه‌ی اصلی و CSS با هم چند ده کیلوبایت متن‌اند و متن با Brotli تا حدود
// یک‌ششم جمع می‌شود. روی سرور واقعی (که برخلاف اینجا پهنای باند و تأخیر
// دارد) این بزرگ‌ترین برد سرعت است، نه یک بهینه‌سازی تزئینی.
//
// EnableForHttps عمداً روشن است: کل سایت روی HTTPS است، و حمله‌ی BREACH
// وقتی معنا دارد که پاسخِ فشرده هم‌زمان «راز» و «ورودیِ مهاجم» را داشته
// باشد. تنها رازِ ما توکن ضدجعل است که در هر پاسخ تازه ساخته می‌شود.
builder.Services.AddResponseCompression(o =>
{
    o.EnableForHttps = true;
    o.Providers.Add<Microsoft.AspNetCore.ResponseCompression.BrotliCompressionProvider>();
    o.Providers.Add<Microsoft.AspNetCore.ResponseCompression.GzipCompressionProvider>();
    // پیش‌فرضِ ASP.NET چند نوعِ محدود را می‌گیرد؛ این‌ها را خودمان اضافه می‌کنیم.
    o.MimeTypes = Microsoft.AspNetCore.ResponseCompression.ResponseCompressionDefaults
        .MimeTypes.Concat(new[]
        {
            "image/svg+xml", "application/rss+xml", "application/xml", "text/xml",
            "application/json", "application/ld+json", "application/manifest+json",
        });
});
builder.Services.Configure<Microsoft.AspNetCore.ResponseCompression.BrotliCompressionProviderOptions>(
    o => o.Level = System.IO.Compression.CompressionLevel.Optimal);
builder.Services.Configure<Microsoft.AspNetCore.ResponseCompression.GzipCompressionProviderOptions>(
    o => o.Level = System.IO.Compression.CompressionLevel.Optimal);

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
}

// آدرسِ اشتباه باید صفحه‌ی ۴۰۴ی فارسیِ خودمان را بگیرد، نه یک پاسخِ خالی.
// (قبل از این، /هرچیز-اشتباه یک بدنه‌ی صفربایتی برمی‌گرداند و متنِ «صفحه‌ی
// ۴۰۴» که در پنل مدیریت قابل ویرایش بود هیچ‌وقت جایی دیده نمی‌شد.)
// ReExecute یعنی کد وضعیت ۴۰۴ حفظ می‌شود و فقط بدنه از /Error رندر می‌شود —
// برای موتورهای جست‌وجو مهم است که ۲۰۰ برنگردد.
app.UseStatusCodePagesWithReExecute("/Error");

// ترتیب مهم است: فشرده‌سازی باید *قبل* از هر چیزی باشد که بدنه می‌نویسد.
app.UseResponseCompression();

// کشِ فایل‌های ثابت.
//
// بدون این، مرورگر هر بار فونت ۵۷ کیلوبایتی و CSS را دوباره می‌گیرد —
// Lighthouse هم دقیقاً همین را به‌عنوان بزرگ‌ترین ایراد نشان می‌داد.
//
// یک سال + immutable فقط برای فایل‌هایی امن است که آدرسشان با تغییر محتوا
// عوض می‌شود. CSS و JS را با `asp-append-version` می‌فرستیم، پس هش در
// کوئری می‌آید و نسخه‌ی تازه بلافاصله دیده می‌شود. فونت هم فایلی است که
// اگر روزی عوض شود، اسمش عوض می‌شود. بقیه (مثل تصویر og) یک روز کش
// می‌گیرند تا در بدترین حالت خیلی زود تازه شوند.
app.UseStaticFiles(new StaticFileOptions
{
    OnPrepareResponse = ctx =>
    {
        var path = ctx.Context.Request.Path.Value ?? "";
        var longLived =
            path.StartsWith("/css/", StringComparison.OrdinalIgnoreCase) ||
            path.StartsWith("/js/", StringComparison.OrdinalIgnoreCase) ||
            path.StartsWith("/fonts/", StringComparison.OrdinalIgnoreCase);

        ctx.Context.Response.Headers.CacheControl = longLived
            ? "public, max-age=31536000, immutable"
            : "public, max-age=86400";
    },
});

app.UseRouting();
app.UseAntiforgery();
app.MapRazorPages();

app.Run();
return 0;

/// <summary>نقطه‌ی ورود خط فرمان برای ساخت هش رمز.</summary>
internal static class HashPasswordCommand
{
    public static int Run()
    {
        var pw = Prompt("رمز پنل مدیریت: ");
        if (pw.Length < 10)
        {
            Console.Error.WriteLine("رمز کوتاه است — حداقل ۱۰ کاراکتر بگذار.");
            return 1;
        }
        var again = Prompt("دوباره برای اطمینان: ");
        if (pw != again)
        {
            Console.Error.WriteLine("دو رمز یکی نبودند.");
            return 1;
        }

        var secret = Convert.ToBase64String(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32))
            .TrimEnd('=').Replace('+', '-').Replace('/', '_');

        Console.WriteLine();
        Console.WriteLine("این دو خط را در فایل .env کنار برنامه (یا در متغیرهای محیطی هاست) بگذار:");
        Console.WriteLine();
        Console.WriteLine($"ADMIN_PASSWORD_HASH={AdminAuth.HashPassword(pw)}");
        Console.WriteLine($"ADMIN_SESSION_SECRET={secret}");
        Console.WriteLine();
        Console.WriteLine("(.env در .gitignore هست، پس وارد مخزن نمی‌شود.)");
        Console.WriteLine("بعد از تغییر .env باید برنامه را دوباره اجرا کنی.");
        return 0;
    }

    /// <summary>ورودی را با ستاره نشان می‌دهد تا رمز روی صفحه نیفتد.</summary>
    private static string Prompt(string question)
    {
        Console.Write(question);

        // اگر ورودی از ترمینال واقعی نمی‌آید (لوله، اسکریپت، CI)، ReadKey
        // کار نمی‌کند. در آن حالت خط را عادی می‌خوانیم — ستاره نشان دادن
        // آن‌جا معنایی هم ندارد.
        if (Console.IsInputRedirected)
        {
            var line = Console.ReadLine() ?? "";
            Console.WriteLine();
            return line;
        }

        var buf = new System.Text.StringBuilder();
        while (true)
        {
            var key = Console.ReadKey(intercept: true);
            if (key.Key == ConsoleKey.Enter) { Console.WriteLine(); break; }
            if (key.Key == ConsoleKey.Backspace)
            {
                if (buf.Length > 0) { buf.Length--; Console.Write("\b \b"); }
                continue;
            }
            if (char.IsControl(key.KeyChar)) continue;
            buf.Append(key.KeyChar);
            Console.Write('*');
        }
        return buf.ToString();
    }
}

/// <summary>
/// خواننده‌ی ساده‌ی فایل `.env`.
///
/// عمداً یک پیاده‌سازی چندخطیِ خودمان است و نه یک پکیج بیرونی: پروژه هیچ
/// وابستگی NuGet ندارد تا روی هر سروری (حتی بدون دسترسی به nuget.org) بیلد
/// بگیرد.
/// </summary>
internal static class DotEnv
{
    /// <summary>
    /// مقدارهای `.env` را *فقط برای کلیدهایی که هنوز مقدار ندارند* می‌گذارد.
    ///
    /// قبلاً این متد منبعِ خودش را به انتهای زنجیره اضافه می‌کرد و در
    /// <c>IConfiguration</c> آخرین منبع برنده است — یعنی یک فایل `.env`
    /// جامانده روی سرور، بی‌صدا متغیرهای محیطیِ خودِ هاست را هم بی‌اثر
    /// می‌کرد. سناریوی واقعی‌اش این است: رمز پنل را از پنلِ هاست عوض می‌کنی،
    /// کار نمی‌کند، و هیچ سرنخی هم نیست که چرا.
    ///
    /// حالا ترتیب درست است: متغیر محیطی و آرگومان خط فرمان بر `.env` مقدم‌اند
    /// و `.env` فقط جای خالی‌ها را پر می‌کند — که همان کاری است که همه از یک
    /// فایل `.env` انتظار دارند.
    /// </summary>
    public static void LoadInto(ConfigurationManager config, string path)
    {
        if (!File.Exists(path)) return;
        var values = new Dictionary<string, string?>();
        foreach (var raw in File.ReadAllLines(path))
        {
            var line = raw.Trim();
            if (line.Length == 0 || line.StartsWith('#')) continue;
            var eq = line.IndexOf('=');
            if (eq <= 0) continue;
            var key = line[..eq].Trim();
            var value = line[(eq + 1)..].Trim();
            if (value.Length >= 2 && value[0] == '"' && value[^1] == '"') value = value[1..^1];
            // کلیدی که از قبل مقدار دارد (متغیر محیطی، خط فرمان، appsettings)
            // دست‌نخورده می‌ماند.
            if (string.IsNullOrEmpty(config[key])) values[key] = value;
        }
        if (values.Count > 0) config.AddInMemoryCollection(values);
    }
}
