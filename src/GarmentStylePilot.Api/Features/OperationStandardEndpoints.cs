using GarmentStylePilot.Api.Data;
using GarmentStylePilot.Api.Domain;
using Microsoft.EntityFrameworkCore;

namespace GarmentStylePilot.Api.Features;

/// <summary>
/// Reference standards are reusable inputs. Copying them creates a style routing revision
/// that must still be validated and approved independently by IE.
/// </summary>
public static class OperationStandardEndpoints
{
    public sealed record NewStandard(string Code, string ComponentName, string OperationName,
        string MachineType, string MethodCode, decimal ReferenceSmvMinutes);
    public sealed record EditStandard(string ComponentName, string OperationName, string MachineType,
        string MethodCode, decimal ReferenceSmvMinutes, bool IsActive);
    public sealed record StandardSelection(int SequenceNo, Guid OperationStandardId);

    public static IEndpointRouteBuilder MapOperationStandardEndpoints(this IEndpointRouteBuilder app)
    {
        var api = app.MapGroup("/api");

        api.MapGet("/operation-standards", async (GarmentDbContext db) =>
            Results.Ok(await db.OperationStandards.OrderBy(x => x.Code).ToListAsync()));

        api.MapPost("/operation-standards", async (NewStandard input, GarmentDbContext db) =>
        {
            var code = input.Code?.Trim().ToUpperInvariant() ?? "";
            if (code.Length == 0 || code.Length > 80 ||
                !Valid(input.ComponentName, input.OperationName, input.MachineType,
                    input.MethodCode, input.ReferenceSmvMinutes))
                return Results.BadRequest(new { error = "Valid operation code, component, name, machine, method and SMV required." });
            if (await db.OperationStandards.AnyAsync(x => x.Code == code))
                return Results.Conflict(new { error = "Operation code already exists." });

            var standard = new OperationStandard
            {
                Code = code, ComponentName = input.ComponentName.Trim(),
                OperationName = input.OperationName.Trim(), MachineType = input.MachineType.Trim().ToUpperInvariant(),
                MethodCode = input.MethodCode?.Trim() ?? "",
                ReferenceSmvMinutes = input.ReferenceSmvMinutes
            };
            db.OperationStandards.Add(standard);
            await db.SaveChangesAsync();
            return Results.Created($"/api/operation-standards/{standard.Id}", standard);
        });

        api.MapPut("/operation-standards/{id:guid}", async (
            Guid id, EditStandard input, GarmentDbContext db) =>
        {
            var standard = await db.OperationStandards.FindAsync(id);
            if (standard is null) return Results.NotFound();
            if (!Valid(input.ComponentName, input.OperationName, input.MachineType,
                input.MethodCode, input.ReferenceSmvMinutes))
                return Results.BadRequest(new { error = "Invalid IE reference operation." });

            standard.ComponentName = input.ComponentName.Trim();
            standard.OperationName = input.OperationName.Trim();
            standard.MachineType = input.MachineType.Trim().ToUpperInvariant();
            standard.MethodCode = input.MethodCode?.Trim() ?? "";
            standard.ReferenceSmvMinutes = input.ReferenceSmvMinutes;
            standard.IsActive = input.IsActive;
            standard.Revision++;
            standard.UpdatedAtUtc = DateTime.UtcNow;
            await db.SaveChangesAsync();
            return Results.Ok(standard);
        });

        api.MapPost("/styles/{styleId:guid}/operations/from-library", async (
            Guid styleId, List<StandardSelection> input, GarmentDbContext db) =>
        {
            var style = await db.Styles.SingleOrDefaultAsync(x =>
                x.Id == styleId && x.Status == StyleStatus.InDevelopment);
            if (style is null) return Results.NotFound(new { error = "Style not found or released." });
            if (input.Count is < 1 or > 500 ||
                input.Any(x => x.SequenceNo is < 1 or > 9999 || x.OperationStandardId == Guid.Empty) ||
                input.Select(x => x.SequenceNo).Distinct().Count() != input.Count)
                return Results.BadRequest(new { error = "One or more unique valid operation sequences required." });

            var ids = input.Select(x => x.OperationStandardId).Distinct().ToArray();
            var standards = await db.OperationStandards
                .Where(x => ids.Contains(x.Id) && x.IsActive).ToListAsync();
            if (standards.Count != ids.Length)
                return Results.BadRequest(new { error = "Every selected standard must exist and be active." });

            var standardsById = standards.ToDictionary(x => x.Id);
            db.SewingOperations.RemoveRange(db.SewingOperations.Where(x => x.StyleId == styleId));
            db.SewingOperations.AddRange(input.Select(x =>
            {
                var standard = standardsById[x.OperationStandardId];
                return new SewingOperation
                {
                    StyleId = styleId, SequenceNo = x.SequenceNo,
                    Name = standard.OperationName,
                    MachineType = standard.MachineType,
                    SmvMinutes = standard.ReferenceSmvMinutes,
                    OperationStandardId = standard.Id,
                    StandardRevision = standard.Revision
                };
            }));

            style.RoutingRevision++;
            await GarmentEndpoints.InvalidateReadiness(db, styleId);
            await db.SaveChangesAsync();
            return Results.Ok(new
            {
                style.RoutingRevision, importedOperations = input.Count,
                requiresIeApproval = true,
                message = "Reference SMV copied to style; IE must validate and approve this routing revision."
            });
        });

        return app;
    }

    private static bool Valid(string component, string operation, string machine, string? method, decimal smv) =>
        !string.IsNullOrWhiteSpace(component) && component.Length <= 120 &&
        !string.IsNullOrWhiteSpace(operation) && operation.Length <= 250 &&
        !string.IsNullOrWhiteSpace(machine) && machine.Trim().Length <= 80 &&
        (method?.Length ?? 0) <= 80 &&
        smv > 0 && smv <= 1000;
}
