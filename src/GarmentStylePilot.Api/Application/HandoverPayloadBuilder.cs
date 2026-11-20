using GarmentStylePilot.Api.Domain;
using System.Text.Json;

namespace GarmentStylePilot.Api.Application;

/// <summary>
/// Persist a deterministic, immutable export envelope at Release time. No external
/// ERP/MES/APS calls are made; a future authenticated adapter will consume the outbox.
/// </summary>
public static class HandoverPayloadBuilder
{
    public static ProductionHandoverOutbox Build(
        Style style, ProductionRelease release, IReadOnlyCollection<BomLine> bom,
        IReadOnlyCollection<SewingOperation> operations, ProductionLine line,
        IReadOnlyCollection<WorkstationGroup> stations, CostSheet cost, Quote quote,
        MaterialCoverageReview review, IReadOnlyCollection<MaterialCoverageItem> materials,
        ProductionScheduleConfirmation schedule)
    {
        var snapshot = new
        {
            schemaVersion = 1,
            eventType = "GarmentStyleProductionReleased",
            releaseId = release.Id,
            releasedAtUtc = release.ApprovedAtUtc,
            style = new
            {
                id = style.Id, style.Code, style.Customer, style.Description,
                style.Currency, style.BomRevision, style.RoutingRevision
            },
            release = new
            {
                lineCode = line.Code, factoryCode = line.FactoryCode,
                release.PlannedStartDate, release.ApprovedBy,
                release.CostSheetId, release.QuoteId,
                release.WorkstationPlanId, release.MaterialCoverageReviewId,
                release.ProductionScheduleConfirmationId
            },
            bom = bom.OrderBy(x => x.MaterialCode).ThenBy(x => x.Unit).Select(x => new
            {
                x.MaterialCode, x.Category, x.Unit, x.Consumption,
                x.WastagePercent
                // UnitPrice is not included in production handover BOM.
            }).ToArray(),
            ieOperations = operations.OrderBy(x => x.SequenceNo).Select(x => new
            {
                x.SequenceNo, x.Name, x.MachineType, x.SmvMinutes,
                x.OperationStandardId, x.StandardRevision
            }).ToArray(),
            workstations = stations.OrderBy(x => x.StationNumber).Select(x => new
            {
                x.StationNumber, x.MachineType, x.CombinedSmvMinutes,
                x.RequiredOperators, x.EstimatedDailyCapacity,
                x.OperationSequences
                // AssignedEmployeeCodes intentionally excluded for data minimization.
            }).ToArray(),
            approvedCosting = new
            {
                id = cost.Id, cost.TotalCost, cost.SmvMinutes,
                cost.MaterialCost, cost.LaborCost, cost.SuggestedPrice,
                currency = style.Currency
            },
            acceptedQuote = new
            {
                id = quote.Id, quote.UnitPrice, quote.Currency, quote.AcceptedAtUtc
            },
            materialAttestation = new
            {
                id = review.Id, review.SourceType, review.SourceReference,
                review.OrderQuantity, review.ConfirmedBy, review.ConfirmedAtUtc,
                items = materials.OrderBy(x => x.MaterialCode).ThenBy(x => x.Unit).Select(x => new
                {
                    x.MaterialCode, x.Unit, x.RequiredQuantity,
                    x.AvailableQuantity, x.ShortageQuantity
                }).ToArray()
            },
            scheduleConfirmation = new
            {
                id = schedule.Id, schedule.SourceType, schedule.SourceReference,
                schedule.OrderQuantity, schedule.TargetDailyQuantity,
                schedule.PlannedStartDate, schedule.WorkstationPlanId,
                schedule.ConfirmedBy, schedule.ConfirmedAtUtc
            },
            integration = new
            {
                state = "ReadyForExport",
                deliveryPerformed = false,
                note = "Generated from manual PPC attestations; not proof of ERP stock or APS confirmation."
            }
        };

        return new ProductionHandoverOutbox
        {
            ProductionReleaseId = release.Id,
            PayloadJson = JsonSerializer.Serialize(snapshot, new JsonSerializerOptions(JsonSerializerDefaults.Web))
        };
    }
}
