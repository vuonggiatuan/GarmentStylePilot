using GarmentStylePilot.Api.Application;
using GarmentStylePilot.Api.Data;
using GarmentStylePilot.Api.Domain;
using Microsoft.EntityFrameworkCore;

namespace GarmentStylePilot.Api.Features;

public static class WorkstationEndpoints
{
    public sealed record SkillInput(string MachineType, int Level);
    public sealed record NewOperator(string EmployeeCode, string DisplayName, List<SkillInput> Skills);
    public sealed record EditOperator(string DisplayName, bool IsActive, List<SkillInput> Skills);
    public sealed record PlanInput(Guid ProductionLineId, int TargetDailyQuantity, int MinimumSkillLevel = 2);
    public sealed record Approval(string ApprovedBy);

    public static IEndpointRouteBuilder MapWorkstationEndpoints(this IEndpointRouteBuilder app)
    {
        var api = app.MapGroup("/api");

        api.MapGet("/lines/{lineId:guid}/operators", async (Guid lineId, GarmentDbContext db) =>
        {
            if (!await db.ProductionLines.AnyAsync(x => x.Id == lineId)) return Results.NotFound();
            var people = await db.LineOperators.Where(x => x.ProductionLineId == lineId)
                .OrderBy(x => x.EmployeeCode).ToArrayAsync();
            var ids = people.Select(x => x.Id).ToArray();
            var skills = await db.OperatorSkills.Where(x => ids.Contains(x.LineOperatorId)).ToListAsync();
            return Results.Ok(people.Select(person => new
            {
                person.Id, person.EmployeeCode, person.DisplayName, person.IsActive,
                skills = skills.Where(x => x.LineOperatorId == person.Id)
                    .OrderBy(x => x.MachineType).Select(x => new { x.MachineType, x.Level })
            }));
        });

        api.MapPost("/lines/{lineId:guid}/operators", async (
            Guid lineId, NewOperator input, GarmentDbContext db) =>
        {
            var line = await db.ProductionLines.FindAsync(lineId);
            if (line is null) return Results.NotFound();
            var code = input.EmployeeCode?.Trim().ToUpperInvariant() ?? "";
            if (!ValidCode(code) || string.IsNullOrWhiteSpace(input.DisplayName) ||
                input.DisplayName.Length > 100 || !ValidSkills(input.Skills))
                return Results.BadRequest(new { error = "Employee code, name and qualified machine skills required." });
            if (await db.LineOperators.AnyAsync(x => x.EmployeeCode == code))
                return Results.Conflict(new { error = "Employee code already exists." });
            var person = new LineOperator
            {
                ProductionLineId = lineId, EmployeeCode = code, DisplayName = input.DisplayName.Trim()
            };
            db.LineOperators.Add(person);
            db.OperatorSkills.AddRange(input.Skills.Select(x => new OperatorSkill
            {
                LineOperatorId = person.Id, MachineType = x.MachineType.Trim().ToUpperInvariant(), Level = x.Level
            }));
            line.CapacityRevision++;
            await db.SaveChangesAsync();
            return Results.Created($"/api/lines/{lineId}/operators", new
            {
                person.Id, person.EmployeeCode, person.DisplayName, person.IsActive,
                line.CapacityRevision
            });
        });

        api.MapPut("/operators/{id:guid}", async (
            Guid id, EditOperator input, GarmentDbContext db) =>
        {
            var person = await db.LineOperators.FindAsync(id);
            if (person is null) return Results.NotFound();
            var line = await db.ProductionLines.FindAsync(person.ProductionLineId);
            if (line is null) return Results.Conflict(new { error = "Line no longer available." });
            if (string.IsNullOrWhiteSpace(input.DisplayName) || input.DisplayName.Length > 100 ||
                !ValidSkills(input.Skills))
                return Results.BadRequest(new { error = "Valid name and skills required." });

            var existing = await db.OperatorSkills.Where(x => x.LineOperatorId == id).ToListAsync();
            db.OperatorSkills.RemoveRange(existing);
            db.OperatorSkills.AddRange(input.Skills.Select(x => new OperatorSkill
            {
                LineOperatorId = id, MachineType = x.MachineType.Trim().ToUpperInvariant(), Level = x.Level
            }));
            person.DisplayName = input.DisplayName.Trim();
            person.IsActive = input.IsActive;
            line.CapacityRevision++; // every skill/assignment change invalidates both types of line plan
            await db.SaveChangesAsync();
            return Results.Ok(new { person.Id, person.EmployeeCode, person.IsActive, line.CapacityRevision });
        });

        api.MapGet("/styles/{styleId:guid}/workstation-plans", async (
            Guid styleId, GarmentDbContext db) =>
        {
            if (!await db.Styles.AnyAsync(x => x.Id == styleId)) return Results.NotFound();
            var plans = await db.WorkstationPlans.Where(x => x.StyleId == styleId)
                .OrderByDescending(x => x.CreatedAtUtc).ToListAsync();
            var ids = plans.Select(x => x.Id).ToArray();
            var groups = await db.WorkstationGroups.Where(x => ids.Contains(x.WorkstationPlanId))
                .OrderBy(x => x.StationNumber).ToListAsync();
            return Results.Ok(plans.Select(plan => new
            {
                plan, stations = groups.Where(g => g.WorkstationPlanId == plan.Id).ToArray()
            }));
        });

        api.MapPost("/styles/{styleId:guid}/workstation-plans/preview", async (
            Guid styleId, PlanInput input, GarmentDbContext db) =>
        {
            var source = await LoadSource(db, styleId, input.ProductionLineId);
            if (source is null) return Results.NotFound(new { error = "Style or active line not found." });
            if (source.Style.Status == StyleStatus.Released)
                return Results.Conflict(new { error = "Released style is immutable." });
            if (source.Style.ApprovedRoutingRevision != source.Style.RoutingRevision ||
                source.Style.RoutingRevision == 0)
                return Results.Conflict(new { error = "Current official IE routing must be approved first." });
            try
            {
                var calc = WorkstationBalanceCalculator.Calculate(source.Operations,
                    source.Line, source.Machines, source.Operators, source.Skills,
                    input.TargetDailyQuantity, input.MinimumSkillLevel);
                return Results.Ok(new
                {
                    line = source.Line.Code, source.Style.RoutingRevision,
                    source.Line.CapacityRevision, calculation = calc,
                    disclaimer = "Deterministic adjacent same-machine grouping; IE must confirm layout and operator qualifications."
                });
            }
            catch (ArgumentException ex) { return Results.BadRequest(new { error = ex.Message }); }
            catch (OverflowException) { return Results.BadRequest(new { error = "Staffing overflow." }); }
        });

        api.MapPost("/styles/{styleId:guid}/workstation-plans", async (
            Guid styleId, PlanInput input, GarmentDbContext db) =>
        {
            var source = await LoadSource(db, styleId, input.ProductionLineId);
            if (source is null) return Results.NotFound(new { error = "Style or active line not found." });
            if (source.Style.Status == StyleStatus.Released)
                return Results.Conflict(new { error = "Released style is immutable." });
            if (source.Style.ApprovedRoutingRevision != source.Style.RoutingRevision ||
                source.Style.RoutingRevision == 0)
                return Results.Conflict(new { error = "Current official IE routing must be approved first." });

            WorkstationBalanceResult calc;
            try
            {
                calc = WorkstationBalanceCalculator.Calculate(source.Operations,
                    source.Line, source.Machines, source.Operators, source.Skills,
                    input.TargetDailyQuantity, input.MinimumSkillLevel);
            }
            catch (ArgumentException ex) { return Results.BadRequest(new { error = ex.Message }); }
            catch (OverflowException) { return Results.BadRequest(new { error = "Staffing overflow." }); }

            var gaps = string.Join("; ", calc.MachineShortfalls.Select(x =>
                $"{x.MachineType} needs {x.Required} machines, only {x.Available} configured"));
            var summary = string.Join("; ", new[]
            {
                calc.LaborFeasible ? "" :
                    $"Staff required {calc.RequiredOperators}, roster {calc.AvailableOperators}, line max {source.Line.MaximumOperators}",
                calc.SkillFeasible ? "" :
                    $"Qualified assignments {calc.AssignedOperators}/{calc.RequiredOperators} (skill >= {input.MinimumSkillLevel})",
                gaps
            }.Where(x => x.Length > 0));

            var plan = new WorkstationPlan
            {
                StyleId = styleId, ProductionLineId = source.Line.Id,
                RoutingRevision = source.Style.RoutingRevision,
                LineCapacityRevision = source.Line.CapacityRevision,
                TargetDailyQuantity = input.TargetDailyQuantity,
                MinimumSkillLevel = input.MinimumSkillLevel,
                RequiredOperators = calc.RequiredOperators,
                AssignedOperators = calc.AssignedOperators,
                AvailableOperators = calc.AvailableOperators,
                EstimatedDailyCapacity = calc.EstimatedDailyCapacity,
                TaktMinutes = calc.TaktMinutes, LaborFeasible = calc.LaborFeasible,
                MachineFeasible = calc.MachineFeasible, SkillFeasible = calc.SkillFeasible,
                ConstraintSummary = summary
            };
            db.WorkstationPlans.Add(plan);
            db.WorkstationGroups.AddRange(calc.Stations.Select(x => new WorkstationGroup
            {
                WorkstationPlanId = plan.Id, StationNumber = x.StationNumber,
                MachineType = x.MachineType, CombinedSmvMinutes = x.CombinedSmvMinutes,
                RequiredOperators = x.RequiredOperators,
                EstimatedDailyCapacity = x.EstimatedDailyCapacity,
                AssignedEmployeeCodes = string.Join(",", x.AssignedEmployeeCodes),
                OperationSequences = string.Join(",", x.Operations.Select(op => op.SequenceNo))
            }));
            await db.SaveChangesAsync();
            return Results.Created($"/api/styles/{styleId}/workstation-plans",
                new { plan, calculation = calc });
        });

        api.MapPost("/workstation-plans/{planId:guid}/approve", async (
            Guid planId, Approval input, GarmentDbContext db) =>
        {
            var plan = await db.WorkstationPlans.FindAsync(planId);
            if (plan is null) return Results.NotFound();
            if (string.IsNullOrWhiteSpace(input.ApprovedBy))
                return Results.BadRequest(new { error = "IE approver required." });
            var style = await db.Styles.FindAsync(plan.StyleId);
            var line = await db.ProductionLines.FindAsync(plan.ProductionLineId);
            if (style is null || line is null ||
                style.Status == StyleStatus.Released ||
                style.RoutingRevision != plan.RoutingRevision ||
                style.ApprovedRoutingRevision != plan.RoutingRevision ||
                !line.IsActive || line.CapacityRevision != plan.LineCapacityRevision)
                return Results.Conflict(new { error = "Workstation plan stale: check IE routing and line capacity versions." });
            if (!plan.LaborFeasible || !plan.MachineFeasible || !plan.SkillFeasible ||
                plan.AssignedOperators != plan.RequiredOperators)
                return Results.Conflict(new { error = "Workstation plan lacks sufficient qualified staff or equipment.",
                    plan.ConstraintSummary });

            bool baseline = await db.LineBalancePlans.AnyAsync(x =>
                x.StyleId == plan.StyleId &&
                x.ProductionLineId == plan.ProductionLineId &&
                x.RoutingRevision == plan.RoutingRevision &&
                x.LineCapacityRevision == plan.LineCapacityRevision &&
                x.TargetDailyQuantity == plan.TargetDailyQuantity &&
                x.Status == LineBalanceStatus.Approved &&
                x.LaborFeasible && x.MachineFeasible);
            if (!baseline)
                return Results.Conflict(new { error = "Approved current Phase 3 line capacity baseline with same target/day required." });

            plan.Status = LineBalanceStatus.Approved;
            plan.ApprovedBy = input.ApprovedBy.Trim();
            plan.ApprovedAtUtc = DateTime.UtcNow;
            await db.SaveChangesAsync();
            return Results.Ok(plan);
        });

        return app;
    }

