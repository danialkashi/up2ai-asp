using Up2Ai.Services;
using Up2Ai.Middleware;

var builder = WebApplication.CreateBuilder(args);

// بدون این، انکودر پیش‌فرض Razor هر حرف غیرلاتین را به `&#x…;` تبدیل می‌کند:
// صفحه درست دیده می‌شود ولی حجم HTML چند برابر می‌شود و خروجی دیگر با نسخه‌ی
// قبلی یکی نیست. با این تنظیم، فارسی همان فارسی می‌ماند.
builder.Services.Configure<Microsoft.Extensions.WebEncoders.WebEncoderOptions>(o =>
    o.TextEncoderSettings = new System.Text.Encodings.Web.TextEncoderSettings(
        System.Text.Unicode.UnicodeRanges.All));

// ────────────────── Storage: PostgreSQL Mandatory ──────────────────────────
//
// PostgreSQL is the ONLY runtime persistence mechanism for application state
// (admin users, authentication, database records).
// 
// The connection string is REQUIRED and must be configured via:
// - ConnectionStrings:DefaultConnection in appsettings
// - ConnectionStrings__DefaultConnection environment variable
// - DATABASE_URL environment variable (cloud platform convention)
//
// If PostgreSQL is unavailable, the application fails at startup with a clear error
// message. There is no JSON fallback for authentication or admin state.
// 
// Website content can optionally use either PostgreSQL or JSON files, but admin
// credentials are ALWAYS persisted in PostgreSQL only.
var connectionString = StorageFactory.ConnectionStringFrom(builder.Configuration);

builder.Services.AddSingleton(sp =>
{
    var logs = sp.GetRequiredService<ILoggerFactory>();
    
    if (string.IsNullOrWhiteSpace(connectionString))
    {
        throw new InvalidOperationException(
            "ConnectionStrings:DefaultConnection must be configured. " +
            "Set it via environment variable ConnectionStrings__DefaultConnection or DATABASE_URL.");
    }

    var db = new Up2Ai.Services.Pg.PgClient(
        Up2Ai.Services.Pg.PgConnectionInfo.Parse(connectionString),
        logs.CreateLogger<Up2Ai.Services.Pg.PgClient>());
    return new StorageFactory(db, logs);
});
builder.Services.AddSingleton(sp => sp.GetRequiredService<StorageFactory>().Content());

builder.Services.AddControllersWithViews();
builder.Services.AddAuthentication("Cookies")
    .AddCookie("Cookies", options =>
    {
        options.LoginPath = "/admin/account/login";
        options.LogoutPath = "/admin/account/logout";
        options.AccessDeniedPath = "/admin/account/login";
        options.ExpireTimeSpan = TimeSpan.FromHours(12);
        options.SlidingExpiration = true;
        options.Cookie.HttpOnly = true;
        options.Cookie.SameSite = SameSiteMode.Lax;
        options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
    });
builder.Services.AddAuthorization();
builder.Services.AddSingleton<ContentStore>();
builder.Services.AddSingleton<LeadStore>();
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

// جدول‌ها را هنگام بالا آمدن تضمین می‌کنیم (همه‌ی دستورها if not exists دارند).
//
// چرا این‌جا و نه در یک ابزار مهاجرتِ جدا: schema این سایت پنج جدولِ ساده است
// و نگه داشتنش کنار کد یعنی «دیپلوی کن، کار می‌کند» — بدون قدمِ فراموش‌شدنی.
{
    var storage = app.Services.GetRequiredService<StorageFactory>();
    try
    {
        await storage.Db.ExecuteScriptAsync(Up2Ai.Services.Pg.PgSchema.CreateAll());
        app.Logger.LogInformation("[storage] {Where}", storage.Describe());
    }
    catch (Exception ex)
    {
        // اگر دیتابیس در دسترس نباشد، سایت نباید بی‌صدا با داده‌ی خالی بالا
        // بیاید — همان‌جا با پیام روشن متوقف می‌شود.
        app.Logger.LogCritical(ex, "[storage] اتصال به پستگرس ممکن نشد؛ برنامه اجرا نمی‌شود");
        throw;
    }

    // Bootstrap initial admin user if none exists
    var users = app.Services.GetRequiredService<AdminUserStore>();
    try
    {
        await Up2Ai.Services.AdminBootstrap.EnsureAdminAsync(users, builder.Configuration, app.Logger);
    }
    catch (Exception ex)
    {
        app.Logger.LogCritical(ex, "[bootstrap] Initial admin creation failed");
        throw;
    }
}

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/error");
}

// آدرسِ اشتباه باید صفحه‌ی ۴۰۴ی فارسیِ خودمان را بگیرد.
app.UseStatusCodePagesWithReExecute("/error/{0}");

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
app.UseAuthentication();
app.UsePasswordVersionValidation();
app.UseAuthorization();
app.UseAntiforgery();

// Security headers
app.Use((context, next) =>
{
    context.Response.Headers["X-Content-Type-Options"] = "nosniff";
    context.Response.Headers["X-Frame-Options"] = "DENY";
    return next();
});

app.MapControllerRoute(
    name: "areas",
    pattern: "{area:exists}/{controller=Dashboard}/{action=Index}/{id?}");

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}");

app.Run();
return 0;
