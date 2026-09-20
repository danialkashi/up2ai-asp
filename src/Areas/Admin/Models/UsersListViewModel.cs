using Up2Ai.Services;

namespace Up2Ai.Areas.Admin.Models;

public class UsersListViewModel
{
    public Cv C { get; set; } = new Cv(null);
    public string? CurrentUserId { get; set; }
    public List<AdminUser> All { get; set; } = new();
    public string? Notice { get; set; }
    public string? Error { get; set; }
    public bool EnvLoginAvailable { get; set; }

    public string NewUsername { get; set; } = "";
    public string NewPassword { get; set; } = "";
    public string NewDisplayName { get; set; } = "";

    public string PasswordUserId { get; set; } = "";
    public string NewPasswordForUser { get; set; } = "";
    public string? PasswordFormFor { get; set; }
    public string? ConfirmDeleteId { get; set; }
}
