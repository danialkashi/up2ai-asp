using System.Text.Json.Serialization;

namespace Up2Ai.Services;

/// <summary>یک کاربرِ پنل مدیریت.</summary>
public sealed class AdminUser
{
    [JsonPropertyName("id")] public string Id { get; set; } = "";
    [JsonPropertyName("username")] public string Username { get; set; } = "";
    /// <summary>فقط هشِ PBKDF2 — خودِ رمز هیچ‌جا ذخیره نمی‌شود.</summary>
    [JsonPropertyName("passwordHash")] public string PasswordHash { get; set; } = "";
    /// <summary>Version incremented on each password change. Used to invalidate all sessions when password changes.</summary>
    [JsonPropertyName("passwordVersion")] public int PasswordVersion { get; set; } = 1;
    [JsonPropertyName("displayName")] public string DisplayName { get; set; } = "";
    [JsonPropertyName("createdAt")] public string CreatedAt { get; set; } = "";
    [JsonPropertyName("lastLoginAt")] public string LastLoginAt { get; set; } = "";
    [JsonPropertyName("active")] public bool Active { get; set; } = true;

    /// <summary>Computed display label. Not persisted.</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public string Label => DisplayName.Length > 0 ? DisplayName : Username;
}

/// <summary>
/// Admin users stored in PostgreSQL.
///
/// Each admin user has a username, hashed password, and optional display name.
/// Initial admin is created by AdminBootstrap using Admin:InitialPasswordHash configuration.
/// Passwords are stored using PBKDF2-SHA256 with 210,000 iterations (OWASP recommended).
/// </summary>
public sealed class AdminUserStore
{
    /// <summary>شناسه‌ی مجازیِ ورود با رمزِ محیطی — کاربرِ واقعی نیست.</summary>
    public const string EnvUserId = "env";

    private readonly IRecordStore<AdminUser> _store;

    public AdminUserStore(StorageFactory storage, ILogger<AdminUserStore> log)
    {
        _store = storage.Records<AdminUser>(Pg.PgSchema.AdminUsers, u => u.Id,
            u => u.Id.Length > 0 && u.Username.Length > 0 && u.PasswordHash.Length > 0, log);
    }

    public List<AdminUser> List() =>
        _store.Read().OrderBy(u => u.CreatedAt, StringComparer.Ordinal).ToList();

    public int Count() => _store.Read().Count;

    /// <summary>هیچ کاربری ساخته نشده؟ آن‌وقت ورودِ محیطی تنها راهِ ورود است.</summary>
    public bool IsEmpty() => Count() == 0;

    public AdminUser? ById(string id) => _store.Read().FirstOrDefault(u => u.Id == id);

    public AdminUser? ByUsername(string username) =>
        _store.Read().FirstOrDefault(u =>
            string.Equals(u.Username, username.Trim(), StringComparison.OrdinalIgnoreCase));

    /* -------------------------------- ورود -------------------------------- */

    /// <summary>
    /// نام کاربری و رمز را می‌سنجد.
    ///
    /// نکته‌ی امنیتی: وقتی کاربر پیدا نشود هم یک هشِ ساختگی بررسی می‌شود.
    /// بدون آن، پاسخِ «کاربر نیست» خیلی سریع‌تر از «رمز غلط» برمی‌گشت و از
    /// روی همین تفاوتِ زمان می‌شد فهمید کدام نام کاربری واقعی است.
    /// </summary>
    public AdminUser? Verify(string username, string password)
    {
        var user = ByUsername(username);
        if (user is null || !user.Active)
        {
            AdminAuth.VerifyPassword(password, DummyHash);
            return null;
        }
        return AdminAuth.VerifyPassword(password, user.PasswordHash) ? user : null;
    }

    // هشِ یک رمزِ تصادفی، فقط برای هم‌زمان کردنِ مسیرِ «کاربر پیدا نشد».
    private static readonly string DummyHash = AdminAuth.HashPassword(Guid.NewGuid().ToString());

    public Task TouchLoginAsync(string id) => _store.MutateAsync(list =>
    {
        var user = list.FirstOrDefault(u => u.Id == id);
        if (user is null) return (false, false);
        user.LastLoginAt = BlogStore.Iso(DateTime.UtcNow);
        return (true, true);
    });

    /* ------------------------------ مدیریت ------------------------------ */

    public sealed record Result(bool Ok, string? Error = null);

    public Task<Result> AddAsync(string username, string password, string displayName)
    {
        var name = username.Trim();
        var invalid = ValidateUsername(name) ?? ValidatePassword(password);
        if (invalid is not null) return Task.FromResult(new Result(false, invalid));

        return _store.MutateAsync(list =>
        {
            if (list.Any(u => string.Equals(u.Username, name, StringComparison.OrdinalIgnoreCase)))
                return (false, new Result(false, "این نام کاربری قبلاً ثبت شده."));

            list.Add(new AdminUser
            {
                Id = Guid.NewGuid().ToString(),
                Username = name,
                PasswordHash = AdminAuth.HashPassword(password),
                DisplayName = displayName.Trim(),
                CreatedAt = BlogStore.Iso(DateTime.UtcNow),
                Active = true,
            });
            return (true, new Result(true));
        });
    }

