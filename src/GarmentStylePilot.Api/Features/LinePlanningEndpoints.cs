using GarmentStylePilot.Api.Application;
using GarmentStylePilot.Api.Data;
using GarmentStylePilot.Api.Domain;
using Microsoft.EntityFrameworkCore;

namespace GarmentStylePilot.Api.Features;

public static class LinePlanningEndpoints
{
    public sealed record NewLine(string Code, string Name, string FactoryCode,
        int MaximumOperators, int ShiftMinutes, decimal PlannedEfficiencyPercent);
    public sealed record UpdateLine(string Name, string FactoryCode, int MaximumOperators,
        int ShiftMinutes, decimal PlannedEfficiencyPercent, bool IsActive);
    public sealed record MachineInput(string MachineType, int AvailableCount);
    public sealed record PlanInput(Guid ProductionLineId, int TargetDailyQuantity);
    public sealed record ApproveInput(string ApprovedBy);

    public static IEndpointRouteBuilder MapLinePlanningEndpoints(this IEndpointRouteBuilder app)
    {
        var api = app.MapGroup("/api");

        api.MapGet("/lines", async (GarmentDbContext db) =>
        {
            var lines = await db.ProductionLines.OrderBy(x => x.FactoryCode).ThenBy(x => x.Code).ToListAsync();
            var machines = await db.LineMachineCapacities.ToListAsync();
            return Results.Ok(lines.Select(line => new
            {
                line.Id, line.Code, line.Name, line.FactoryCode, line.MaximumOperators,
                line.ShiftMinutes, line.PlannedEfficiencyPercent,
                line.CapacityRevision, line.IsActive,
                machines = machines.Where(m => m.ProductionLineId == line.Id)
                    .OrderBy(m => m.MachineType).ToArray()
            }));
        });

        api.MapPost("/lines", async (NewLine input, GarmentDbContext db) =>
        {
            var code = input.Code?.Trim().ToUpperInvariant() ?? "";
            if (!ValidLine(input.MaximumOperators, input.ShiftMinutes, input.PlannedEfficiencyPercent)
                || string.IsNullOrWhiteSpace(code) || code.Length > 64
                || string.IsNullOrWhiteSpace(input.Name) || string.IsNullOrWhiteSpace(input.FactoryCode))
                return Results.BadRequest(new { error = "Line code, name, factory, headcount, shift and efficiency are required." });
            if (await db.ProductionLines.AnyAsync(x => x.Code == code))
                return Results.Conflict(new { error = "Line code already exists." });
            var line = new ProductionLine
            {
                Code = code, Name = input.Name.Trim(), FactoryCode = input.FactoryCode.Trim(),
                MaximumOperators = input.MaximumOperators, ShiftMinutes = input.ShiftMinutes,
                PlannedEfficiencyPercent = input.PlannedEfficiencyPercent
            };
            db.ProductionLines.Add(line);
            await db.SaveChangesAsync();
            return Results.Created($"/api/lines/{line.Id}", line);
        });

        api.MapPut("/lines/{id:guid}", async (Guid id, UpdateLine input, GarmentDbContext db) =>
        {
            var line = await db.ProductionLines.FindAsync(id);
            if (line is null) return Results.NotFound();
            if (!ValidLine(input.MaximumOperators, input.ShiftMinutes, input.PlannedEfficiencyPercent)
                || string.IsNullOrWhiteSpace(input.Name) || string.IsNullOrWhiteSpace(input.FactoryCode))
                return Results.BadRequest(new { error = "Invalid line configuration." });
            // Updating a line revokes any approval based on its older staffing capacity.
            line.Name = input.Name.Trim();
            line.FactoryCode = input.FactoryCode.Trim();
            line.MaximumOperators = input.MaximumOperators;
            line.ShiftMinutes = input.ShiftMinutes;
            line.PlannedEfficiencyPercent = input.PlannedEfficiencyPercent;
            line.IsActive = input.IsActive;
            line.CapacityRevision++;
            await db.SaveChangesAsync();
            return Results.Ok(line);
        });

        api.MapPut("/lines/{id:guid}/machines", async (Guid id,
            List<MachineInput> input, GarmentDbContext db) =>
        {
            var line = await db.ProductionLines.FindAsync(id);
            if (line is null) return Results.NotFound();
            if (input.Count == 0 || input.Count > 100 ||
                input.Any(x => string.IsNullOrWhiteSpace(x.MachineType) ||
                    x.MachineType.Trim().Length > 80 || x.AvailableCount < 0 || x.AvailableCount > 10000))
                return Results.BadRequest(new { error = "Provide one or more valid machine types and capacities." });
            var normalized = input.Select(x => x.MachineType.Trim().ToUpperInvariant()).ToArray();
            if (normalized.Distinct(StringComparer.Ordinal).Count() != normalized.Length)
                return Results.BadRequest(new { error = "Duplicate machine type in line configuration." });
            db.LineMachineCapacities.RemoveRange(
                db.LineMachineCapacities.Where(x => x.ProductionLineId == id));
            db.LineMachineCapacities.AddRange(input.Select(x => new LineMachineCapacity
            {
                ProductionLineId = id, MachineType = x.MachineType.Trim().ToUpperInvariant(),
                AvailableCount = x.AvailableCount
            }));
            line.CapacityRevision++;
            await db.SaveChangesAsync();
            return Results.Ok(new { line.Id, line.CapacityRevision, machineTypes = input.Count });
        });

        api.MapGet("/styles/{styleId:guid}/line-plans", async (Guid styleId, GarmentDbContext db) =>
        {
            if (!await db.Styles.AnyAsync(x => x.Id == styleId)) return Results.NotFound();
            var plans = await db.LineBalancePlans.Where(x => x.StyleId == styleId)
                .OrderByDescending(x => x.CreatedAtUtc).ToListAsync();
            var ids = plans.Select(x => x.Id).ToArray();
            var assignments = await db.LineBalanceAssignments
                .Where(x => ids.Contains(x.LineBalancePlanId))
                .OrderBy(x => x.SequenceNo).ToListAsync();
            return Results.Ok(plans.Select(p => new
            {
                plan = p,
                assignments = assignments.Where(x => x.LineBalancePlanId == p.Id).ToArray()
            }));
        });

        api.MapPost("/styles/{styleId:guid}/line-plans/preview", async (
            Guid styleId, PlanInput input, GarmentDbContext db) =>
        {
            var source = await LoadSource(db, styleId, input.ProductionLineId);
            if (source is null)
                return Results.NotFound(new { error = "Style or active production line not found." });
            if (source.Style.Status == StyleStatus.Released)
                return Results.Conflict(new { error = "Style already released." });
            if (source.Style.RoutingRevision == 0 ||
                source.Style.RoutingRevision != source.Style.ApprovedRoutingRevision)
                return Results.Conflict(new { error = "Official IE routing revision must be approved." });
            if (input.TargetDailyQuantity is < 1 or > 200000)
                return Results.BadRequest(new { error = "Invalid target daily quantity." });
            try
            {
                var calculation = LineBalanceCalculator.Calculate(
                    source.Operations, source.Line, source.Machines, input.TargetDailyQuantity);
                return Results.Ok(new
                {
                    line = source.Line.Code, source.Style.RoutingRevision, source.Line.CapacityRevision,
                    targetDailyQuantity = input.TargetDailyQuantity, calculation,
                    disclaimer = "Conservative dedicated-operation staffing estimate, not final optimized line balance."
                });
            }
            catch (ArgumentException ex) { return Results.BadRequest(new { error = ex.Message }); }
            catch (OverflowException) { return Results.BadRequest(new { error = "Operation staffing exceeds supported bounds." }); }
        });

        api.MapPost("/styles/{styleId:guid}/line-plans", async (
            Guid styleId, PlanInput input, GarmentDbContext db) =>
        {
            var source = await LoadSource(db, styleId, input.ProductionLineId);
            if (source is null)
                return Results.NotFound(new { error = "Style or active production line not found." });
            if (source.Style.Status == StyleStatus.Released)
                return Results.Conflict(new { error = "Style already released." });
            if (source.Style.RoutingRevision == 0 ||
                source.Style.RoutingRevision != source.Style.ApprovedRoutingRevision)
                return Results.Conflict(new { error = "Approve official IE routing before planning." });
            if (input.TargetDailyQuantity is < 1 or > 200000)
                return Results.BadRequest(new { error = "Invalid target daily quantity." });
            LineBalanceResult calc;
            try
            {
                calc = LineBalanceCalculator.Calculate(
                    source.Operations, source.Line, source.Machines, input.TargetDailyQuantity);
            }
            catch (ArgumentException ex) { return Results.BadRequest(new { error = ex.Message }); }
            catch (OverflowException) { return Results.BadRequest(new { error = "Operation staffing exceeds supported bounds." }); }

            var shortages = string.Join("; ", calc.MachineShortfalls.Select(x =>
                $"{x.MachineType}: requires {x.Required}, available {x.Available}"));
            var plan = new LineBalancePlan
            {
                StyleId = styleId, ProductionLineId = source.Line.Id,
                RoutingRevision = source.Style.RoutingRevision,
                LineCapacityRevision = source.Line.CapacityRevision,
                TargetDailyQuantity = input.TargetDailyQuantity,
                TotalSmvMinutes = calc.TotalSmvMinutes, TaktMinutes = calc.TaktMinutes,
                EffectiveMinutesPerOperator = calc.EffectiveMinutesPerOperator,
                RequiredOperators = calc.RequiredOperators,
                AvailableOperators = calc.AvailableOperators,
                EstimatedDailyCapacity = calc.EstimatedDailyCapacity,
                LaborFeasible = calc.LaborFeasible, MachineFeasible = calc.MachineFeasible,
                ConstraintSummary = (calc.LaborFeasible ? "" :
                    $"Operators: requires {calc.RequiredOperators}, available {calc.AvailableOperators}. ")
                    + shortages
            };
            db.LineBalancePlans.Add(plan);
            db.LineBalanceAssignments.AddRange(calc.Assignments.Select(a => new LineBalanceAssignment
            {
                LineBalancePlanId = plan.Id, SequenceNo = a.SequenceNo,
                OperationName = a.OperationName, MachineType = a.MachineType,
                SmvMinutes = a.SmvMinutes, RequiredOperators = a.RequiredOperators,
                EstimatedDailyCapacity = a.EstimatedDailyCapacity
            }));
            await db.SaveChangesAsync();
            return Results.Created($"/api/styles/{styleId}/line-plans",
                new { plan, calculation = calc });
        });

        api.MapPost("/line-plans/{planId:guid}/approve", async (
            Guid planId, ApproveInput input, GarmentDbContext db) =>
        {
            var plan = await db.LineBalancePlans.FindAsync(planId);
            if (plan is null) return Results.NotFound();
            if (string.IsNullOrWhiteSpace(input.ApprovedBy))
                return Results.BadRequest(new { error = "IE approver required." });
            var style = await db.Styles.FindAsync(plan.StyleId);
            var line = await db.ProductionLines.FindAsync(plan.ProductionLineId);
            if (style is null || line is null || style.Status == StyleStatus.Released ||
                style.ApprovedRoutingRevision != plan.RoutingRevision ||
                style.RoutingRevision != plan.RoutingRevision ||
                line.CapacityRevision != plan.LineCapacityRevision || !line.IsActive)
                return Results.Conflict(new { error = "Line plan stale: check IE routing and line capacity revisions." });
            if (!plan.LaborFeasible || !plan.MachineFeasible)
                return Results.Conflict(new { error = "Line lacks required staffing or machine capacity.",
                    plan.ConstraintSummary });
            plan.Status = LineBalanceStatus.Approved;
            plan.ApprovedBy = input.ApprovedBy.Trim();
            plan.ApprovedAtUtc = DateTime.UtcNow;
            await db.SaveChangesAsync();
            return Results.Ok(plan);
        });
        return app;
    }

    private static bool ValidLine(int operators, int shiftMinutes, decimal efficiency) =>
        operators is >= 1 and <= 5000 && shiftMinutes is >= 60 and <= 1440 &&
        efficiency > 0 && efficiency <= 100;

    private sealed record Source(Style Style, ProductionLine Line,
        List<SewingOperation> Operations, List<LineMachineCapacity> Machines);

    private static async Task<Source?> LoadSource(
        GarmentDbContext db, Guid styleId, Guid lineId)
    {
        var style = await db.Styles.FindAsync(styleId);
        var line = await db.ProductionLines.FirstOrDefaultAsync(x => x.Id == lineId && x.IsActive);
        if (style is null || line is null) return null;
        var operations = await db.SewingOperations.Where(x => x.StyleId == styleId)
            .OrderBy(x => x.SequenceNo).ToListAsync();
        var machines = await db.LineMachineCapacities.Where(x => x.ProductionLineId == lineId)
            .ToListAsync();
        return new Source(style, line, operations, machines);
    }
}