    private static bool ValidCode(string code) =>
        code.Length is >= 1 and <= 40 && code.All(x =>
            x is >= 'A' and <= 'Z' or >= '0' and <= '9' or '-' or '_');

    private static bool ValidSkills(List<SkillInput>? skills)
    {
        if (skills is null || skills.Count is < 1 or > 20 ||
            skills.Any(x => string.IsNullOrWhiteSpace(x.MachineType) ||
                x.MachineType.Trim().Length > 80 || x.Level is < 1 or > 5))
            return false;
        var normalized = skills.Select(x => x.MachineType.Trim().ToUpperInvariant()).ToArray();
        return normalized.Distinct(StringComparer.Ordinal).Count() == normalized.Length;
    }

    private sealed record Source(Style Style, ProductionLine Line,
        List<SewingOperation> Operations, List<LineMachineCapacity> Machines,
        List<LineOperator> Operators, List<OperatorSkill> Skills);

    private static async Task<Source?> LoadSource(GarmentDbContext db, Guid styleId, Guid lineId)
    {
        var style = await db.Styles.FindAsync(styleId);
        var line = await db.ProductionLines.FirstOrDefaultAsync(x => x.Id == lineId && x.IsActive);
        if (style is null || line is null) return null;
        var operations = await db.SewingOperations.Where(x => x.StyleId == styleId)
            .OrderBy(x => x.SequenceNo).ToListAsync();
        var machines = await db.LineMachineCapacities.Where(x => x.ProductionLineId == lineId)
            .ToListAsync();
        var people = await db.LineOperators.Where(x => x.ProductionLineId == lineId).ToListAsync();
        var ids = people.Select(x => x.Id).ToArray();
        var skills = await db.OperatorSkills.Where(x => ids.Contains(x.LineOperatorId)).ToListAsync();
        return new Source(style, line, operations, machines, people, skills);
    }
}
