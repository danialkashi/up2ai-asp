using Up2Ai.Data;
using Up2Ai.Services;
using Microsoft.EntityFrameworkCore;

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
builder.Services.AddSingleton<AdminAuth>();

var connectionString = builder.Configuration.GetConnectionString("DefaultConnection")
    ?? Environment.GetEnvironmentVariable("UP2AI_POSTGRES_CONNECTION")
    ?? throw new InvalidOperationException("PostgreSQL connection string is missing. Set ConnectionStrings:DefaultConnection or UP2AI_POSTGRES_CONNECTION.");

builder.Services.AddDbContext<AppDbContext>(options =>
{
    options.UseNpgsql(connectionString);
});

builder.Services.AddScoped<LeadStore>();
builder.Services.AddAntiforgery();

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
    
    // ForwardedHeaders: support reverse proxy (Nginx, Apache) that sends X-Forwarded-* headers
    app.UseForwardedHeaders();
    
    // HSTS: tell browsers to always use HTTPS for this domain
    app.UseHsts();
}

// Security headers for all responses
app.Use(async (context, next) =>
{
    // Prevent clickjacking: only allow framing in same origin
    context.Response.Headers["X-Frame-Options"] = "SAMEORIGIN";
    
    // Prevent MIME sniffing: browsers must respect Content-Type header
    context.Response.Headers["X-Content-Type-Options"] = "nosniff";
    
    // Referrer-Policy: send referrer only for same-origin requests
    context.Response.Headers["Referrer-Policy"] = "strict-origin-when-cross-origin";
    
    // Content Security Policy: restrict resource loading to mitigate XSS
    // Allow inline styles for admin panel Tailwind, but not inline scripts
    context.Response.Headers["Content-Security-Policy"] = 
        "default-src 'self'; " +
        "script-src 'self'; " +
        "style-src 'self' 'unsafe-inline'; " +
        "img-src 'self' data: https:; " +
        "font-src 'self' data:; " +
        "connect-src 'self'; " +
        "frame-ancestors 'self'; " +
        "base-uri 'self'; " +
        "form-action 'self'";
    
    await next();
});

// Custom 404 handling: reexecute to /404 page when a route is not found
app.UseStatusCodePagesWithReExecute("/404", "?statusCode={0}");

// Redirect HTTP to HTTPS in production
if (!app.Environment.IsDevelopment())
{
    app.UseHttpsRedirection();
}

app.UseStaticFiles();
app.UseRouting();
app.UseAntiforgery();

// Redirect old admin login route to the canonical route
app.MapGet("/admin/login", () => Results.Redirect("/admin/account/login", permanent: true));

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
    public static void LoadInto(IConfigurationBuilder config, string path)
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
            values[key] = value;
        }
        config.AddInMemoryCollection(values);
    }
}
