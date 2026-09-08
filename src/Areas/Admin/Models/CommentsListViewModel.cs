using Up2Ai.Services;

namespace Up2Ai.Areas.Admin.Models;

public class CommentsListViewModel
{
    public List<Comment> Comments { get; set; } = new();
    public Dictionary<string, Post> PostsById { get; set; } = new();
    public string Filter { get; set; } = "pending";
    public int PendingCount { get; set; }
    public int TotalCount { get; set; }
    public string? Notice { get; set; }
    public bool OnlyPending { get; set; }
}
