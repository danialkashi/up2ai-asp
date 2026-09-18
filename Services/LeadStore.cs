using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using Up2Ai.Data;

namespace Up2Ai.Services;

/// <summary>Lead workflow status constants.</summary>
public static class LeadStatus
{
    public const string New = "new";
    public const string Contacted = "contacted";
    public const string FollowUp = "follow_up";
    public const string Done = "done";

    public static readonly string[] All = { New, Contacted, FollowUp, Done };

    public static bool IsValid(string? status) => All.Contains(status);
}

public sealed class Lead
{
    [JsonPropertyName("id")] public string Id { get; set; } = "";
    [JsonPropertyName("at")] public string At { get; set; } = "";
    [JsonPropertyName("name")] public string Name { get; set; } = "";
    [JsonPropertyName("reach")] public string Reach { get; set; } = "";
    [JsonPropertyName("business")] public string Business { get; set; } = "";
    [JsonPropertyName("service")] public string Service { get; set; } = "";
    [JsonPropertyName("need")] public string Need { get; set; } = "";
    [JsonPropertyName("status")] public string? Status { get; set; }
    [JsonPropertyName("lastContactedAt")] public string? LastContactedAt { get; set; }
    [JsonPropertyName("internalNotes")] public string? InternalNotes { get; set; }
    [JsonPropertyName("handled")] [JsonIgnore(Condition = JsonIgnoreCondition.WhenWriting)]
    public bool Handled { get; set; }
}

public sealed class LeadStore
{
    private readonly AppDbContext _db;
    private readonly ILogger<LeadStore> _log;

    public LeadStore(AppDbContext db, ILogger<LeadStore> log)
    {
        _db = db;
        _log = log;
    }

    public async Task<Lead> AddAsync(string name, string reach, string business, string service, string need)
    {
        var entity = new LeadEntity
        {
            Id = Guid.NewGuid().ToString(),
            CreatedAt = DateTimeOffset.UtcNow,
            Name = name.Trim(),
            Reach = reach.Trim(),
            Business = string.IsNullOrWhiteSpace(business) ? null : business.Trim(),
            Service = service.Trim(),
            Need = need.Trim(),
            Status = LeadStatus.New,
            InternalNotes = null,
            LastContactedAt = null,
        };

        _db.Leads.Add(entity);
        await _db.SaveChangesAsync();

        return entity.ToModel();
    }

    public async Task<List<Lead>> ListAsync()
    {
        var rows = await _db.Leads
            .AsNoTracking()
            .OrderByDescending(x => x.CreatedAt)
            .ToListAsync();

        return rows.Select(x => x.ToModel()).ToList();
    }

    public List<Lead> List() => ListAsync().GetAwaiter().GetResult();

    public async Task<bool> SetStatusAsync(string id, string status)
    {
        if (!LeadStatus.IsValid(status)) return false;

        var entity = await _db.Leads.FindAsync(id);
        if (entity is null) return false;

        entity.Status = status;
        await _db.SaveChangesAsync();
        return true;
    }

    public async Task<bool> SetContactedAsync(string id)
    {
        var entity = await _db.Leads.FindAsync(id);
        if (entity is null) return false;

        entity.LastContactedAt = DateTimeOffset.UtcNow;
        entity.Status = LeadStatus.Contacted;
        await _db.SaveChangesAsync();
        return true;
    }

    public async Task<bool> SetNotesAsync(string id, string? notes)
    {
        var entity = await _db.Leads.FindAsync(id);
        if (entity is null) return false;

        var maxNotes = 5000;
        if (!string.IsNullOrEmpty(notes) && notes.Length > maxNotes)
            notes = notes.Substring(0, maxNotes);

        entity.InternalNotes = string.IsNullOrWhiteSpace(notes) ? null : notes.Trim();
        await _db.SaveChangesAsync();
        return true;
    }

    public async Task<bool> DeleteAsync(string id)
    {
        var entity = await _db.Leads.FindAsync(id);
        if (entity is null) return false;

        _db.Leads.Remove(entity);
        await _db.SaveChangesAsync();
        return true;
    }

    public static string ToCsv(IEnumerable<Lead> leads)
    {
        var fa = new CultureInfo("fa-IR");
        string Esc(string s) => "\"" + s.Replace("\"", "\"\"") + "\"";

        var headers = new[] { "تاریخ", "نام", "راه ارتباطی", "کسب‌وکار", "حوزه", "نیاز", "وضعیت", "آخرین تماس", "یادداشت" };
        var lines = new List<string> { string.Join(",", headers.Select(Esc)) };

        foreach (var l in leads)
        {
            var when = DateTimeOffset.TryParse(l.At, out var dt)
                ? dt.ToLocalTime().ToString("d MMMM yyyy'، 'H:mm", fa)
                : l.At;

            var lastContacted = string.IsNullOrEmpty(l.LastContactedAt)
                ? ""
                : (DateTimeOffset.TryParse(l.LastContactedAt, out var ldt)
                    ? ldt.ToLocalTime().ToString("d MMMM yyyy'، 'H:mm", fa)
                    : l.LastContactedAt);

            lines.Add(string.Join(",", new[]
            {
                when, l.Name, l.Reach, l.Business, l.Service, l.Need,
                StatusToPersian(l.Status ?? LeadStatus.New), lastContacted, l.InternalNotes ?? "",
            }.Select(Esc)));
        }

        return "﻿" + string.Join("\r\n", lines);
    }

    private static string StatusToPersian(string status) => status switch
    {
        LeadStatus.New => "جدید",
        LeadStatus.Contacted => "تماس گرفته شد",
        LeadStatus.FollowUp => "پیگیری مورد نیاز",
        LeadStatus.Done => "انجام شده",
        _ => status,
    };
}
