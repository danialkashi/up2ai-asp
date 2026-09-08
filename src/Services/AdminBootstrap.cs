using Up2Ai.Services;

namespace Up2Ai.Services;

/// <summary>
/// Bootstrap initial admin user if none exists.
/// 
/// When the app starts for the first time with no admin users:
/// - Checks for Admin:InitialPasswordHash in configuration
/// - If present, creates an admin user with that password hash
/// - If missing, fails startup with clear error message
/// - Once an admin exists in the database, InitialPasswordHash is never used again
/// </summary>
public sealed class AdminBootstrap
{
    /// <summary>
    /// Ensure at least one admin user exists, creating from InitialPasswordHash if needed.
    /// </summary>
    public static async Task EnsureAdminAsync(AdminUserStore users, IConfiguration config, ILogger logger)
    {
        // If admin user(s) already exist in the database, we're done
        if (!users.IsEmpty())
        {
            logger.LogInformation("[bootstrap] Admin users exist, skipping bootstrap");
            return;
        }

        var hash = config["Admin:InitialPasswordHash"]?.Trim();
        if (string.IsNullOrEmpty(hash))
        {
            logger.LogCritical(
                "[bootstrap] No admin user exists and Admin:InitialPasswordHash is not configured. " +
                "Set Admin:InitialPasswordHash in appsettings.json or via environment variable Admin__InitialPasswordHash");
            throw new InvalidOperationException(
                "Admin bootstrap failed: missing Admin:InitialPasswordHash configuration");
        }

        logger.LogInformation("[bootstrap] Creating initial admin user from Admin:InitialPasswordHash");

        // Create the initial admin with a temporary password, then update its hash
        var tempPassword = Guid.NewGuid().ToString();
        var result = await users.AddAsync("admin", tempPassword, "مدیر سایت");

        if (!result.Ok)
        {
            logger.LogCritical("[bootstrap] Failed to create initial admin user: {Error}", result.Error);
            throw new InvalidOperationException($"Admin bootstrap failed: {result.Error}");
        }

        // Retrieve the newly created admin and update its hash to the configured value
        var admin = users.ByUsername("admin");
        if (admin == null)
        {
            logger.LogCritical("[bootstrap] Admin user created but could not be retrieved");
            throw new InvalidOperationException("Admin bootstrap failed: user creation verification failed");
        }

        // Use a special internal method to set the password hash directly
        await users.SetPasswordHashDirectAsync(admin.Id, hash);
        logger.LogInformation("[bootstrap] Initial admin user created successfully with configured password hash");
    }
}

