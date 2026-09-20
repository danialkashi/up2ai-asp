using Microsoft.AspNetCore.Mvc;
using Up2Ai.Models;
using Up2Ai.Services;

namespace Up2Ai.Controllers;

public class BlogController : Controller
{
    private const int DefaultPageSize = 10;
    private const int MaxCommentLength = 2000;
    private const int MaxNameLength = 60;

    private readonly ContentStore _store;
    private readonly BlogStore _blog;
    private readonly FormGuard _guard;
    private readonly IConfiguration _config;
    private readonly ILogger<BlogController> _log;

    public BlogController(ContentStore store, BlogStore blog, FormGuard guard,
                          IConfiguration config, ILogger<BlogController> log)
    {
        _store = store;
        _blog = blog;
        _guard = guard;
        _config = config;
        _log = log;
    }

    private Cv LoadContent()
    {
        return new Cv(_store.Get());
    }

    private string RequestIp() => ClientIp.Of(HttpContext, _config);

    /// <summary>Blog posts list with pagination</summary>
    [HttpGet("/blog")]
    public IActionResult Index([FromQuery] int? page)
    {
        var content = LoadContent();
        ViewData["Content"] = content;

        var pageSize = int.TryParse(content["copy"]["blog"]["pageSize"].S, out var n)
            ? Math.Clamp(n, 3, 50)
            : DefaultPageSize;

        var all = _blog.Published();
        var totalPages = Math.Max(1, (int)Math.Ceiling(all.Count / (double)pageSize));
        var pageNumber = Math.Clamp(page ?? 1, 1, totalPages);
        var posts = all.Skip((pageNumber - 1) * pageSize).Take(pageSize).ToList();
        var commentCounts = _blog.ApprovedCounts();

        var model = new BlogIndexViewModel
        {
            C = content,
            Posts = posts,
            CommentCounts = commentCounts,
            PageNumber = pageNumber,
            TotalPages = totalPages,
            PageSize = pageSize
        };

        return View(model);
    }

    /// <summary>Individual blog post with comments</summary>
    [HttpGet("/blog/{slug}")]
    public IActionResult Post(string slug)
    {
        var post = _blog.BySlug(slug);
        if (post is null)
            return NotFound();

        var content = LoadContent();
        ViewData["Content"] = content;

        var model = new BlogPostViewModel
        {
            C = content,
            Post = post,
            Comments = _blog.ApprovedFor(post.Id),
            BodyHtml = MiniMarkdown.BodyToHtml(post.Body),
            Captcha = _guard.NewChallenge(),
            FormStamp = _guard.StampNow(),
        };

        // Check for success message from TempData
        if (TempData["comment.sent"] is true)
            model.JustSubmitted = true;

        return View(model);
    }

