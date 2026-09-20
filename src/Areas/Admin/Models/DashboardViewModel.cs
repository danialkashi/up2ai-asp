using Up2Ai.Services;

namespace Up2Ai.Areas.Admin.Models;

public class DashboardViewModel
{
    public Cv C { get; set; } = new Cv(null);
    public int PostCount { get; set; }
    public int DraftCount { get; set; }
    public int PendingComments { get; set; }
    public int LeadCount { get; set; }
    public int UnhandledLeads { get; set; }
    public List<string> Rejected { get; set; } = new();
    public List<string> SectionKeys { get; set; } = new();

    public bool IsEdited(string key) => Rejected.Contains(key);
}
