using GarmentStylePilot.Api.Domain;
using Microsoft.EntityFrameworkCore;

namespace GarmentStylePilot.Api.Data;

public sealed class GarmentDbContext(DbContextOptions<GarmentDbContext> options) : DbContext(options)
{
    public DbSet<Style> Styles => Set<Style>();
    public DbSet<Sample> Samples => Set<Sample>();
    public DbSet<BomLine> BomLines => Set<BomLine>();
    public DbSet<SewingOperation> SewingOperations => Set<SewingOperation>();
    public DbSet<OperationStandard> OperationStandards => Set<OperationStandard>();
    public DbSet<SmvEstimate> SmvEstimates => Set<SmvEstimate>();
    public DbSet<CostSheet> CostSheets => Set<CostSheet>();
    public DbSet<Quote> Quotes => Set<Quote>();
    public DbSet<ReadinessCheck> ReadinessChecks => Set<ReadinessCheck>();
    public DbSet<ProductionRelease> ProductionReleases => Set<ProductionRelease>();
    public DbSet<ProductionLine> ProductionLines => Set<ProductionLine>();
    public DbSet<LineMachineCapacity> LineMachineCapacities => Set<LineMachineCapacity>();
    public DbSet<LineBalancePlan> LineBalancePlans => Set<LineBalancePlan>();
    public DbSet<LineBalanceAssignment> LineBalanceAssignments => Set<LineBalanceAssignment>();
    public DbSet<LineOperator> LineOperators => Set<LineOperator>();
    public DbSet<OperatorSkill> OperatorSkills => Set<OperatorSkill>();
    public DbSet<WorkstationPlan> WorkstationPlans => Set<WorkstationPlan>();
    public DbSet<WorkstationGroup> WorkstationGroups => Set<WorkstationGroup>();
    public DbSet<MaterialCoverageReview> MaterialCoverageReviews => Set<MaterialCoverageReview>();
    public DbSet<MaterialCoverageItem> MaterialCoverageItems => Set<MaterialCoverageItem>();
    public DbSet<ProductionScheduleConfirmation> ProductionScheduleConfirmations => Set<ProductionScheduleConfirmation>();
    public DbSet<ProductionHandoverOutbox> ProductionHandoverOutboxes => Set<ProductionHandoverOutbox>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Style>().HasIndex(x => x.Code).IsUnique();
        modelBuilder.Entity<OperationStandard>().HasIndex(x => x.Code).IsUnique();
        modelBuilder.Entity<OperationStandard>().Property(x => x.ReferenceSmvMinutes).HasPrecision(18, 4);
        modelBuilder.Entity<ReadinessCheck>().HasIndex(x => new { x.StyleId, x.Code }).IsUnique();
        modelBuilder.Entity<ProductionRelease>().HasIndex(x => x.StyleId).IsUnique();
        modelBuilder.Entity<ProductionLine>().HasIndex(x => x.Code).IsUnique();
        modelBuilder.Entity<LineMachineCapacity>().HasIndex(x => new { x.ProductionLineId, x.MachineType }).IsUnique();
        modelBuilder.Entity<LineBalanceAssignment>().HasIndex(x => new { x.LineBalancePlanId, x.SequenceNo }).IsUnique();
        modelBuilder.Entity<LineBalancePlan>().Property(x => x.Status).HasConversion<string>();
        modelBuilder.Entity<MaterialCoverageReview>().Property(x => x.Status).HasConversion<string>();
        modelBuilder.Entity<ProductionScheduleConfirmation>().Property(x => x.Status).HasConversion<string>();
        modelBuilder.Entity<MaterialCoverageItem>().HasIndex(x => new { x.ReviewId, x.MaterialCode, x.Unit }).IsUnique();
        modelBuilder.Entity<ProductionHandoverOutbox>().HasIndex(x => x.ProductionReleaseId).IsUnique();
        modelBuilder.Entity<MaterialCoverageItem>().Property(x => x.RequiredQuantity).HasPrecision(18, 4);
        modelBuilder.Entity<MaterialCoverageItem>().Property(x => x.AvailableQuantity).HasPrecision(18, 4);
        modelBuilder.Entity<MaterialCoverageItem>().Property(x => x.ShortageQuantity).HasPrecision(18, 4);
        modelBuilder.Entity<LineOperator>().HasIndex(x => x.EmployeeCode).IsUnique();
        modelBuilder.Entity<OperatorSkill>().HasIndex(x => new { x.LineOperatorId, x.MachineType }).IsUnique();
        modelBuilder.Entity<WorkstationGroup>().HasIndex(x => new { x.WorkstationPlanId, x.StationNumber }).IsUnique();
        modelBuilder.Entity<WorkstationPlan>().Property(x => x.Status).HasConversion<string>();
        modelBuilder.Entity<WorkstationPlan>().Property(x => x.TaktMinutes).HasPrecision(18, 6);
        modelBuilder.Entity<WorkstationGroup>().Property(x => x.CombinedSmvMinutes).HasPrecision(18, 4);
        modelBuilder.Entity<ProductionLine>().Property(x => x.PlannedEfficiencyPercent).HasPrecision(9, 4);
        modelBuilder.Entity<LineBalancePlan>().Property(x => x.TotalSmvMinutes).HasPrecision(18, 4);
        modelBuilder.Entity<LineBalancePlan>().Property(x => x.TaktMinutes).HasPrecision(18, 6);
        modelBuilder.Entity<LineBalancePlan>().Property(x => x.EffectiveMinutesPerOperator).HasPrecision(18, 4);
        modelBuilder.Entity<LineBalanceAssignment>().Property(x => x.SmvMinutes).HasPrecision(18, 4);

