using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Up2Ai.Services;

namespace Up2Ai.Middleware;

/// <summary>
/// Middleware that validates the password version claim against the current database value.
/// 
/// When a user's password changes, their PasswordVersion increments.
/// Any cookies issued before the change will have a stale PasswordVersion claim.
/// This middleware rejects requests with stale versions, forcing re-authentication.
/// 
/// This implements cross-session invalidation: when the password changes,
/// ALL previously issued authentication cookies become invalid immediately.
/// </summary>
public class PasswordVersionValidationMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<PasswordVersionValidationMiddleware> _logger;

    public PasswordVersionValidationMiddleware(RequestDelegate next, ILogger<PasswordVersionValidationMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context, AdminUserStore users)
    {
        // Only check authenticated requests
        if (context.User?.Identity?.IsAuthenticated ?? false)
        {
            var userIdClaim = context.User.FindFirst(ClaimTypes.NameIdentifier);
            var versionClaim = context.User.FindFirst("PasswordVersion");

            if (userIdClaim != null && versionClaim != null)
            {
                var userId = userIdClaim.Value;
                var claimedVersion = int.TryParse(versionClaim.Value, out var v) ? v : -1;

                // Get current user from database
                var currentUser = users.ById(userId);

                if (currentUser == null)
                {
                    // User no longer exists in database, sign out
                    _logger.LogWarning("PasswordVersionValidationMiddleware: User {UserId} not found in database, signing out", userId);
                    await context.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
                }
                else if (currentUser.PasswordVersion != claimedVersion)
                {
                    // Password version mismatch - password was changed, sign out
                    _logger.LogInformation(
                        "PasswordVersionValidationMiddleware: Version mismatch for user {UserId}. Claimed: {ClaimedVersion}, Current: {CurrentVersion}. Signing out.",
                        userId, claimedVersion, currentUser.PasswordVersion);
                    await context.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);

                    // Redirect to login if this was an admin request
                    if (context.Request.Path.StartsWithSegments("/admin"))
                    {
                        context.Response.Redirect($"/admin/login?returnUrl={Uri.EscapeDataString(context.Request.Path + context.Request.QueryString)}");
                        return;
                    }
                }
            }
        }

        await _next(context);
    }
}

/// <summary>
/// Extension method to register password version validation middleware
/// </summary>
public static class PasswordVersionValidationMiddlewareExtensions
{
    public static IApplicationBuilder UsePasswordVersionValidation(this IApplicationBuilder builder)
    {
        return builder.UseMiddleware<PasswordVersionValidationMiddleware>();
    }
}
