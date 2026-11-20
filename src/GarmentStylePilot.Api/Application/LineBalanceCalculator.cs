using GarmentStylePilot.Api.Domain;

namespace GarmentStylePilot.Api.Application;

// Conservative dedicated-operation staffing model. This is a planning baseline,
// NOT a certified optimized workstation balance or a substitute for an IE study.
public sealed record OperationAllocation(
    int SequenceNo, string OperationName, string MachineType,
    decimal SmvMinutes, int RequiredOperators, int EstimatedDailyCapacity);

public sealed record MachineShortfall(string MachineType, int Required, int Available);

public sealed record LineBalanceResult(
    decimal TotalSmvMinutes, decimal TaktMinutes, decimal EffectiveMinutesPerOperator,
    int RequiredOperators, int AvailableOperators, int EstimatedDailyCapacity,
    bool LaborFeasible, bool MachineFeasible,
    IReadOnlyList<MachineShortfall> MachineShortfalls,
    IReadOnlyList<OperationAllocation> Assignments)
{
    public bool CanMeetTarget => LaborFeasible && MachineFeasible;
}

public static class LineBalanceCalculator
{
    public static LineBalanceResult Calculate(
        IReadOnlyCollection<SewingOperation> operations,
        ProductionLine line, IReadOnlyCollection<LineMachineCapacity> machines, int targetDailyQuantity)
    {
        if (targetDailyQuantity is < 1 or > 200000)
            throw new ArgumentOutOfRangeException(nameof(targetDailyQuantity), "Target quantity must be 1–200,000.");
        if (line.MaximumOperators is < 1 or > 5000 || line.ShiftMinutes is < 60 or > 1440 ||
            line.PlannedEfficiencyPercent <= 0 || line.PlannedEfficiencyPercent > 100)
            throw new ArgumentException("Line capacity must have valid staffing, shift minutes and efficiency.");
        if (operations.Count == 0 ||
            operations.Any(x => x.SmvMinutes <= 0 || x.SmvMinutes > 1000 ||
                x.SequenceNo <= 0 || string.IsNullOrWhiteSpace(x.MachineType)))
            throw new ArgumentException("All official IE operations need positive SMV, sequence and machine type.");

        decimal effectiveMinutes = line.ShiftMinutes * line.PlannedEfficiencyPercent / 100m;
        decimal takt = effectiveMinutes / targetDailyQuantity;
        var allocations = operations.OrderBy(x => x.SequenceNo).Select(op =>
        {
            var needed = checked((int)decimal.Ceiling(op.SmvMinutes / takt));
            var capacity = decimal.Floor(effectiveMinutes * needed / op.SmvMinutes);
            return new OperationAllocation(op.SequenceNo, op.Name, op.MachineType.Trim().ToUpperInvariant(),
                op.SmvMinutes, needed, (int)decimal.Min(capacity, 2000000000m));
        }).ToArray();

        var neededOperators = checked(allocations.Sum(x => x.RequiredOperators));
        var shortfalls = allocations.GroupBy(x => x.MachineType, StringComparer.OrdinalIgnoreCase)
            .Select(group =>
            {
                var required = group.Sum(x => x.RequiredOperators);
                var available = machines.Where(x =>
                    string.Equals(x.MachineType, group.Key, StringComparison.OrdinalIgnoreCase))
                    .Sum(x => x.AvailableCount);
                return new MachineShortfall(group.Key, required, available);
            })
            .Where(x => x.Required > x.Available).OrderBy(x => x.MachineType)
            .ToArray();

        return new LineBalanceResult(
            decimal.Round(operations.Sum(x => x.SmvMinutes), 4),
            decimal.Round(takt, 6), decimal.Round(effectiveMinutes, 4),
            neededOperators, line.MaximumOperators,
            allocations.Min(x => x.EstimatedDailyCapacity),
            neededOperators <= line.MaximumOperators, shortfalls.Length == 0,
            shortfalls, allocations);
    }
}
