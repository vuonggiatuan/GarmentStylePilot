using GarmentStylePilot.Api.Application;
using GarmentStylePilot.Api.Data;
using GarmentStylePilot.Api.Domain;
using Microsoft.EntityFrameworkCore;

namespace GarmentStylePilot.Api.Features;

public static class GarmentEndpoints
{
    public sealed record CreateStyle(string Code, string Customer, string Description, string Currency = "USD");
    public sealed record SampleInput(string SampleType, string Revision, string? Notes);
    public sealed record BomInput(string MaterialCode, string Category, string Unit, decimal Consumption,
        decimal UnitPrice, decimal WastagePercent);
    public sealed record OperationInput(int SequenceNo, string Name, string MachineType, decimal SmvMinutes);
    public sealed record SmvEstimateInput(string ModelVersion, string? Source, int PredictedOperationCount,
        decimal PredictedSmvMinutes, decimal SmvLower80, decimal SmvUpper80,
        decimal? ViewConsistencyPercent, string? Confidence);
    public sealed record Approval(string ApprovedBy);
    public sealed record CostInput(decimal MinuteRate, decimal OverheadPerPiece, decimal TargetMarginPercent);
    public sealed record QuoteInput(Guid CostSheetId, decimal UnitPrice);
    public sealed record CheckInput(bool Verified, string VerifiedBy, string? Notes);
    public sealed record ReleaseInput(string LineCode, DateOnly PlannedStartDate, string ApprovedBy);

