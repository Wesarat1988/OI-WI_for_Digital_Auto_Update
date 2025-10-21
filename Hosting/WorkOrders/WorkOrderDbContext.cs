using Microsoft.EntityFrameworkCore;

namespace BlazorPdfApp.WorkOrders;

public sealed class WorkOrderDbContext : DbContext
{
    public WorkOrderDbContext(DbContextOptions<WorkOrderDbContext> options)
        : base(options)
    {
    }

    public DbSet<WorkOrderEntity> WorkOrders => Set<WorkOrderEntity>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        var entity = modelBuilder.Entity<WorkOrderEntity>();
        entity.ToTable("WorkOrders", "dbo");
        entity.HasKey(x => x.Id);
        entity.Property(x => x.Id).HasMaxLength(64);
        entity.Property(x => x.Number).HasMaxLength(64);
        entity.Property(x => x.Status).HasMaxLength(32);
        entity.Property(x => x.Line).HasMaxLength(16);
        entity.Property(x => x.PartNo).HasMaxLength(64);
        entity.Property(x => x.JsonPayload).HasColumnType("nvarchar(max)");
    }
}

public sealed class WorkOrderEntity
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    public string Number { get; set; } = string.Empty;

    public string Status { get; set; } = string.Empty;

    public string Line { get; set; } = string.Empty;

    public string PartNo { get; set; } = string.Empty;

    public DateTime CreatedUtc { get; set; }

    public DateTime? DueUtc { get; set; }

    public string? JsonPayload { get; set; }
}
