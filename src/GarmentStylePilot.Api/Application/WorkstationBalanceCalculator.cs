using GarmentStylePilot.Api.Domain;

namespace GarmentStylePilot.Api.Application;

public sealed record WorkstationOperation(int SequenceNo, string OperationName, decimal SmvMinutes);
public sealed record WorkstationRow(
    int StationNumber, string MachineType, decimal CombinedSmvMinutes,
    int RequiredOperators, int EstimatedDailyCapacity,
    IReadOnlyList<WorkstationOperation> Operations,
    IReadOnlyList<string> AssignedEmployeeCodes);
public sealed record WorkstationMachineShortfall(string MachineType, int Required, int Available);
public sealed record WorkstationBalanceResult(
    decimal TotalSmvMinutes, decimal TaktMinutes, int TargetDailyQuantity,
    int RequiredOperators, int AssignedOperators, int AvailableOperators,
    int EstimatedDailyCapacity, bool LaborFeasible, bool MachineFeasible, bool SkillFeasible,
    IReadOnlyList<WorkstationMachineShortfall> MachineShortfalls,
    IReadOnlyList<WorkstationRow> Stations)
{
    public bool CanMeetTarget => LaborFeasible && MachineFeasible && SkillFeasible;
}

/// <summary>
/// Transparent Phase 4 heuristic, NOT an optimal assembly-line balancing solver.
/// Contiguous same-machine operations can share one workstation until takt is full;
/// one operator is matched to at most one workstation slot using the skill matrix.
/// </summary>
public static class WorkstationBalanceCalculator
{
    private sealed class Group
    {
        public int Number { get; init; }
        public string Machine { get; init; } = "";
        public decimal Smv { get; set; }
        public List<WorkstationOperation> Operations { get; } = [];
        public int Staffing { get; set; }
        public int Capacity { get; set; }
    }

