using Microsoft.AspNetCore.Mvc;
using System.Text;
using Up2Ai.Models;
using Up2Ai.Services;

namespace Up2Ai.Controllers;

public class HomeController : Controller
{
    private readonly ContentStore _store;
    private readonly BlogStore _blog;
    private readonly LeadStore _leads;
    private readonly FormGuard _guard;
    private readonly IConfiguration _config;
    private readonly ILogger<HomeController> _log;

    public HomeController(ContentStore store, BlogStore blog, LeadStore leads,
                          FormGuard guard, IConfiguration config, ILogger<HomeController> log)
    {
        _store = store;
        _blog = blog;
        _leads = leads;
        _guard = guard;
        _config = config;
        _log = log;
    }

    private Cv LoadContent()
    {
        return new Cv(_store.Get());
    }

    private string RequestIp() => ClientIp.Of(HttpContext, _config);

    /// <summary>Home page with contact form</summary>
    [HttpGet("/")]
    public IActionResult Index()
    {
        var content = LoadContent();
        ViewData["Content"] = content;

        var model = new IndexViewModel
        {
            Captcha = _guard.NewChallenge(),
            FormStamp = _guard.StampNow(),
            DemoJson = content["demo"]["scenarios"].Node?.ToJsonString(new System.Text.Json.JsonSerializerOptions
            {
                Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
            }) ?? "[]"
        };

        // Check for success message from TempData (Post-Redirect-Get pattern)
        if (TempData["lead.sent"] is string blob)
        {
            model.Submitted = true;
            var parts = blob.Split('\u001f');
            if (parts.Length == 5)
            {
                model.Name = parts[0];
                model.Reach = parts[1];
                model.Business = parts[2];
                model.Service = parts[3];
                model.Need = parts[4];
            }
        }
        
        // Check for error message from TempData
        if (TempData["lead.error"] is string error)
        {
            model.FormError = error;
        }
        
        // Check for field errors from TempData
        if (TempData["lead.fieldErrors"] is string fieldErrorsJson)
        {
            try
            {
                var fieldErrors = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, string>>(fieldErrorsJson);
                if (fieldErrors != null)
                {
                    foreach (var kvp in fieldErrors)
                    {
                        model.FieldErrors[kvp.Key] = kvp.Value;
                    }
                }
            }
            catch { }
        }
        
        // Check for form data from TempData
        if (TempData["lead.formData"] is string formData)
        {
            var parts = formData.Split('\u001f');
            if (parts.Length >= 1) model.Name = parts[0];
            if (parts.Length >= 2) model.Reach = parts[1];
            if (parts.Length >= 3) model.Business = parts[2];
            if (parts.Length >= 4) model.Service = parts[3];
            if (parts.Length >= 5) model.Need = parts[4];
        }

        return View(model);
    }

