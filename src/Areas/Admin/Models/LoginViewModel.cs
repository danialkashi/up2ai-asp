namespace Up2Ai.Areas.Admin.Models;

public class LoginViewModel
{
    public string Username { get; set; } = "";
    public string Password { get; set; } = "";
    public string? Error { get; set; }
    public bool ShowUsername { get; set; }
}
