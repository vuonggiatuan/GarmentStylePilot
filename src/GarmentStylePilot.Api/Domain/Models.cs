namespace GarmentStylePilot.Api.Domain;

public enum SampleStatus { Draft, Approved, Rejected }
public enum QuoteStatus { Proposed, Accepted, Rejected }
public enum StyleStatus { InDevelopment, Released }

public sealed class Style
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Code { get; set; } = "";
    public string Customer { get; set; } = "";
    public string Description { get; set; } = "";
    public string Currency { get; set; } = "USD";
    public StyleStatus Status { get; set; } = StyleStatus.InDevelopment;
    public int BomRevision { get; set; }
    public int ApprovedBomRevision { get; set; } = -1;
    public int RoutingRevision { get; set; }
    public int ApprovedRoutingRevision { get; set; } = -1;
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}

public sealed class Sample
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid StyleId { get; set; }
    public string SampleType { get; set; } = "Fit";
    public string Revision { get; set; } = "A";
    public string Notes { get; set; } = "";
    public SampleStatus Status { get; set; } = SampleStatus.Draft;
    public string? ApprovedBy { get; set; }
    public DateTime? ApprovedAtUtc { get; set; }
}

public sealed class BomLine
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid StyleId { get; set; }
    public string MaterialCode { get; set; } = "";
    public string Category { get; set; } = "Fabric";
    public string Unit { get; set; } = "m";
    public decimal Consumption { get; set; }
    public decimal UnitPrice { get; set; }
    public decimal WastagePercent { get; set; }
}

/// <summary>
/// An AI estimate is a non-authoritative planning suggestion, never an IE-approved routing.
/// It must not be used for production release or final costing.
/// </summary>
public sealed class SmvEstimate
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid StyleId { get; set; }
    public string ModelVersion { get; set; } = "";
    public string Source { get; set; } = "AI";
    public int PredictedOperationCount { get; set; }
    public decimal PredictedSmvMinutes { get; set; }
    public decimal SmvLower80 { get; set; }
    public decimal SmvUpper80 { get; set; }
    public decimal? ViewConsistencyPercent { get; set; }
    public string Confidence { get; set; } = "";
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}

// Shared IE reference library. Entries are not a style approval and must be
// validated in each style's routing before production release.
public sealed class OperationStandard
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Code { get; set; } = "";
    public string ComponentName { get; set; } = "";
    public string OperationName { get; set; } = "";
    public string MachineType { get; set; } = "";
    public string MethodCode { get; set; } = "";
    public decimal ReferenceSmvMinutes { get; set; }
    public int Revision { get; set; } = 1;
    public bool IsActive { get; set; } = true;
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
}

public sealed class SewingOperation
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid StyleId { get; set; }
    public int SequenceNo { get; set; }
    public string Name { get; set; } = "";
    public string MachineType { get; set; } = "";
    public decimal SmvMinutes { get; set; }
    public Guid? OperationStandardId { get; set; }
    public int? StandardRevision { get; set; }
}

public sealed class CostSheet
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid StyleId { get; set; }
    public int BomRevision { get; set; }
    public int RoutingRevision { get; set; }
    public decimal MinuteRate { get; set; }
    public decimal OverheadPerPiece { get; set; }
    public decimal TargetMarginPercent { get; set; }
    public decimal MaterialCost { get; set; }
    public decimal LaborCost { get; set; }
    public decimal SmvMinutes { get; set; }
    public decimal TotalCost { get; set; }
    public decimal SuggestedPrice { get; set; }
    public bool IsApproved { get; set; }
    public string? ApprovedBy { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}

public sealed class Quote
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid StyleId { get; set; }
    public Guid CostSheetId { get; set; }
    public decimal UnitPrice { get; set; }
    public string Currency { get; set; } = "USD";
    public QuoteStatus Status { get; set; } = QuoteStatus.Proposed;
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime? AcceptedAtUtc { get; set; }
}

public sealed class ReadinessCheck
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid StyleId { get; set; }
    public string Code { get; set; } = "";
    public bool Verified { get; set; }
    public string Notes { get; set; } = "";
    public string? VerifiedBy { get; set; }
    public DateTime? VerifiedAtUtc { get; set; }
}

public enum LineBalanceStatus { Draft, Approved }

