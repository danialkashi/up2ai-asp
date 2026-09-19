using Microsoft.EntityFrameworkCore;
using Up2Ai.Services;

namespace Up2Ai.Data;

public sealed class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options)
    {
    }

    public DbSet<LeadEntity> Leads => Set<LeadEntity>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<LeadEntity>(entity =>
        {
            entity.ToTable("leads");

            entity.HasKey(x => x.Id);
            entity.Property(x => x.Id)
                .HasMaxLength(128)
                .IsRequired();

            entity.Property(x => x.Name)
                .HasMaxLength(200)
                .IsRequired();

            entity.Property(x => x.Reach)
                .HasMaxLength(200)
                .IsRequired();

            entity.Property(x => x.Business)
                .HasMaxLength(200)
                .IsRequired(false);

            entity.Property(x => x.Service)
                .HasMaxLength(200)
                .IsRequired();

            entity.Property(x => x.Need)
                .HasColumnType("text")
                .IsRequired();

            entity.Property(x => x.Status)
                .HasMaxLength(32)
                .HasDefaultValue(LeadStatus.New)
                .IsRequired();

            entity.Property(x => x.CreatedAt)
                .HasColumnType("timestamptz")
                .IsRequired();

            entity.Property(x => x.LastContactedAt)
                .HasColumnType("timestamptz")
                .IsRequired(false);

            entity.Property(x => x.InternalNotes)
                .HasColumnType("text")
                .IsRequired(false);

            entity.HasIndex(x => x.CreatedAt);
            entity.HasIndex(x => x.Status);
        });

        base.OnModelCreating(modelBuilder);
    }
}