    /// <summary>Submit contact form</summary>
    [HttpPost("/")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SubmitContact(IndexViewModel model)
    {
        var content = LoadContent();
        ViewData["Content"] = content;

        var name = (model.Name ?? "").Trim();
        var reach = (model.Reach ?? "").Trim();
        var business = (model.Business ?? "").Trim();
        var service = (model.Service ?? "").Trim();
        var need = (model.Need ?? "").Trim();

        var cc = content["copy"]["contact"];
        var errors = cc["errors"];
        var ip = RequestIp();

        // Layer 1: Honeypot and rate limiting (cheap checks first)
        if (_guard.HoneypotTripped(model.Honeypot) || _guard.FilledTooFast(model.StartedStamp))
        {
            _log.LogInformation("[leads] Suspicious submission rejected (honeypot/timing) from {Ip}", ip);
            TempData["lead.error"] = errors["spam"].S;
            return RedirectToAction("Index", new { fragment = "contact" });
        }

        // Layer 2: Send rate limit per IP
        if (!_guard.WithinSendLimit(ip))
        {
            _log.LogInformation("[leads] Send limit exceeded for {Ip}", ip);
            TempData["lead.error"] = errors["tooMany"].S;
            return RedirectToAction("Index", new { fragment = "contact" });
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
        if (name.Length < 2)
            model.FieldErrors["name"] = errors["name"].S;
        if (reach.Length < 5)
            model.FieldErrors["reach"] = errors["reach"].S;
        if (need.Length < 10)
            model.FieldErrors["need"] = errors["need"].S;

        if (model.FieldErrors.Count > 0)
        {
            // Save form data to TempData for POST-Redirect-Get pattern
            TempData["lead.formData"] = string.Join('\u001f', name, reach, business, service, need);
            TempData["lead.fieldErrors"] = System.Text.Json.JsonSerializer.Serialize(model.FieldErrors);
            return RedirectToAction("Index", new { fragment = "contact" });
        }

        try
        {
            _log.LogInformation("[leads] Attempting to add lead: name={Name}, reach={Reach}", name, reach);
            var addedLead = await _leads.AddAsync(name, reach, business, service, need);
            _log.LogInformation("[leads] Lead added successfully: id={Id}, name={Name}", addedLead.Id, addedLead.Name);
            
            _guard.CountSend(ip);

            // Post-Redirect-Get pattern
            TempData["lead.sent"] = string.Join('\u001f', name, reach, business, service, need);
            _log.LogInformation("[leads] Setting success TempData and redirecting");
            return RedirectToAction("Index", new { fragment = "contact" });
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "[leads] CRITICAL: Failed to save lead - name={Name}, reach={Reach}", name, reach);
            TempData["lead.error"] = cc["errorNote"].S;
            return RedirectToAction("Index", new { fragment = "contact" });
        }
    }

    /// <summary>Sitemap.xml - dynamically generated from content</summary>
    [HttpGet("/sitemap.xml")]
    public IActionResult Sitemap()
    {
        var url = System.Security.SecurityElement.Escape(
            new Cv(_store.Get())["site"]["url"].S.TrimEnd('/')) ?? "";

        var xml = new StringBuilder();
        xml.Append("<?xml version=\"1.0\" encoding=\"UTF-8\"?>\n");
        xml.Append("<urlset xmlns=\"http://www.sitemaps.org/schemas/sitemap/0.9\">\n");
        xml.Append($"<url>\n<loc>{url}/</loc>\n<changefreq>monthly</changefreq>\n<priority>1</priority>\n</url>\n");

        var posts = _blog.Published();
        xml.Append($"<url>\n<loc>{url}/blog</loc>\n<changefreq>weekly</changefreq>\n<priority>0.8</priority>\n</url>\n");

        foreach (var post in posts)
        {
            var loc = System.Security.SecurityElement.Escape($"{url}/blog/{Uri.EscapeDataString(post.Slug)}") ?? "";
            var lastMod = post.UpdatedAt.Length > 0 ? post.UpdatedAt : post.PublishedAt;
            xml.Append($"<url>\n<loc>{loc}</loc>\n<lastmod>{lastMod}</lastmod>\n<changefreq>monthly</changefreq>\n<priority>0.7</priority>\n</url>\n");
        }

        xml.Append("</urlset>");
        return Content(xml.ToString(), "application/xml; charset=utf-8");
    }

    /// <summary>Robots.txt - dynamically generated from content</summary>
    [HttpGet("/robots.txt")]
    public IActionResult Robots()
    {
        var content = new Cv(_store.Get());
        var url = content["site"]["url"].S.TrimEnd('/');

        var txt = new StringBuilder();
        txt.Append("User-agent: *\n");
        txt.Append("Allow: /\n");
        txt.Append($"Sitemap: {url}/sitemap.xml\n");

        return Content(txt.ToString(), "text/plain; charset=utf-8");
    }

    /// <summary>SEO metadata page</summary>
    [HttpGet("/seo")]
    public IActionResult Seo()
    {
        var content = LoadContent();
        ViewData["Content"] = content;
        ViewData["NoIndex"] = true;
        return View();
    }

    /// <summary>Error page</summary>
    [Route("/error")]
    [Route("/error/{statusCode}")]
    public IActionResult Error(int? statusCode = null)
    {
        var content = LoadContent();
        ViewData["Content"] = content;
        ViewData["NoIndex"] = true;

        var model = new ErrorViewModel
        {
            RequestId = HttpContext.TraceIdentifier,
            OriginalStatusCode = statusCode ?? Response.StatusCode,
        };

        return View(model);
    }
}
