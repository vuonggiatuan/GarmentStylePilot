using GarmentStylePilot.Api.Data;
using GarmentStylePilot.Api.Domain;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

namespace GarmentStylePilot.Api.Features;

/// <summary>
/// Manual PPC handover attestations and read-only export. ERP/MES/APS connectors
/// are deliberately not called by this phase.
/// </summary>
public static class ProductionHandoverEndpoints
{
    public sealed record MaterialAvailability(string MaterialCode, string Unit, decimal AvailableQuantity);
    public sealed record MaterialReviewInput(int OrderQuantity, string SourceReference,
        List<MaterialAvailability> Available);
    public sealed record ScheduleInput(Guid ProductionLineId, Guid WorkstationPlanId,
        int OrderQuantity, int TargetDailyQuantity, DateOnly PlannedStartDate, string SourceReference);
    public sealed record ConfirmInput(string ConfirmedBy);

    public static IEndpointRouteBuilder MapProductionHandoverEndpoints(this IEndpointRouteBuilder app)
    {
        var api = app.MapGroup("/api");

        api.MapGet("/styles/{styleId:guid}/material-reviews", async (Guid styleId, GarmentDbContext db) =>
        {
            if (!await db.Styles.AnyAsync(x => x.Id == styleId)) return Results.NotFound();
            var reviews = await db.MaterialCoverageReviews.Where(x => x.StyleId == styleId)
                .OrderByDescending(x => x.CreatedAtUtc).ToListAsync();
            var ids = reviews.Select(x => x.Id).ToArray();
            var items = await db.MaterialCoverageItems.Where(x => ids.Contains(x.ReviewId))
                .OrderBy(x => x.MaterialCode).ThenBy(x => x.Unit).ToListAsync();
            return Results.Ok(reviews.Select(review => new
            {
                review, items = items.Where(x => x.ReviewId == review.Id).ToArray()
            }));
        });

        api.MapPost("/styles/{styleId:guid}/material-reviews", async (
            Guid styleId, MaterialReviewInput input, GarmentDbContext db) =>
        {
            var style = await db.Styles.FindAsync(styleId);
            if (style is null) return Results.NotFound();
            if (style.Status != StyleStatus.InDevelopment)
                return Results.Conflict(new { error = "Released styles cannot accept new material attestations." });
            if (style.BomRevision == 0 || style.ApprovedBomRevision != style.BomRevision)
                return Results.Conflict(new { error = "Approve current BOM before reviewing material coverage." });
            if (input.OrderQuantity is < 1 or > 1000000 || !ValidReference(input.SourceReference) ||
                input.Available is not { Count: > 0 and <= 500 } ||
                input.Available.Any(x => string.IsNullOrWhiteSpace(x.MaterialCode) ||
                    string.IsNullOrWhiteSpace(x.Unit) || x.AvailableQuantity < 0 ||
                    x.AvailableQuantity > 1000000000m))
                return Results.BadRequest(new { error = "Valid order quantity, source reference and available material quantities required." });

            var bom = await db.BomLines.Where(x => x.StyleId == styleId).ToListAsync();
            if (bom.Count is < 1 or > 500)
                return Results.BadRequest(new { error = "BOM must contain 1–500 material lines." });
            var normalized = input.Available.Select(x => new
            {
                Code = x.MaterialCode.Trim().ToUpperInvariant(),
                Unit = x.Unit.Trim().ToUpperInvariant(),
                x.AvailableQuantity
            }).ToArray();
            if (normalized.Any(x => x.Code.Length > 100 || x.Unit.Length > 20) ||
                normalized.Select(x => x.Code + "|" + x.Unit).Distinct(StringComparer.Ordinal).Count() != normalized.Length)
                return Results.BadRequest(new { error = "Material+unit must be unique; codes <=100 and units <=20." });

            var requirements = bom.GroupBy(x => new
            {
                Code = x.MaterialCode.Trim().ToUpperInvariant(),
                Unit = x.Unit.Trim().ToUpperInvariant()
            }).OrderBy(g => g.Key.Code).ThenBy(g => g.Key.Unit).ToList();

            if (requirements.Count != normalized.Length ||
                requirements.Any(g => !normalized.Any(x => x.Code == g.Key.Code && x.Unit == g.Key.Unit)))
                return Results.BadRequest(new { error = "Supply an available quantity for every BOM material+unit, without extra items." });

            var review = new MaterialCoverageReview
            {
                StyleId = styleId, BomRevision = style.BomRevision,
                OrderQuantity = input.OrderQuantity,
                SourceReference = input.SourceReference.Trim()
            };
            var items = new List<MaterialCoverageItem>();
            try
            {
                foreach (var g in requirements)
                {
                    decimal required = g.Sum(x =>
                        x.Consumption * (1m + x.WastagePercent / 100m) * input.OrderQuantity);
                    required = decimal.Ceiling(required * 10000m) / 10000m;
                    decimal available = normalized.Single(x => x.Code == g.Key.Code && x.Unit == g.Key.Unit).AvailableQuantity;
                    items.Add(new MaterialCoverageItem
                    {
                        ReviewId = review.Id, MaterialCode = g.Key.Code, Unit = g.Key.Unit,
                        RequiredQuantity = required, AvailableQuantity = available,
                        ShortageQuantity = decimal.Max(0m, required - available)
                    });
                }
            }
            catch (OverflowException)
            {
                return Results.BadRequest(new { error = "Quantity overflow; check BOM consumption and order size." });
            }

            review.FullyCovered = items.All(x => x.ShortageQuantity == 0m);
            db.MaterialCoverageReviews.Add(review);
            db.MaterialCoverageItems.AddRange(items);
            await db.SaveChangesAsync();
            return Results.Created($"/api/styles/{styleId}/material-reviews", new
            {
                review, items, limitation = "MANUAL_PPC_ATTESTATION only. No live ERP stock validation."
            });
        });

        api.MapPost("/material-reviews/{reviewId:guid}/confirm", async (
            Guid reviewId, ConfirmInput input, GarmentDbContext db) =>
        {
            var review = await db.MaterialCoverageReviews.FindAsync(reviewId);
            if (review is null) return Results.NotFound();
            var style = await db.Styles.FindAsync(review.StyleId);
            if (style is null || style.Status != StyleStatus.InDevelopment ||
                review.BomRevision != style.BomRevision ||
                style.ApprovedBomRevision != style.BomRevision)
                return Results.Conflict(new { error = "Material review stale, BOM unapproved or style released." });
            if (!review.FullyCovered)
                return Results.Conflict(new { error = "Cannot confirm shortages; obtain material coverage first." });
            if (!ValidActor(input.ConfirmedBy))
                return Results.BadRequest(new { error = "PPC confirmer required." });
            if (review.Status == HandoverReviewStatus.Confirmed)
                return Results.Ok(review);
            review.Status = HandoverReviewStatus.Confirmed;
            review.ConfirmedBy = input.ConfirmedBy.Trim();
            review.ConfirmedAtUtc = DateTime.UtcNow;
            await db.SaveChangesAsync();
            return Results.Ok(review);
        });

        api.MapGet("/styles/{styleId:guid}/schedules", async (Guid styleId, GarmentDbContext db) =>
        {
            if (!await db.Styles.AnyAsync(x => x.Id == styleId)) return Results.NotFound();
            return Results.Ok(await db.ProductionScheduleConfirmations.Where(x => x.StyleId == styleId)
                .OrderByDescending(x => x.CreatedAtUtc).ToListAsync());
        });

        api.MapPost("/styles/{styleId:guid}/schedules", async (
            Guid styleId, ScheduleInput input, GarmentDbContext db) =>
        {
            var style = await db.Styles.FindAsync(styleId);
            if (style is null) return Results.NotFound();
            if (style.Status != StyleStatus.InDevelopment)
                return Results.Conflict(new { error = "Style released." });
            if (style.BomRevision == 0 || style.ApprovedBomRevision != style.BomRevision ||
                style.RoutingRevision == 0 || style.ApprovedRoutingRevision != style.RoutingRevision)
                return Results.Conflict(new { error = "Current BOM and IE routing approvals required." });
            if (input.OrderQuantity is < 1 or > 1000000 ||
                input.TargetDailyQuantity is < 1 or > 200000 ||
                !ValidReference(input.SourceReference))
                return Results.BadRequest(new { error = "Order quantity, target/day and PPC planning reference required." });

            var line = await db.ProductionLines.FindAsync(input.ProductionLineId);
            var plan = await db.WorkstationPlans.FindAsync(input.WorkstationPlanId);
            if (line is null || !line.IsActive || plan is null ||
                plan.StyleId != styleId || plan.ProductionLineId != line.Id ||
                plan.RoutingRevision != style.RoutingRevision ||
                plan.LineCapacityRevision != line.CapacityRevision ||
                plan.TargetDailyQuantity != input.TargetDailyQuantity ||
                plan.Status != LineBalanceStatus.Approved ||
                !plan.LaborFeasible || !plan.MachineFeasible || !plan.SkillFeasible ||
                plan.AssignedOperators != plan.RequiredOperators)
                return Results.Conflict(new { error = "Current approved workstation plan for the requested line and daily target is required." });

            var schedule = new ProductionScheduleConfirmation
            {
                StyleId = styleId, ProductionLineId = line.Id,
                WorkstationPlanId = plan.Id, BomRevision = style.BomRevision,
                RoutingRevision = style.RoutingRevision,
                LineCapacityRevision = line.CapacityRevision,
                OrderQuantity = input.OrderQuantity,
                TargetDailyQuantity = input.TargetDailyQuantity,
                PlannedStartDate = input.PlannedStartDate,
                SourceReference = input.SourceReference.Trim()
            };
            db.ProductionScheduleConfirmations.Add(schedule);
            await db.SaveChangesAsync();
            return Results.Created($"/api/styles/{styleId}/schedules", new
            {
                schedule, limitation = "Manual PPC scheduling assertion; APS has NOT been contacted."
            });
        });

        api.MapPost("/schedules/{scheduleId:guid}/confirm", async (
            Guid scheduleId, ConfirmInput input, GarmentDbContext db) =>
        {
            var schedule = await db.ProductionScheduleConfirmations.FindAsync(scheduleId);
            if (schedule is null) return Results.NotFound();
            if (!ValidActor(input.ConfirmedBy))
                return Results.BadRequest(new { error = "PPC confirmer required." });
            var style = await db.Styles.FindAsync(schedule.StyleId);
            var line = await db.ProductionLines.FindAsync(schedule.ProductionLineId);
            var plan = await db.WorkstationPlans.FindAsync(schedule.WorkstationPlanId);
            if (style is null || line is null || plan is null ||
                style.Status != StyleStatus.InDevelopment ||
                style.BomRevision != schedule.BomRevision ||
                style.ApprovedBomRevision != schedule.BomRevision ||
                style.RoutingRevision != schedule.RoutingRevision ||
                style.ApprovedRoutingRevision != schedule.RoutingRevision ||
                !line.IsActive || line.CapacityRevision != schedule.LineCapacityRevision ||
                plan.Status != LineBalanceStatus.Approved ||
                plan.LineCapacityRevision != line.CapacityRevision ||
                plan.RoutingRevision != style.RoutingRevision ||
                plan.TargetDailyQuantity != schedule.TargetDailyQuantity)
                return Results.Conflict(new { error = "Schedule stale; recheck BOM, IE routing, line capacity and workstation plan." });
            bool baseline = await db.LineBalancePlans.AnyAsync(x =>
                x.StyleId == style.Id && x.ProductionLineId == line.Id &&
                x.LineCapacityRevision == line.CapacityRevision &&
                x.RoutingRevision == style.RoutingRevision &&
                x.TargetDailyQuantity == schedule.TargetDailyQuantity &&
                x.Status == LineBalanceStatus.Approved && x.LaborFeasible && x.MachineFeasible);
            bool material = await db.MaterialCoverageReviews.AnyAsync(x =>
                x.StyleId == style.Id && x.BomRevision == style.BomRevision &&
                x.Status == HandoverReviewStatus.Confirmed && x.FullyCovered &&
                x.OrderQuantity == schedule.OrderQuantity);
            if (!baseline || !material)
                return Results.Conflict(new { error = "Matching approved line plan and confirmed material review for full order quantity required." });

            if (schedule.Status == HandoverReviewStatus.Confirmed)
                return Results.Ok(schedule);
            schedule.Status = HandoverReviewStatus.Confirmed;
            schedule.ConfirmedBy = input.ConfirmedBy.Trim();
            schedule.ConfirmedAtUtc = DateTime.UtcNow;
            await db.SaveChangesAsync();
            return Results.Ok(schedule);
        });

        api.MapGet("/releases/{releaseId:guid}/handover", async (
            Guid releaseId, GarmentDbContext db) =>
        {
            var outbox = await db.ProductionHandoverOutboxes
                .SingleOrDefaultAsync(x => x.ProductionReleaseId == releaseId);
            if (outbox is null) return Results.NotFound();
            using var json = JsonDocument.Parse(outbox.PayloadJson);
            return Results.Ok(new
            {
                outboxId = outbox.Id, outbox.ProductionReleaseId,
                outbox.Status, outbox.SchemaVersion, outbox.CreatedAtUtc,
                payload = json.RootElement.Clone()
            });
        });

        return app;
    }

    private static bool ValidActor(string? actor) =>
        !string.IsNullOrWhiteSpace(actor) && actor.Trim().Length <= 100;
    private static bool ValidReference(string? reference) =>
        !string.IsNullOrWhiteSpace(reference) && reference.Trim().Length is >= 3 and <= 120;
}