    public Task<Result> SetPasswordAsync(string id, string password)
    {
        var invalid = ValidatePassword(password);
        if (invalid is not null) return Task.FromResult(new Result(false, invalid));

        return _store.MutateAsync(list =>
        {
            var user = list.FirstOrDefault(u => u.Id == id);
            if (user is null) return (false, new Result(false, "کاربر پیدا نشد."));
            
            user.PasswordHash = AdminAuth.HashPassword(password);
            user.PasswordVersion++; // Increment version to invalidate all sessions
            
            return (true, new Result(true));
        });
    }

    /// <summary>
    /// Change password with verification of current password.
    /// Required for self-service password change - user must prove knowledge of current password.
    /// </summary>
    public async Task<Result> ChangePasswordAsync(string id, string currentPassword, string newPassword)
    {
        var user = ById(id);
        if (user is null)
        {
            return new Result(false, "کاربر پیدا نشد.");
        }

        // Verify current password
        if (!AdminAuth.VerifyPassword(currentPassword, user.PasswordHash))
        {
            return new Result(false, "رمز کنونی نادرست است.");
        }

        // Validate new password
        var invalid = ValidatePassword(newPassword);
        if (invalid is not null)
        {
            return new Result(false, invalid);
        }

        // Persist new password
        var result = await SetPasswordAsync(id, newPassword);
        return result;
    }

    /// <summary>
    /// Set password hash directly (used for bootstrap with pre-computed hash).
    /// Internal use only.
    /// </summary>
    public Task<Result> SetPasswordHashDirectAsync(string id, string hash)
    {
        if (hash.Length < 20) return Task.FromResult(new Result(false, "Invalid hash format."));

        return _store.MutateAsync(list =>
        {
            var user = list.FirstOrDefault(u => u.Id == id);
            if (user is null) return (false, new Result(false, "کاربر پیدا نشد."));
            user.PasswordHash = hash;
            return (true, new Result(true));
        });
    }

    /// <summary>
    /// حذفِ کاربر. آخرین کاربرِ فعال حذف نمی‌شود مگر رمزِ محیطی هم تنظیم
    /// باشد — وگرنه پنل بدون هیچ راهِ ورودی می‌ماند و فقط با دسترسی به
    /// سرور می‌شد نجاتش داد.
    /// </summary>
    public Task<Result> DeleteAsync(string id, bool envLoginAvailable) => _store.MutateAsync(list =>
    {
        var user = list.FirstOrDefault(u => u.Id == id);
        if (user is null) return (false, new Result(false, "کاربر پیدا نشد."));
        if (list.Count(u => u.Active) <= 1 && user.Active && !envLoginAvailable)
            return (false, new Result(false, "این تنها کاربرِ فعال است؛ اول یک کاربر دیگر بساز."));

        list.Remove(user);
        return (true, new Result(true));
    });

    public Task<Result> SetActiveAsync(string id, bool active, bool envLoginAvailable) =>
        _store.MutateAsync(list =>
        {
            var user = list.FirstOrDefault(u => u.Id == id);
            if (user is null) return (false, new Result(false, "کاربر پیدا نشد."));
            if (!active && list.Count(u => u.Active) <= 1 && user.Active && !envLoginAvailable)
                return (false, new Result(false, "این تنها کاربرِ فعال است؛ غیرفعال کردنش پنل را قفل می‌کند."));
            if (user.Active == active) return (false, new Result(true));

            user.Active = active;
            return (true, new Result(true));
        });

    /* ----------------------------- اعتبارسنجی ----------------------------- */

    public static string? ValidateUsername(string username) => username switch
    {
        { Length: < 3 } => "نام کاربری حداقل ۳ کاراکتر باشد.",
        { Length: > 32 } => "نام کاربری حداکثر ۳۲ کاراکتر باشد.",
        _ when !username.All(ch => char.IsAsciiLetterOrDigit(ch) || ch is '.' or '_' or '-')
            => "نام کاربری فقط حروف و رقم انگلیسی و . _ - می‌پذیرد.",
        _ => null,
    };

    /// <summary>
    /// همان حداقلی که دستور `hash-password` هم می‌گیرد. طولِ ۱۰ کاراکتر
    /// عمدی است: برای پنلی که پشت اینترنت باز است، هشت کاراکتر دیگر کافی
    /// نیست.
    /// </summary>
    public static string? ValidatePassword(string password) =>
        password.Length < 10 ? "رمز حداقل ۱۰ کاراکتر باشد." : null;
}