        modelBuilder.Entity<Style>().Property(x => x.Status).HasConversion<string>();
        modelBuilder.Entity<Sample>().Property(x => x.Status).HasConversion<string>();
        modelBuilder.Entity<Quote>().Property(x => x.Status).HasConversion<string>();

        modelBuilder.Entity<BomLine>().Property(x => x.Consumption).HasPrecision(18, 6);
        modelBuilder.Entity<BomLine>().Property(x => x.UnitPrice).HasPrecision(18, 4);
        modelBuilder.Entity<BomLine>().Property(x => x.WastagePercent).HasPrecision(9, 4);
        modelBuilder.Entity<SewingOperation>().Property(x => x.SmvMinutes).HasPrecision(18, 4);
        modelBuilder.Entity<SmvEstimate>().Property(x => x.PredictedSmvMinutes).HasPrecision(18, 4);
        modelBuilder.Entity<SmvEstimate>().Property(x => x.SmvLower80).HasPrecision(18, 4);
        modelBuilder.Entity<SmvEstimate>().Property(x => x.SmvUpper80).HasPrecision(18, 4);
        modelBuilder.Entity<SmvEstimate>().Property(x => x.ViewConsistencyPercent).HasPrecision(8, 2);
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
        modelBuilder.Entity<SewingOperation>().HasOne<OperationStandard>().WithMany()
            .HasForeignKey(x => x.OperationStandardId).OnDelete(DeleteBehavior.Restrict);
        modelBuilder.Entity<SmvEstimate>().HasOne<Style>().WithMany().HasForeignKey(x => x.StyleId);
        modelBuilder.Entity<CostSheet>().HasOne<Style>().WithMany().HasForeignKey(x => x.StyleId);
        modelBuilder.Entity<Quote>().HasOne<Style>().WithMany().HasForeignKey(x => x.StyleId);
        modelBuilder.Entity<Quote>().HasOne<CostSheet>().WithMany().HasForeignKey(x => x.CostSheetId)
            .OnDelete(DeleteBehavior.Restrict);
        modelBuilder.Entity<ReadinessCheck>().HasOne<Style>().WithMany().HasForeignKey(x => x.StyleId);
        modelBuilder.Entity<ProductionRelease>().HasOne<Style>().WithMany().HasForeignKey(x => x.StyleId);
        modelBuilder.Entity<LineMachineCapacity>().HasOne<ProductionLine>().WithMany()
            .HasForeignKey(x => x.ProductionLineId).OnDelete(DeleteBehavior.Cascade);
        modelBuilder.Entity<LineBalancePlan>().HasOne<ProductionLine>().WithMany()
            .HasForeignKey(x => x.ProductionLineId).OnDelete(DeleteBehavior.Restrict);
        modelBuilder.Entity<LineBalancePlan>().HasOne<Style>().WithMany()
            .HasForeignKey(x => x.StyleId).OnDelete(DeleteBehavior.Restrict);
        modelBuilder.Entity<LineBalanceAssignment>().HasOne<LineBalancePlan>().WithMany()
            .HasForeignKey(x => x.LineBalancePlanId).OnDelete(DeleteBehavior.Cascade);
        modelBuilder.Entity<LineOperator>().HasOne<ProductionLine>().WithMany()
            .HasForeignKey(x => x.ProductionLineId).OnDelete(DeleteBehavior.Restrict);
        modelBuilder.Entity<OperatorSkill>().HasOne<LineOperator>().WithMany()
            .HasForeignKey(x => x.LineOperatorId).OnDelete(DeleteBehavior.Cascade);
        modelBuilder.Entity<WorkstationPlan>().HasOne<Style>().WithMany()
            .HasForeignKey(x => x.StyleId).OnDelete(DeleteBehavior.Restrict);
        modelBuilder.Entity<WorkstationPlan>().HasOne<ProductionLine>().WithMany()
            .HasForeignKey(x => x.ProductionLineId).OnDelete(DeleteBehavior.Restrict);
        modelBuilder.Entity<WorkstationGroup>().HasOne<WorkstationPlan>().WithMany()
            .HasForeignKey(x => x.WorkstationPlanId).OnDelete(DeleteBehavior.Cascade);
        modelBuilder.Entity<ProductionRelease>().HasOne<WorkstationPlan>().WithMany()
            .HasForeignKey(x => x.WorkstationPlanId).OnDelete(DeleteBehavior.Restrict);
        modelBuilder.Entity<MaterialCoverageReview>().HasOne<Style>().WithMany()
            .HasForeignKey(x => x.StyleId).OnDelete(DeleteBehavior.Restrict);
        modelBuilder.Entity<MaterialCoverageItem>().HasOne<MaterialCoverageReview>().WithMany()
            .HasForeignKey(x => x.ReviewId).OnDelete(DeleteBehavior.Cascade);
        modelBuilder.Entity<ProductionScheduleConfirmation>().HasOne<Style>().WithMany()
            .HasForeignKey(x => x.StyleId).OnDelete(DeleteBehavior.Restrict);
        modelBuilder.Entity<ProductionScheduleConfirmation>().HasOne<ProductionLine>().WithMany()
            .HasForeignKey(x => x.ProductionLineId).OnDelete(DeleteBehavior.Restrict);
        modelBuilder.Entity<ProductionScheduleConfirmation>().HasOne<WorkstationPlan>().WithMany()
            .HasForeignKey(x => x.WorkstationPlanId).OnDelete(DeleteBehavior.Restrict);
        modelBuilder.Entity<ProductionRelease>().HasOne<MaterialCoverageReview>().WithMany()
            .HasForeignKey(x => x.MaterialCoverageReviewId).OnDelete(DeleteBehavior.Restrict);
        modelBuilder.Entity<ProductionRelease>().HasOne<ProductionScheduleConfirmation>().WithMany()
            .HasForeignKey(x => x.ProductionScheduleConfirmationId).OnDelete(DeleteBehavior.Restrict);
        modelBuilder.Entity<ProductionHandoverOutbox>().HasOne<ProductionRelease>().WithMany()
            .HasForeignKey(x => x.ProductionReleaseId).OnDelete(DeleteBehavior.Restrict);
    }
}
