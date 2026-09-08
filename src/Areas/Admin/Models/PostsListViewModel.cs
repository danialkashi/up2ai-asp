using Up2Ai.Services;

namespace Up2Ai.Areas.Admin.Models;

public class PostsListViewModel
{
    public List<Post> Posts { get; set; } = new();
    public Dictionary<string, int> CommentCounts { get; set; } = new();
    public string? Notice { get; set; }
    public string? ConfirmDeleteId { get; set; }
}