    public static WorkstationBalanceResult Calculate(
        IReadOnlyCollection<SewingOperation> routing,
        ProductionLine line, IReadOnlyCollection<LineMachineCapacity> machines,
        IReadOnlyCollection<LineOperator> operators,
        IReadOnlyCollection<OperatorSkill> skills,
        int targetDailyQuantity, int minimumSkillLevel)
    {
        if (targetDailyQuantity is < 1 or > 200000 || minimumSkillLevel is < 1 or > 5)
            throw new ArgumentException("Target (1-200000) and minimum skill level (1-5) required.");
        if (line.MaximumOperators is < 1 or > 5000 || line.ShiftMinutes is < 60 or > 1440 ||
            line.PlannedEfficiencyPercent <= 0 || line.PlannedEfficiencyPercent > 100)
            throw new ArgumentException("Invalid line staffing, shift time or efficiency.");
        if (routing.Count is < 1 or > 500 ||
            routing.Any(x => x.SequenceNo <= 0 || x.SmvMinutes <= 0 ||
                x.SmvMinutes > 1000 || string.IsNullOrWhiteSpace(x.MachineType)) ||
            routing.Select(x => x.SequenceNo).Distinct().Count() != routing.Count)
            throw new ArgumentException("Official routing must contain 1-500 uniquely numbered valid operations.");

        var effective = line.ShiftMinutes * line.PlannedEfficiencyPercent / 100m;
        var takt = effective / targetDailyQuantity;
        var groups = new List<Group>();

        foreach (var operation in routing.OrderBy(x => x.SequenceNo))
        {
            string machine = operation.MachineType.Trim().ToUpperInvariant();
            Group? last = groups.LastOrDefault();
            if (last is null || last.Machine != machine ||
                last.Smv + operation.SmvMinutes > takt)
            {
                last = new Group { Number = groups.Count + 1, Machine = machine };
                groups.Add(last);
            }
            last.Smv += operation.SmvMinutes;
            last.Operations.Add(new WorkstationOperation(
                operation.SequenceNo, operation.Name, operation.SmvMinutes));
        }

        foreach (var group in groups)
        {
            group.Staffing = checked((int)decimal.Ceiling(group.Smv / takt));
            group.Capacity = (int)decimal.Min(
                decimal.Floor(effective * group.Staffing / group.Smv), 2000000000m);
        }

        int required = checked(groups.Sum(x => x.Staffing));
        if (required > 2000)
            throw new ArgumentException("Workstation staffing exceeds 2000 slots; reduce target/day or split the plan.");

        var activeOperators = operators.Where(x => x.ProductionLineId == line.Id && x.IsActive)
            .OrderBy(x => x.EmployeeCode, StringComparer.Ordinal).ToArray();
        var activeIds = activeOperators.Select(x => x.Id).ToHashSet();
        var qualified = skills.Where(x => activeIds.Contains(x.LineOperatorId) &&
            x.Level >= minimumSkillLevel)
            .GroupBy(x => x.LineOperatorId)
            .ToDictionary(x => x.Key,
                x => x.Select(y => y.MachineType.Trim().ToUpperInvariant()).ToHashSet(StringComparer.Ordinal));

        var slots = new List<int>();
        for (int i = 0; i < groups.Count; i++)
            for (int n = 0; n < groups[i].Staffing; n++)
                slots.Add(i);

        var candidates = slots.Select(slot =>
            Enumerable.Range(0, activeOperators.Length)
                .Where(employee => qualified.TryGetValue(activeOperators[employee].Id, out var types)
                    && types.Contains(groups[slot].Machine))
                .ToArray()).ToArray();

        // Distinct-employee bipartite matching protects against double-counting multi-skilled workers.
        int[] ownerByEmployee = Enumerable.Repeat(-1, activeOperators.Length).ToArray();
        bool FindAssignment(int slot, bool[] visited)
        {
            foreach (int employee in candidates[slot])
            {
                if (visited[employee]) continue;
                visited[employee] = true;
                if (ownerByEmployee[employee] == -1 ||
                    FindAssignment(ownerByEmployee[employee], visited))
                {
                    ownerByEmployee[employee] = slot;
                    return true;
                }
            }
            return false;
        }

        int assigned = 0;
        foreach (int slot in Enumerable.Range(0, slots.Count)
                     .OrderBy(x => candidates[x].Length).ThenBy(x => x))
        {
            if (FindAssignment(slot, new bool[activeOperators.Length])) assigned++;
        }
        var codesByStation = groups.Select(_ => new List<string>()).ToArray();
        for (int i = 0; i < activeOperators.Length; i++)
            if (ownerByEmployee[i] != -1)
                codesByStation[slots[ownerByEmployee[i]]].Add(activeOperators[i].EmployeeCode);

        var shortages = groups.GroupBy(x => x.Machine, StringComparer.OrdinalIgnoreCase)
            .Select(group =>
            {
                var needed = group.Sum(x => x.Staffing);
                var available = machines.Where(x => string.Equals(
                        x.MachineType, group.Key, StringComparison.OrdinalIgnoreCase))
                    .Sum(x => x.AvailableCount);
                return new WorkstationMachineShortfall(group.Key, needed, available);
            })
            .Where(x => x.Required > x.Available)
            .OrderBy(x => x.MachineType).ToArray();

        var rows = groups.Select((group, i) => new WorkstationRow(
            group.Number, group.Machine, decimal.Round(group.Smv, 4),
            group.Staffing, group.Capacity, group.Operations.ToArray(),
            codesByStation[i].OrderBy(x => x, StringComparer.Ordinal).ToArray())).ToArray();

        return new WorkstationBalanceResult(
            decimal.Round(routing.Sum(x => x.SmvMinutes), 4), decimal.Round(takt, 6),
            targetDailyQuantity, required, assigned, activeOperators.Length,
            groups.Min(x => x.Capacity),
            required <= line.MaximumOperators && activeOperators.Length >= required,
            shortages.Length == 0, assigned == required, shortages, rows);
    }
}
