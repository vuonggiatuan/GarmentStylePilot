using GarmentStylePilot.Api.Domain;
using Microsoft.EntityFrameworkCore;

namespace GarmentStylePilot.Api.Data;

public sealed class GarmentDbContext(DbContextOptions<GarmentDbContext> options) : DbContext(options)
{
    public DbSet<Style> Styles => Set<Style>();
    public DbSet<Sample> Samples => Set<Sample>();
    public DbSet<BomLine> BomLines => Set<BomLine>();
    public DbSet<SewingOperation> SewingOperations => Set<SewingOperation>();
    public DbSet<CostSheet> CostSheets => Set<CostSheet>();
    public DbSet<Quote> Quotes => Set<Quote>();
    public DbSet<ReadinessCheck> ReadinessChecks => Set<ReadinessCheck>();
    public DbSet<ProductionRelease> ProductionReleases => Set<ProductionRelease>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Style>().HasIndex(x => x.Code).IsUnique();
        modelBuilder.Entity<ReadinessCheck>().HasIndex(x => new { x.StyleId, x.Code }).IsUnique();
        modelBuilder.Entity<ProductionRelease>().HasIndex(x => x.StyleId).IsUnique();

        modelBuilder.Entity<Style>().Property(x => x.Status).HasConversion<string>();
        modelBuilder.Entity<Sample>().Property(x => x.Status).HasConversion<string>();
        modelBuilder.Entity<Quote>().Property(x => x.Status).HasConversion<string>();

        modelBuilder.Entity<BomLine>().Property(x => x.Consumption).HasPrecision(18, 6);
        modelBuilder.Entity<BomLine>().Property(x => x.UnitPrice).HasPrecision(18, 4);
        modelBuilder.Entity<BomLine>().Property(x => x.WastagePercent).HasPrecision(9, 4);
        modelBuilder.Entity<SewingOperation>().Property(x => x.SmvMinutes).HasPrecision(18, 4);
        modelBuilder.Entity<CostSheet>().Property(x => x.MinuteRate).HasPrecision(18, 4);
        modelBuilder.Entity<CostSheet>().Property(x => x.OverheadPerPiece).HasPrecision(18, 4);
        modelBuilder.Entity<CostSheet>().Property(x => x.TargetMarginPercent).HasPrecision(9, 4);
        modelBuilder.Entity<CostSheet>().Property(x => x.MaterialCost).HasPrecision(18, 4);
        modelBuilder.Entity<CostSheet>().Property(x => x.LaborCost).HasPrecision(18, 4);
        modelBuilder.Entity<CostSheet>().Property(x => x.SmvMinutes).HasPrecision(18, 4);
        modelBuilder.Entity<CostSheet>().Property(x => x.TotalCost).HasPrecision(18, 4);
        modelBuilder.Entity<CostSheet>().Property(x => x.SuggestedPrice).HasPrecision(18, 4);
        modelBuilder.Entity<Quote>().Property(x => x.UnitPrice).HasPrecision(18, 4);

        modelBuilder.Entity<Sample>().HasOne<Style>().WithMany().HasForeignKey(x => x.StyleId);
        modelBuilder.Entity<BomLine>().HasOne<Style>().WithMany().HasForeignKey(x => x.StyleId);
        modelBuilder.Entity<SewingOperation>().HasOne<Style>().WithMany().HasForeignKey(x => x.StyleId);
        modelBuilder.Entity<CostSheet>().HasOne<Style>().WithMany().HasForeignKey(x => x.StyleId);
        modelBuilder.Entity<Quote>().HasOne<Style>().WithMany().HasForeignKey(x => x.StyleId);
        modelBuilder.Entity<Quote>().HasOne<CostSheet>().WithMany().HasForeignKey(x => x.CostSheetId)
            .OnDelete(DeleteBehavior.Restrict);
        modelBuilder.Entity<ReadinessCheck>().HasOne<Style>().WithMany().HasForeignKey(x => x.StyleId);
        modelBuilder.Entity<ProductionRelease>().HasOne<Style>().WithMany().HasForeignKey(x => x.StyleId);
    }
}
