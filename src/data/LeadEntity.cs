using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Up2Ai.Services;

namespace Up2Ai.Data;

public sealed class LeadEntity
{
    [Key]
    [Column("id")]
    [MaxLength(128)]
    public string Id { get; set; } = string.Empty;

    [Column("created_at")]
    public DateTimeOffset CreatedAt { get; set; }

    [Column("name")]
    [MaxLength(200)]
    public string Name { get; set; } = string.Empty;

    [Column("reach")]
    [MaxLength(200)]
    public string Reach { get; set; } = string.Empty;

    [Column("business")]
    [MaxLength(200)]
    public string? Business { get; set; }

    [Column("service")]
    [MaxLength(200)]
    public string Service { get; set; } = string.Empty;

    [Column("need", TypeName = "text")]
    public string Need { get; set; } = string.Empty;

    [Column("status")]
    [MaxLength(32)]
    public string Status { get; set; } = LeadStatus.New;

    [Column("last_contacted_at")]
    public DateTimeOffset? LastContactedAt { get; set; }

    [Column("internal_notes", TypeName = "text")]
    public string? InternalNotes { get; set; }

    public Lead ToModel() => new()
    {
        Id = Id,
        At = CreatedAt.ToString("O"),
        Name = Name,
        Reach = Reach,
        Business = Business ?? string.Empty,
        Service = Service,
        Need = Need,
        Status = Status,
        LastContactedAt = LastContactedAt?.ToString("O"),
        InternalNotes = InternalNotes
    };

    public static LeadEntity FromModel(Lead lead) => new()
    {
        Id = lead.Id,
        CreatedAt = DateTimeOffset.TryParse(lead.At, out var createdAt) ? createdAt : DateTimeOffset.UtcNow,
        Name = lead.Name,
        Reach = lead.Reach,
        Business = string.IsNullOrWhiteSpace(lead.Business) ? null : lead.Business,
        Service = lead.Service,
        Need = lead.Need,
        Status = LeadStatus.IsValid(lead.Status) ? lead.Status! : LeadStatus.New,
        LastContactedAt = string.IsNullOrWhiteSpace(lead.LastContactedAt)
            ? null
            : DateTimeOffset.TryParse(lead.LastContactedAt, out var lastContactedAt)
                ? lastContactedAt
                : null,
        InternalNotes = lead.InternalNotes
    };
}
