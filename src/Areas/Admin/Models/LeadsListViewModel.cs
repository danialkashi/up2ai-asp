using Up2Ai.Services;

namespace Up2Ai.Areas.Admin.Models;

public class LeadsListViewModel
{
    public string? Filter { get; set; }
    public bool OnlyUnhandled { get; set; }
    public List<Lead> Leads { get; set; } = new();
    public int TotalCount { get; set; }
    public int UnhandledCount { get; set; }
}
