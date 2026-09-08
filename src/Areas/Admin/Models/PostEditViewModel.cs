using Up2Ai.Services;

namespace Up2Ai.Areas.Admin.Models;

public class PostEditViewModel
{
    public string Id { get; set; } = "";
    public string Title { get; set; } = "";
    public string Slug { get; set; } = "";
    public string Excerpt { get; set; } = "";
    public string Body { get; set; } = "";
    public string TagsText { get; set; } = "";
    public bool Published { get; set; }
    public bool CommentsOpen { get; set; }

    public bool IsNew { get; set; }
    public string? Error { get; set; }
    public string? Saved { get; set; }
    public string PreviewHtml { get; set; } = "";
    public string SavedSlug { get; set; } = "";
    public bool SavedPublished { get; set; }

    public void Fill(Post post)
    {
        Id = post.Id;
        Title = post.Title;
        Slug = post.Slug;
        Excerpt = post.Excerpt;
        Body = post.Body;
        TagsText = string.Join("، ", post.Tags);
        Published = post.Published;
        CommentsOpen = post.CommentsOpen;
        SavedSlug = post.Slug;
        SavedPublished = post.Published;
        PreviewHtml = MiniMarkdown.ToHtml(post.Body);
    }
}
