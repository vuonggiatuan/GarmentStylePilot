using GarmentStylePilot.Api.Domain;

namespace GarmentStylePilot.Api.Application;

public sealed record CostBreakdown(decimal SmvMinutes, decimal MaterialCost, decimal LaborCost,
    decimal OverheadCost, decimal TotalCost, decimal SuggestedPrice);

public static class CostCalculator
{
    public static CostBreakdown Calculate(
        IEnumerable<BomLine> bom, IEnumerable<SewingOperation> operations,
        decimal minuteRate, decimal overheadPerPiece, decimal targetMarginPercent)
    {
        if (minuteRate < 0 || overheadPerPiece < 0 || targetMarginPercent < 0 || targetMarginPercent >= 100)
            throw new ArgumentOutOfRangeException(nameof(targetMarginPercent),
                "Rates cannot be negative; target margin must be between 0 and 100 (exclusive).");

        var bomLines = bom.ToArray();
        var sewingOperations = operations.ToArray();
        if (bomLines.Length == 0 || sewingOperations.Length == 0)
            throw new ArgumentException("BOM and sewing operations must be entered.");

        if (bomLines.Any(x => x.Consumption <= 0 || x.UnitPrice < 0 || x.WastagePercent < 0)
            || sewingOperations.Any(x => x.SmvMinutes <= 0))
            throw new ArgumentException("Invalid BOM consumption, price, wastage, or SMV.");

        var material = bomLines.Sum(x => x.Consumption * x.UnitPrice * (1 + x.WastagePercent / 100m));
        var smv = sewingOperations.Sum(x => x.SmvMinutes);
        var labor = smv * minuteRate;
        var total = material + labor + overheadPerPiece;
        var quote = total / (1 - targetMarginPercent / 100m);
        return new CostBreakdown(
            decimal.Round(smv, 4),
            decimal.Round(material, 4),
            decimal.Round(labor, 4),
            decimal.Round(overheadPerPiece, 4),
            decimal.Round(total, 4),
            decimal.Round(quote, 4));
    }
}
