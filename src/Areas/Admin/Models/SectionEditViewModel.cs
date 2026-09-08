using System.Text.Json.Nodes;
using Up2Ai.Services;

namespace Up2Ai.Areas.Admin.Models;

public class SectionEditViewModel
{
    public Cv C { get; set; } = new Cv(null);
    public string SectionKey { get; set; } = "";
    public AdminLabels.SectionMeta Meta { get; set; } = new("", "");
    public JsonNode Working { get; set; } = new JsonObject();
    public JsonNode Defaults { get; set; } = new JsonObject();

    public string? Saved { get; set; }
    public string? Error { get; set; }
    public List<string> Rejected { get; set; } = new();

    public string? NewItemPath { get; set; }
    public string? NewItemListLabel { get; set; }

    public bool ChangedFromDefault => Working.ToJsonString() != Defaults.ToJsonString();
}
