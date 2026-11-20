using GarmentStylePilot.Api.Domain;

namespace GarmentStylePilot.Api.Application;

public static class ReadinessCodes
{
    public static readonly string[] Required =
    [
        "TechPackApproved",
        "MaterialAvailable",
        "CapacityConfirmed",
        "LineBalanceApproved",
        "PpMeetingCompleted",
        "PilotRunApproved",
        "QualityPlanApproved",
        "MachineReady"
    ];
}

public static class ProductionGate
{
    public static IReadOnlyList<string> Evaluate(
        Style style, IReadOnlyCollection<Sample> samples, IReadOnlyCollection<BomLine> bom,
        IReadOnlyCollection<SewingOperation> operations, IReadOnlyCollection<CostSheet> costs,
        IReadOnlyCollection<Quote> quotes, IReadOnlyCollection<ReadinessCheck> checks,
        IReadOnlyCollection<LineBalancePlan> plans, IReadOnlyCollection<ProductionLine> lines,
        IReadOnlyCollection<WorkstationPlan> workstations,
        IReadOnlyCollection<MaterialCoverageReview> materialReviews,
        IReadOnlyCollection<ProductionScheduleConfirmation> schedules,
        string? requiredLineCode = null, DateOnly? requiredStartDate = null)
    {
        var blockers = new List<string>();
        if (style.Status == StyleStatus.Released)
            blockers.Add("Style has already been released.");
        if (!samples.Any(x => x.Status == SampleStatus.Approved))
            blockers.Add("Sample approval missing.");
        if (bom.Count == 0 || bom.Any(x => x.Consumption <= 0 || x.UnitPrice < 0))
            blockers.Add("Complete BOM with valid consumption and prices required.");
        if (style.ApprovedBomRevision != style.BomRevision || style.BomRevision == 0)
            blockers.Add("Current BOM revision is not approved.");
        if (operations.Count == 0 || operations.Any(x => x.SmvMinutes <= 0))
            blockers.Add("Validated GST/SMV operations required.");
        if (style.ApprovedRoutingRevision != style.RoutingRevision || style.RoutingRevision == 0)
            blockers.Add("Current IE routing/SMV revision is not approved.");

        var currentCosts = costs.Where(x => x.IsApproved
            && x.BomRevision == style.BomRevision
            && x.RoutingRevision == style.RoutingRevision).ToArray();
        if (currentCosts.Length == 0)
            blockers.Add("Approved costing for current BOM/routing revision required.");
        if (!quotes.Any(x => x.Status == QuoteStatus.Accepted
            && currentCosts.Any(c => c.Id == x.CostSheetId)))
            blockers.Add("Customer-accepted quote linked to current approved costing required.");

        var approvedPlans = plans.Where(p => p.Status == LineBalanceStatus.Approved &&
            p.RoutingRevision == style.RoutingRevision &&
            p.LaborFeasible && p.MachineFeasible &&
            lines.Any(line => line.Id == p.ProductionLineId &&
                line.CapacityRevision == p.LineCapacityRevision && line.IsActive &&
                (requiredLineCode is null ||
                 string.Equals(line.Code, requiredLineCode, StringComparison.OrdinalIgnoreCase)))).ToArray();
        if (approvedPlans.Length == 0)
            blockers.Add(requiredLineCode is null
                ? "Approved feasible line balance plan for current routing and line capacity required."
                : "Approved feasible line balance plan required for target line " + requiredLineCode + ".");

        var approvedWorkstations = workstations.Any(p =>
            p.Status == LineBalanceStatus.Approved &&
            p.RoutingRevision == style.RoutingRevision &&
            p.LaborFeasible && p.MachineFeasible && p.SkillFeasible &&
            p.AssignedOperators == p.RequiredOperators &&
            lines.Any(line => line.Id == p.ProductionLineId &&
                line.CapacityRevision == p.LineCapacityRevision && line.IsActive &&
                (requiredLineCode is null ||
                 string.Equals(line.Code, requiredLineCode, StringComparison.OrdinalIgnoreCase))) &&
            approvedPlans.Any(baseline => baseline.ProductionLineId == p.ProductionLineId &&
                baseline.TargetDailyQuantity == p.TargetDailyQuantity));
        if (!approvedWorkstations)
            blockers.Add(requiredLineCode is null
                ? "Approved qualified workstation balance for current IE routing and line capacity required."
                : "Approved qualified workstation balance required for target line " + requiredLineCode + ".");

        var coveredMaterials = materialReviews.Where(m =>
            m.Status == HandoverReviewStatus.Confirmed &&
            m.BomRevision == style.BomRevision &&
            m.OrderQuantity > 0 && m.FullyCovered).ToArray();
        if (coveredMaterials.Length == 0)
            blockers.Add("Confirmed complete material coverage for current BOM and order quantity required.");

        var confirmedSchedule = schedules.Any(s =>
            s.Status == HandoverReviewStatus.Confirmed &&
            s.BomRevision == style.BomRevision &&
            s.RoutingRevision == style.RoutingRevision &&
            s.OrderQuantity > 0 && s.TargetDailyQuantity > 0 &&
            coveredMaterials.Any(m => m.OrderQuantity == s.OrderQuantity) &&
            lines.Any(line => line.Id == s.ProductionLineId &&
                line.IsActive && line.CapacityRevision == s.LineCapacityRevision &&
                (requiredLineCode is null ||
                    string.Equals(line.Code, requiredLineCode, StringComparison.OrdinalIgnoreCase))) &&
            (requiredStartDate is null || s.PlannedStartDate == requiredStartDate.Value) &&
            workstations.Any(w => w.Id == s.WorkstationPlanId &&
                w.ProductionLineId == s.ProductionLineId &&
                w.RoutingRevision == style.RoutingRevision &&
                w.LineCapacityRevision == s.LineCapacityRevision &&
                w.TargetDailyQuantity == s.TargetDailyQuantity &&
                w.Status == LineBalanceStatus.Approved &&
                w.LaborFeasible && w.MachineFeasible && w.SkillFeasible &&
                w.AssignedOperators == w.RequiredOperators) &&
            approvedPlans.Any(p => p.ProductionLineId == s.ProductionLineId &&
                p.TargetDailyQuantity == s.TargetDailyQuantity));
        if (!confirmedSchedule)
            blockers.Add(requiredLineCode is null
                ? "Confirmed PPC schedule with current approved line, workstation plan and material quantity required."
                : "Confirmed PPC schedule for requested line, date and current approved plans required.");

        foreach (var code in ReadinessCodes.Required)
            if (!checks.Any(x => x.Code == code && x.Verified))
                blockers.Add("Readiness check missing: " + code);

        return blockers;
    }
}
