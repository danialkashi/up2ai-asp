using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Up2Ai.Pages;

public class NotFoundModel : PageModel
{
    public string? RequestPath { get; set; }

    public void OnGet()
    {
        Response.StatusCode = 404;
        RequestPath = Request.Path.ToString();
    }
}
