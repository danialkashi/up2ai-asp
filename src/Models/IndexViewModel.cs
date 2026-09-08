using Up2Ai.Services;

namespace Up2Ai.Models;

public class IndexViewModel
{
    public string Name { get; set; } = "";
    public string Reach { get; set; } = "";
    public string Business { get; set; } = "";
    public string Service { get; set; } = "";
    public string Need { get; set; } = "";
    public string CaptchaToken { get; set; } = "";
    public string CaptchaAnswer { get; set; } = "";
    public string Honeypot { get; set; } = "";
    public string StartedStamp { get; set; } = "";

    public bool Submitted { get; set; }
    public string? FormError { get; set; }
    public Dictionary<string, string> FieldErrors { get; } = new();

    public FormGuard.Challenge Captcha { get; set; } = null!;
    public string FormStamp { get; set; } = "";
    public string DemoJson { get; set; } = "[]";
}