    /// <summary>Submit a comment on a blog post</summary>
    [HttpPost("/blog/{slug}/comment")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SubmitComment(string slug, BlogPostViewModel model)
    {
        var post = _blog.BySlug(slug);
        if (post is null)
            return NotFound();

        var content = LoadContent();
        ViewData["Content"] = content;
        model.C = content;
        model.Post = post;
        model.BodyHtml = MiniMarkdown.ToHtml(post.Body);

        var errors = content["copy"]["blog"]["errors"];
        var ip = RequestIp();

        // Check if comments are closed
        if (!post.CommentsOpen)
        {
            model.FormError = content["copy"]["blog"]["commentsClosed"].S;
            model.Captcha = _guard.NewChallenge();
            model.FormStamp = _guard.StampNow();
            model.CaptchaAnswer = "";
            return View("Post", model);
        }

        // Layer 1: Honeypot and timing
        if (_guard.HoneypotTripped(model.Honeypot) || _guard.FilledTooFast(model.StartedStamp))
        {
            _log.LogInformation("[blog] Suspicious comment rejected (honeypot/timing) from {Ip}", ip);
            model.FormError = errors["spam"].S;
            model.Captcha = _guard.NewChallenge();
            model.FormStamp = _guard.StampNow();
            model.CaptchaAnswer = "";
            return View("Post", model);
        }

        // Layer 2: Send rate limit (per hour for comments)
        if (!_guard.WithinSendLimit(ip, FormGuard.CommentBucket, FormGuard.MaxCommentsPerHour))
        {
            _log.LogInformation("[blog] Comment rate limit exceeded for {Ip}", ip);
            model.FormError = errors["tooMany"].S;
            model.Captcha = _guard.NewChallenge();
            model.FormStamp = _guard.StampNow();
            model.CaptchaAnswer = "";
            return View("Post", model);
        }

        // Layer 3: CAPTCHA
        var captcha = _guard.CheckCaptcha(model.CaptchaToken, model.CaptchaAnswer);
        if (captcha != FormGuard.CaptchaResult.Ok)
        {
            model.FieldErrors["captcha"] = captcha == FormGuard.CaptchaResult.Expired
                ? errors["captchaExpired"].S
                : errors["captcha"].S;
        }

        // Layer 4: Field validation
        var name = (model.CommentName ?? "").Trim();
        var body = (model.CommentBody ?? "").Trim();

        if (name.Length < 2 || name.Length > MaxNameLength)
            model.FieldErrors["name"] = errors["name"].S;
        if (body.Length < 5)
            model.FieldErrors["body"] = errors["body"].S;
        else if (body.Length > MaxCommentLength)
            model.FieldErrors["body"] = errors["bodyLong"].S;

        if (model.FieldErrors.Count > 0)
        {
            model.Captcha = _guard.NewChallenge();
            model.FormStamp = _guard.StampNow();
            model.CaptchaAnswer = "";
            return View("Post", model);
        }

        try
        {
            await _blog.AddCommentAsync(post.Id, name, body);
            _guard.CountSend(ip, FormGuard.CommentBucket);

            // Post-Redirect-Get pattern
            TempData["comment.sent"] = true;
            return RedirectToAction("Post", new { slug = Uri.EscapeDataString(post.Slug), fragment = "comments" });
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "[blog] Failed to save comment");
            model.FormError = errors["failed"].S;
            model.Captcha = _guard.NewChallenge();
            model.FormStamp = _guard.StampNow();
            model.CaptchaAnswer = "";
        }

        return View("Post", model);
    }

    /// <summary>RSS feed of blog posts</summary>
    [HttpGet("/blog/feed.xml")]
    public IActionResult Feed()
    {
        var c = new Cv(_store.Get());
        var site = c["site"];
        var b = c["copy"]["blog"];
        var baseUrl = site["url"].S.TrimEnd('/');
        var posts = _blog.Published().Take(20).ToList();

        var xml = new System.Text.StringBuilder();
        xml.Append("<?xml version=\"1.0\" encoding=\"UTF-8\"?>\n");
        xml.Append("<rss version=\"2.0\" xmlns:atom=\"http://www.w3.org/2005/Atom\">\n<channel>\n");
        xml.Append($"<title>{Escape($"{b["heading"].S} — {site["name"].S}")}</title>\n");
        xml.Append($"<link>{Escape($"{baseUrl}/blog")}</link>\n");
        xml.Append($"<description>{Escape(b["lead"].S)}</description>\n");
        xml.Append("<language>fa-IR</language>\n");
        xml.Append($"<atom:link href=\"{Escape($"{baseUrl}/blog/feed.xml")}\" rel=\"self\" type=\"application/rss+xml\" />\n");

        foreach (var post in posts)
        {
            var link = $"{baseUrl}/blog/{Uri.EscapeDataString(post.Slug)}";
            var desc = post.Excerpt.Length > 0 ? post.Excerpt : MiniMarkdown.ToPlainText(post.Body, 160);
            xml.Append("<item>\n");
            xml.Append($"<title>{Escape(post.Title)}</title>\n");
            xml.Append($"<link>{Escape(link)}</link>\n");
            xml.Append($"<guid>{Escape(link)}</guid>\n");
            xml.Append($"<description>{Escape(desc)}</description>\n");
            xml.Append($"<pubDate>{ToRfc2822(post.PublishedAt)}</pubDate>\n");
            xml.Append("</item>\n");
        }

        xml.Append("</channel>\n</rss>");
        return Content(xml.ToString(), "application/rss+xml; charset=utf-8");
    }

    private static string Escape(string text)
    {
        return System.Security.SecurityElement.Escape(text) ?? "";
    }

    private static string ToRfc2822(string isoDate)
    {
        if (DateTime.TryParse(isoDate, out var dt))
        {
            return dt.ToString("R");
        }
        return DateTime.Now.ToString("R");
    }
}