    public static IEndpointRouteBuilder MapGarmentEndpoints(this IEndpointRouteBuilder app)
    {
        var api = app.MapGroup("/api");
        api.MapGet("/dashboard", async (GarmentDbContext db) => Results.Ok(new
        {
            styles = await db.Styles.CountAsync(),
            inDevelopment = await db.Styles.CountAsync(x => x.Status == StyleStatus.InDevelopment),
            released = await db.Styles.CountAsync(x => x.Status == StyleStatus.Released),
            samplesApproved = await db.Samples.CountAsync(x => x.Status == SampleStatus.Approved),
            quotationsAccepted = await db.Quotes.CountAsync(x => x.Status == QuoteStatus.Accepted)
        }));

        api.MapGet("/styles", async (GarmentDbContext db) =>
            Results.Ok(await db.Styles.OrderByDescending(x => x.CreatedAtUtc).ToListAsync()));

        api.MapPost("/styles", async (CreateStyle input, GarmentDbContext db) =>
        {
            var code = input.Code?.Trim().ToUpperInvariant() ?? "";
            var currency = input.Currency?.Trim().ToUpperInvariant() ?? "";
            if (string.IsNullOrWhiteSpace(code) || string.IsNullOrWhiteSpace(input.Customer) ||
                currency.Length != 3 || !currency.All(char.IsLetter))
                return Results.BadRequest(new { error = "Code, customer and three-letter currency are required." });
            if (await db.Styles.AnyAsync(x => x.Code == code))
                return Results.Conflict(new { error = "Style code already exists." });

            var style = new Style
            {
                Code = code,
                Customer = input.Customer.Trim(),
                Description = input.Description ?? "",
                Currency = currency
            };
            db.Styles.Add(style);
            await db.SaveChangesAsync();
            return Results.Created($"/api/styles/{style.Id}", style);
        });

        api.MapGet("/styles/{id:guid}", async (Guid id, GarmentDbContext db) =>
        {
            var style = await db.Styles.FindAsync(id);
            if (style is null) return Results.NotFound();
            return Results.Ok(new
            {
                style,
                samples = await db.Samples.Where(x => x.StyleId == id).ToListAsync(),
                bom = await db.BomLines.Where(x => x.StyleId == id).ToListAsync(),
                operations = await db.SewingOperations.Where(x => x.StyleId == id)
                    .OrderBy(x => x.SequenceNo).ToListAsync(),
                smvEstimates = await db.SmvEstimates.Where(x => x.StyleId == id)
                    .OrderByDescending(x => x.CreatedAtUtc).ToListAsync(),
                costs = await db.CostSheets.Where(x => x.StyleId == id).ToListAsync(),
                quotes = await db.Quotes.Where(x => x.StyleId == id).ToListAsync(),
                checks = await db.ReadinessChecks.Where(x => x.StyleId == id).ToListAsync(),
                linePlans = await db.LineBalancePlans.Where(x => x.StyleId == id)
                    .OrderByDescending(x => x.CreatedAtUtc).ToListAsync(),
                release = await db.ProductionReleases.SingleOrDefaultAsync(x => x.StyleId == id)
            });
        });

        // The AI estimate and official IE-approved operation routing are intentionally separate.
        // Importing an estimate never changes routing revision, approvals or costing.
        api.MapPost("/styles/{id:guid}/smv-estimates", async (Guid id, SmvEstimateInput input,
            GarmentDbContext db) =>
        {
            if (await Editable(db, id) is null)
                return Results.NotFound(new { error = "Style not found or already released." });
            if (string.IsNullOrWhiteSpace(input.ModelVersion) || input.ModelVersion.Length > 80 ||
                input.PredictedOperationCount <= 0 || input.PredictedSmvMinutes <= 0 ||
                input.SmvLower80 <= 0 || input.SmvUpper80 < input.SmvLower80 ||
                input.PredictedSmvMinutes < input.SmvLower80 ||
                input.PredictedSmvMinutes > input.SmvUpper80 ||
                (input.ViewConsistencyPercent.HasValue &&
                 (input.ViewConsistencyPercent < 0 || input.ViewConsistencyPercent > 100)))
                return Results.BadRequest(new { error = "Provide valid model version, operation count, SMV and 80% interval." });

            var estimate = new SmvEstimate
            {
                StyleId = id, ModelVersion = input.ModelVersion.Trim(),
                Source = string.IsNullOrWhiteSpace(input.Source) ? "AI" : input.Source.Trim(),
                PredictedOperationCount = input.PredictedOperationCount,
                PredictedSmvMinutes = input.PredictedSmvMinutes,
                SmvLower80 = input.SmvLower80, SmvUpper80 = input.SmvUpper80,
                ViewConsistencyPercent = input.ViewConsistencyPercent,
                Confidence = input.Confidence?.Trim() ?? ""
            };
            db.SmvEstimates.Add(estimate);
            await db.SaveChangesAsync();
            return Results.Created($"/api/styles/{id}", new
            {
                estimate, classification = "AI_ESTIMATE_NOT_IE_APPROVED",
                message = "IE must independently validate and approve official routing operations."
            });
        });

                api.MapPost("/styles/{id:guid}/samples", async (Guid id, SampleInput input, GarmentDbContext db) =>
        {
            var style = await Editable(db, id);
            if (style is null) return Results.NotFound(new { error = "Style not found or released." });
            if (string.IsNullOrWhiteSpace(input.SampleType) || string.IsNullOrWhiteSpace(input.Revision))
                return Results.BadRequest(new { error = "Sample type and revision required." });
            var sample = new Sample { StyleId = id, SampleType = input.SampleType.Trim(),
                Revision = input.Revision.Trim(), Notes = input.Notes ?? "" };
            db.Samples.Add(sample);
            await db.SaveChangesAsync();
            return Results.Created($"/api/styles/{id}", sample);
        });

        api.MapPost("/samples/{sampleId:guid}/approve", async (Guid sampleId, Approval input, GarmentDbContext db) =>
        {
            var sample = await db.Samples.FindAsync(sampleId);
            if (sample is null) return Results.NotFound();
            if (await Editable(db, sample.StyleId) is null) return Results.Conflict(new { error = "Style released." });
            if (string.IsNullOrWhiteSpace(input.ApprovedBy))
                return Results.BadRequest(new { error = "Approver required." });
            sample.Status = SampleStatus.Approved;
            sample.ApprovedBy = input.ApprovedBy.Trim();
            sample.ApprovedAtUtc = DateTime.UtcNow;
            await db.SaveChangesAsync();
            return Results.Ok(sample);
        });

        api.MapPut("/styles/{id:guid}/bom", async (Guid id, List<BomInput> input, GarmentDbContext db) =>
        {
            var style = await Editable(db, id);
            if (style is null) return Results.NotFound(new { error = "Style not found or released." });
            if (input.Count == 0 || input.Any(x => string.IsNullOrWhiteSpace(x.MaterialCode) ||
                 string.IsNullOrWhiteSpace(x.Unit) || x.Consumption <= 0 || x.UnitPrice < 0 ||
                 x.WastagePercent < 0 || x.WastagePercent > 100))
                return Results.BadRequest(new { error = "BOM requires valid materials, consumption, unit price and wastage." });
            db.BomLines.RemoveRange(db.BomLines.Where(x => x.StyleId == id));
            db.BomLines.AddRange(input.Select(x => new BomLine
            {
                StyleId = id, MaterialCode = x.MaterialCode.Trim(), Category = x.Category ?? "",
                Unit = x.Unit.Trim(), Consumption = x.Consumption, UnitPrice = x.UnitPrice,
                WastagePercent = x.WastagePercent
            }));
            style.BomRevision++;
            await InvalidateReadiness(db, id);
            await db.SaveChangesAsync();
            return Results.Ok(new { style.BomRevision, approvalRequired = true });
        });

        api.MapPut("/styles/{id:guid}/operations", async (Guid id, List<OperationInput> input, GarmentDbContext db) =>
        {
            var style = await Editable(db, id);
            if (style is null) return Results.NotFound(new { error = "Style not found or released." });
            if (input.Count == 0 || input.Any(x => x.SequenceNo <= 0 || string.IsNullOrWhiteSpace(x.Name)
                 || x.SmvMinutes <= 0) || input.Select(x => x.SequenceNo).Distinct().Count() != input.Count)
                return Results.BadRequest(new { error = "Unique operation sequence, name and positive SMV required." });
            db.SewingOperations.RemoveRange(db.SewingOperations.Where(x => x.StyleId == id));
            db.SewingOperations.AddRange(input.Select(x => new SewingOperation
            {
                StyleId = id, SequenceNo = x.SequenceNo, Name = x.Name.Trim(),
                MachineType = x.MachineType ?? "", SmvMinutes = x.SmvMinutes
            }));
            style.RoutingRevision++;
            await InvalidateReadiness(db, id);
            await db.SaveChangesAsync();
            return Results.Ok(new { style.RoutingRevision, approvalRequired = true });
        });

        api.MapPost("/styles/{id:guid}/bom/approve", async (Guid id, Approval input, GarmentDbContext db) =>
        {
            var style = await Editable(db, id);
            if (style is null) return Results.NotFound();
            if (string.IsNullOrWhiteSpace(input.ApprovedBy) || style.BomRevision == 0)
                return Results.BadRequest(new { error = "Current BOM and approver required." });
            style.ApprovedBomRevision = style.BomRevision;
            await db.SaveChangesAsync();
            return Results.Ok(new { style.ApprovedBomRevision, input.ApprovedBy });
        });

        api.MapPost("/styles/{id:guid}/operations/approve", async (Guid id, Approval input, GarmentDbContext db) =>
        {
            var style = await Editable(db, id);
            if (style is null) return Results.NotFound();
            if (string.IsNullOrWhiteSpace(input.ApprovedBy) || style.RoutingRevision == 0)
                return Results.BadRequest(new { error = "Current routing/SMV and IE approver required." });
            style.ApprovedRoutingRevision = style.RoutingRevision;
            await db.SaveChangesAsync();
            return Results.Ok(new { style.ApprovedRoutingRevision, input.ApprovedBy });
        });

        api.MapPost("/styles/{id:guid}/costs", async (Guid id, CostInput input, GarmentDbContext db) =>
        {
            var style = await Editable(db, id);
            if (style is null) return Results.NotFound();
            if (style.ApprovedBomRevision != style.BomRevision || style.BomRevision == 0 ||
                style.ApprovedRoutingRevision != style.RoutingRevision || style.RoutingRevision == 0)
                return Results.Conflict(new { error = "Approve current BOM and IE routing before costing." });

            var bom = await db.BomLines.Where(x => x.StyleId == id).ToListAsync();
            var routing = await db.SewingOperations.Where(x => x.StyleId == id).ToListAsync();
            CostBreakdown breakdown;
            try
            {
                breakdown = CostCalculator.Calculate(bom, routing, input.MinuteRate,
                    input.OverheadPerPiece, input.TargetMarginPercent);
            }
            catch (ArgumentException ex) { return Results.BadRequest(new { error = ex.Message }); }

            var cost = new CostSheet
            {
                StyleId = id, BomRevision = style.BomRevision, RoutingRevision = style.RoutingRevision,
                MinuteRate = input.MinuteRate, OverheadPerPiece = input.OverheadPerPiece,
                TargetMarginPercent = input.TargetMarginPercent, MaterialCost = breakdown.MaterialCost,
                LaborCost = breakdown.LaborCost, SmvMinutes = breakdown.SmvMinutes,
                TotalCost = breakdown.TotalCost, SuggestedPrice = breakdown.SuggestedPrice
            };
            db.CostSheets.Add(cost);
            await db.SaveChangesAsync();
            return Results.Created($"/api/styles/{id}", cost);
        });

        api.MapPost("/costs/{costId:guid}/approve", async (Guid costId, Approval input, GarmentDbContext db) =>
        {
            var cost = await db.CostSheets.FindAsync(costId);
            if (cost is null) return Results.NotFound();
            var style = await Editable(db, cost.StyleId);
            if (style is null || cost.BomRevision != style.BomRevision ||
                cost.RoutingRevision != style.RoutingRevision)
                return Results.Conflict(new { error = "Costing is stale or style released." });
            if (string.IsNullOrWhiteSpace(input.ApprovedBy))
                return Results.BadRequest(new { error = "Approver required." });
            cost.IsApproved = true;
            cost.ApprovedBy = input.ApprovedBy.Trim();
            await db.SaveChangesAsync();
            return Results.Ok(cost);
        });

        api.MapPost("/styles/{id:guid}/quotes", async (Guid id, QuoteInput input, GarmentDbContext db) =>
        {
            var style = await Editable(db, id);
            if (style is null) return Results.NotFound();
            var cost = await db.CostSheets.FindAsync(input.CostSheetId);
            if (cost is null || cost.StyleId != id || !cost.IsApproved ||
                cost.BomRevision != style.BomRevision || cost.RoutingRevision != style.RoutingRevision)
                return Results.Conflict(new { error = "Approved current costing required." });
            if (input.UnitPrice <= 0)
                return Results.BadRequest(new { error = "Price must be greater than zero." });
            var quote = new Quote { StyleId = id, CostSheetId = cost.Id,
                UnitPrice = input.UnitPrice, Currency = style.Currency };
            db.Quotes.Add(quote);
            await db.SaveChangesAsync();
            return Results.Created($"/api/styles/{id}", quote);
        });

        api.MapPost("/quotes/{quoteId:guid}/accept", async (Guid quoteId, GarmentDbContext db) =>
        {
            var quote = await db.Quotes.FindAsync(quoteId);
            if (quote is null) return Results.NotFound();
            var style = await Editable(db, quote.StyleId);
            var cost = await db.CostSheets.FindAsync(quote.CostSheetId);
            if (style is null || cost is null || !cost.IsApproved ||
                cost.BomRevision != style.BomRevision || cost.RoutingRevision != style.RoutingRevision)
                return Results.Conflict(new { error = "Quote is stale or style released." });
            quote.Status = QuoteStatus.Accepted;
            quote.AcceptedAtUtc = DateTime.UtcNow;
            await db.SaveChangesAsync();
            return Results.Ok(quote);
        });

        api.MapGet("/readiness-codes", () => Results.Ok(ReadinessCodes.Required));
        api.MapPut("/styles/{id:guid}/readiness/{code}", async (Guid id, string code,
            CheckInput input, GarmentDbContext db) =>
        {
            if (!ReadinessCodes.Required.Contains(code, StringComparer.Ordinal))
                return Results.BadRequest(new { error = "Unknown readiness code." });
            if (await Editable(db, id) is null) return Results.NotFound();
            if (input.Verified && string.IsNullOrWhiteSpace(input.VerifiedBy))
                return Results.BadRequest(new { error = "Verifier required." });
            var check = await db.ReadinessChecks.SingleOrDefaultAsync(x => x.StyleId == id && x.Code == code);
            if (check is null)
            {
                check = new ReadinessCheck { StyleId = id, Code = code };
                db.ReadinessChecks.Add(check);
            }
            check.Verified = input.Verified;
            check.Notes = input.Notes ?? "";
            check.VerifiedBy = input.Verified ? input.VerifiedBy.Trim() : null;
            check.VerifiedAtUtc = input.Verified ? DateTime.UtcNow : null;
            await db.SaveChangesAsync();
            return Results.Ok(check);
        });

        api.MapGet("/styles/{id:guid}/readiness", async (Guid id, GarmentDbContext db) =>
        {
            var snapshot = await Load(db, id);
            if (snapshot is null) return Results.NotFound();
            var blockers = ProductionGate.Evaluate(snapshot.Style, snapshot.Samples,
                snapshot.Bom, snapshot.Operations, snapshot.Costs, snapshot.Quotes, snapshot.Checks,
                snapshot.LinePlans, snapshot.Lines);
            return Results.Ok(new { ready = blockers.Count == 0, blockers });
        });

        api.MapPost("/styles/{id:guid}/release", async (Guid id, ReleaseInput input, GarmentDbContext db) =>
        {
            if (string.IsNullOrWhiteSpace(input.LineCode) || string.IsNullOrWhiteSpace(input.ApprovedBy))
                return Results.BadRequest(new { error = "Line and approver required." });

            // For a production system use a database transaction, authenticated users and optimistic concurrency.
            await using var transaction = await db.Database.BeginTransactionAsync();
            var snapshot = await Load(db, id);
            if (snapshot is null) return Results.NotFound();
            var blockers = ProductionGate.Evaluate(snapshot.Style, snapshot.Samples,
                snapshot.Bom, snapshot.Operations, snapshot.Costs, snapshot.Quotes, snapshot.Checks,
                snapshot.LinePlans, snapshot.Lines, input.LineCode);
            if (blockers.Count > 0) return Results.Conflict(new { ready = false, blockers });

            var cost = snapshot.Costs.Where(x => x.IsApproved &&
                x.BomRevision == snapshot.Style.BomRevision &&
                x.RoutingRevision == snapshot.Style.RoutingRevision)
                .OrderByDescending(x => x.CreatedAtUtc).First();
            var quote = snapshot.Quotes.Where(x => x.CostSheetId == cost.Id &&
                x.Status == QuoteStatus.Accepted).OrderByDescending(x => x.AcceptedAtUtc).FirstOrDefault();
            // Accepted quote can belong to another approved current cost sheet.
            if (quote is null)
            {
                quote = snapshot.Quotes.Where(x => x.Status == QuoteStatus.Accepted &&
                    snapshot.Costs.Any(c => c.Id == x.CostSheetId && c.IsApproved &&
                        c.BomRevision == snapshot.Style.BomRevision &&
                        c.RoutingRevision == snapshot.Style.RoutingRevision))
                    .OrderByDescending(x => x.AcceptedAtUtc).First();
                cost = snapshot.Costs.Single(x => x.Id == quote.CostSheetId);
            }

            var release = new ProductionRelease
            {
                StyleId = id, LineCode = input.LineCode.Trim(),
                PlannedStartDate = input.PlannedStartDate, ApprovedBy = input.ApprovedBy.Trim(),
                CostSheetId = cost.Id, QuoteId = quote.Id
            };
            snapshot.Style.Status = StyleStatus.Released;
            db.ProductionReleases.Add(release);
            await db.SaveChangesAsync();
            await transaction.CommitAsync();
            return Results.Created($"/api/styles/{id}", release);
        });
        return app;
    }

