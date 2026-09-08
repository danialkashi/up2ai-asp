using Up2Ai.Services;

namespace Up2Ai.Models;

public class BlogIndexViewModel
{
    public Cv C { get; set; } = new Cv(null);
    public List<Post> Posts { get; set; } = new();
    public Dictionary<string, int> CommentCounts { get; set; } = new();
    public int PageNumber { get; set; } = 1;
    public int TotalPages { get; set; } = 1;
    public int PageSize { get; set; } = 10;
}
