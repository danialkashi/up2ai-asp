using Up2Ai.Services;

namespace Up2Ai.Models;

public class BlogPostViewModel
{
    public Cv C { get; set; } = new Cv(null);
    public Post Post { get; set; } = null!;
    public List<Comment> Comments { get; set; } = new();
    public string BodyHtml { get; set; } = "";

    public string CommentName { get; set; } = "";
    public string CommentBody { get; set; } = "";
    public string CaptchaToken { get; set; } = "";
    public string CaptchaAnswer { get; set; } = "";
    public string Honeypot { get; set; } = "";
    public string StartedStamp { get; set; } = "";

    public FormGuard.Challenge Captcha { get; set; } = null!;
    public string FormStamp { get; set; } = "";
    public string? FormError { get; set; }
    public Dictionary<string, string> FieldErrors { get; } = new();
    public bool JustSubmitted { get; set; }

    public string ArticleJsonLd(string baseUrl, string postUrl) => "";
}
