namespace Up2Ai.Areas.Admin.Models;

public class ChangePasswordViewModel
{
    public string CurrentPassword { get; set; } = "";
    public string NewPassword { get; set; } = "";
    public string? Error { get; set; }
}
