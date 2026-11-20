namespace GarmentStylePilot.Api.Domain;

public enum SampleStatus { Draft, Approved, Rejected }
public enum QuoteStatus { Proposed, Accepted, Rejected }
public enum StyleStatus { InDevelopment, Released }

public sealed class Style
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Code { get; set; } = "";
    public string Customer { get; set; } = "";
    public string Description { get; set; } = "";
    public string Currency { get; set; } = "USD";
    public StyleStatus Status { get; set; } = StyleStatus.InDevelopment;
    public int BomRevision { get; set; }
    public int ApprovedBomRevision { get; set; } = -1;
    public int RoutingRevision { get; set; }
    public int ApprovedRoutingRevision { get; set; } = -1;
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}

public sealed class Sample
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid StyleId { get; set; }
    public string SampleType { get; set; } = "Fit";
    public string Revision { get; set; } = "A";
    public string Notes { get; set; } = "";
    public SampleStatus Status { get; set; } = SampleStatus.Draft;
    public string? ApprovedBy { get; set; }
    public DateTime? ApprovedAtUtc { get; set; }
}

public sealed class BomLine
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid StyleId { get; set; }
    public string MaterialCode { get; set; } = "";
    public string Category { get; set; } = "Fabric";
    public string Unit { get; set; } = "m";
    public decimal Consumption { get; set; }
    public decimal UnitPrice { get; set; }
    public decimal WastagePercent { get; set; }
}

public sealed class SewingOperation
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid StyleId { get; set; }
    public int SequenceNo { get; set; }
    public string Name { get; set; } = "";
    public string MachineType { get; set; } = "";
    public decimal SmvMinutes { get; set; }
}

public sealed class CostSheet
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid StyleId { get; set; }
    public int BomRevision { get; set; }
    public int RoutingRevision { get; set; }
    public decimal MinuteRate { get; set; }
    public decimal OverheadPerPiece { get; set; }
    public decimal TargetMarginPercent { get; set; }
    public decimal MaterialCost { get; set; }
    public decimal LaborCost { get; set; }
    public decimal SmvMinutes { get; set; }
    public decimal TotalCost { get; set; }
    public decimal SuggestedPrice { get; set; }
    public bool IsApproved { get; set; }
    public string? ApprovedBy { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}

public sealed class Quote
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid StyleId { get; set; }
    public Guid CostSheetId { get; set; }
    public decimal UnitPrice { get; set; }
    public string Currency { get; set; } = "USD";
    public QuoteStatus Status { get; set; } = QuoteStatus.Proposed;
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime? AcceptedAtUtc { get; set; }
}

public sealed class ReadinessCheck
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid StyleId { get; set; }
    public string Code { get; set; } = "";
    public bool Verified { get; set; }
    public string Notes { get; set; } = "";
    public string? VerifiedBy { get; set; }
    public DateTime? VerifiedAtUtc { get; set; }
}

public sealed class ProductionRelease
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid StyleId { get; set; }
    public string LineCode { get; set; } = "";
    public DateOnly PlannedStartDate { get; set; }
    public string ApprovedBy { get; set; } = "";
    public DateTime ApprovedAtUtc { get; set; } = DateTime.UtcNow;
    public Guid CostSheetId { get; set; }
    public Guid QuoteId { get; set; }
}