    // A changed BOM or engineering routing invalidates the previously verified production checklist.
    private static async Task InvalidateReadiness(GarmentDbContext db, Guid styleId)
    {
        var checks = await db.ReadinessChecks.Where(x => x.StyleId == styleId).ToListAsync();
        foreach (var check in checks)
        {
            check.Verified = false;
            check.VerifiedBy = null;
            check.VerifiedAtUtc = null;
            check.Notes = "Reverification required after BOM or IE routing change.";
        }
    }

    private static Task<Style?> Editable(GarmentDbContext db, Guid id) =>
        db.Styles.SingleOrDefaultAsync(x => x.Id == id && x.Status == StyleStatus.InDevelopment);

    private sealed record Snapshot(Style Style, List<Sample> Samples, List<BomLine> Bom,
        List<SewingOperation> Operations, List<CostSheet> Costs, List<Quote> Quotes,
        List<ReadinessCheck> Checks, List<LineBalancePlan> LinePlans, List<ProductionLine> Lines);

    private static async Task<Snapshot?> Load(GarmentDbContext db, Guid id)
    {
        var style = await db.Styles.SingleOrDefaultAsync(x => x.Id == id);
        if (style is null) return null;
        return new Snapshot(style,
            await db.Samples.Where(x => x.StyleId == id).ToListAsync(),
            await db.BomLines.Where(x => x.StyleId == id).ToListAsync(),
            await db.SewingOperations.Where(x => x.StyleId == id).ToListAsync(),
            await db.CostSheets.Where(x => x.StyleId == id).ToListAsync(),
            await db.Quotes.Where(x => x.StyleId == id).ToListAsync(),
            await db.ReadinessChecks.Where(x => x.StyleId == id).ToListAsync(),
            await db.LineBalancePlans.Where(x => x.StyleId == id).ToListAsync(),
            await db.ProductionLines.ToListAsync());
    }
}