// An operational line is a specific production capacity, not just a string code.
public sealed class ProductionLine
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
    public string FactoryCode { get; set; } = "";
    public int MaximumOperators { get; set; }
    public int ShiftMinutes { get; set; } = 480;
    public decimal PlannedEfficiencyPercent { get; set; } = 85m;
    public int CapacityRevision { get; set; } = 1;
    public bool IsActive { get; set; } = true;
}

public sealed class LineMachineCapacity
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ProductionLineId { get; set; }
    public string MachineType { get; set; } = "";
    public int AvailableCount { get; set; }
}

// Historical calculation snapshot. Routing revision and line capacity revision must both match
// before the plan may be used to release a style.
public sealed class LineBalancePlan
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid StyleId { get; set; }
    public Guid ProductionLineId { get; set; }
    public int RoutingRevision { get; set; }
    public int LineCapacityRevision { get; set; }
    public int TargetDailyQuantity { get; set; }
    public decimal TotalSmvMinutes { get; set; }
    public decimal TaktMinutes { get; set; }
    public decimal EffectiveMinutesPerOperator { get; set; }
    public int RequiredOperators { get; set; }
    public int AvailableOperators { get; set; }
    public int EstimatedDailyCapacity { get; set; }
    public bool LaborFeasible { get; set; }
    public bool MachineFeasible { get; set; }
    public string ConstraintSummary { get; set; } = "";
    public LineBalanceStatus Status { get; set; } = LineBalanceStatus.Draft;
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public string? ApprovedBy { get; set; }
    public DateTime? ApprovedAtUtc { get; set; }
}

public sealed class LineBalanceAssignment
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid LineBalancePlanId { get; set; }
    public int SequenceNo { get; set; }
    public string OperationName { get; set; } = "";
    public string MachineType { get; set; } = "";
    public decimal SmvMinutes { get; set; }
    public int RequiredOperators { get; set; }
    public int EstimatedDailyCapacity { get; set; }
}

// Actual operator skill matrix. A qualified operator can be allocated to only one
// workstation slot per simulated shift; multi-skilled people are not double counted.
public sealed class LineOperator
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ProductionLineId { get; set; }
    public string EmployeeCode { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public bool IsActive { get; set; } = true;
}

public sealed class OperatorSkill
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid LineOperatorId { get; set; }
    public string MachineType { get; set; } = "";
    public int Level { get; set; } // 1-5, 2 is the default qualified threshold.
}

// A saved heuristic grouping: distinct from the Phase 3 dedicated-operation plan.
// Versions are snapshotted, and production release requires current approved grouping.
public sealed class WorkstationPlan
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid StyleId { get; set; }
    public Guid ProductionLineId { get; set; }
    public int RoutingRevision { get; set; }
    public int LineCapacityRevision { get; set; }
    public int TargetDailyQuantity { get; set; }
    public int MinimumSkillLevel { get; set; } = 2;
    public int RequiredOperators { get; set; }
    public int AssignedOperators { get; set; }
    public int AvailableOperators { get; set; }
    public int EstimatedDailyCapacity { get; set; }
    public decimal TaktMinutes { get; set; }
    public bool LaborFeasible { get; set; }
    public bool MachineFeasible { get; set; }
    public bool SkillFeasible { get; set; }
    public string ConstraintSummary { get; set; } = "";
    public LineBalanceStatus Status { get; set; } = LineBalanceStatus.Draft;
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public string? ApprovedBy { get; set; }
    public DateTime? ApprovedAtUtc { get; set; }
}

public sealed class WorkstationGroup
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid WorkstationPlanId { get; set; }
    public int StationNumber { get; set; }
    public string MachineType { get; set; } = "";
    public decimal CombinedSmvMinutes { get; set; }
    public int RequiredOperators { get; set; }
    public int EstimatedDailyCapacity { get; set; }
    public string AssignedEmployeeCodes { get; set; } = ""; // comma-separated snapshot codes
    public string OperationSequences { get; set; } = ""; // comma-separated sequences
}

public sealed class ProductionRelease
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid StyleId { get; set; }
    public string LineCode { get; set; } = "";
    public DateOnly PlannedStartDate { get; set; }
    public string ApprovedBy { get; set; } = "";
    public DateTime ApprovedAtUtc { get; set; } = DateTime.UtcNow;
    public Guid CostSheetId { get; set; }
    public Guid QuoteId { get; set; }
    public Guid? WorkstationPlanId { get; set; }
}
