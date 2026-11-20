# Phase 5 · PPC Production Handover Contract

## Scope and safety

Phase 5 adds *versioned, database-persisted manual attestation* for materials and line schedule, plus a read-only export envelope built in the same transaction as Production Release. It is an integration boundary for the future, **not** an operational ERP/WMS/MES/APS adapter.

The application cannot verify stock available-to-promise, reservations, inbound receipts, scheduled line calendars or supplier commitments. Thus the status names intentionally remain **MANUAL_PPC_ATTESTATION**, **MANUAL_PPC_CONFIRMATION** and **ReadyForExport**; there is no "ERP Verified" or "MES Received" status.

## 1. Material coverage snapshot

Call \`POST /api/styles/{styleId}/material-reviews\` with:

- \`orderQuantity\`: pieces to be prepared, integer 1–1,000,000
- \`sourceReference\`: source document/evidence reference entered by PPC
- \`available\`: exactly one available quantity (same UOM) for every distinct current approved BOM \`MaterialCode + Unit\`. Extraneous and missing BOM materials are rejected.

For each material/UOM key, the backend computes:

\`\`\`text
requiredQuantity =
  roundUp4Decimals(
    Σ over BOM lines for this code+unit
    (consumptionPerPiece × (1 + wastagePercent / 100) × orderQuantity)
  )
shortageQuantity = max(0, requiredQuantity - reportedAvailableQuantity)
\`\`\`

A review remains Draft until confirmed with \`POST /api/material-reviews/{reviewId}/confirm\`. Reviews with any shortage cannot be confirmed. A confirmed review is a fixed snapshot for the BOM revision and order quantity; changing BOM makes it stale.

**Example:** 1.25 m fabric per garment, 5% wastage, 1,000 garments = 1,312.5 m required. PPC reporting 1,300 m available leaves 12.5 m shortage, and confirmation is rejected. Reporting 1,320 m is sufficient *only according to the entered information*.

## 2. PPC schedule confirmation

Create \`POST /api/styles/{styleId}/schedules\` with current line and approved WorkstationPlan ID, planned start date, order quantity, daily target, and PPC source reference.

To confirm with \`POST /api/schedules/{scheduleId}/confirm\`, the following must match:

- Current approved BOM and official IE routing versions
- Active physical production line's current \`CapacityRevision\`
- Current approved WorkstationPlan, and Phase 3 baseline for the same daily target, routing and line revision
- Confirmed full material coverage for the same \`orderQuantity\`

The confirmation is invalidated for release when BOM, IE routing or production line revision changes, or when release request specifies another date/line. The source remains a **manual PPC schedule**, not APS live scheduling.

## 3. Production Gate

Previously approved Sample, BOM, IE, costing, accepted quotation, baseline, WorkstationPlan and eight manual check items remain mandatory.

Additionally, the gate requires:
- Confirmed full material coverage for current BOM revision and full order quantity
- Confirmed PPC schedule matching the same order quantity, current routing/line/workstation versions
- At release time: exact line code and planned start date from the confirmed schedule

A manual \`MaterialAvailable\` or \`CapacityConfirmed\` checklist flag alone is **not** proof of a passing Phase 5 gate.

## 4. Transactional immutable export

When \`POST /api/styles/{id}/release\` succeeds, the same database transaction creates:

- \`ProductionRelease\` row linking approved CostSheet, Quote, WorkstationPlan, MaterialReview and PPC Schedule
- \`ProductionHandoverOutbox\` row keyed uniquely by \`ProductionReleaseId\` with \`SchemaVersion=1\`, \`Status=ReadyForExport\` and serialized \`PayloadJson\`

The export includes style identity, approved BOM consumption, officially approved IE sewing routing, workstation operation groups (not employee IDs), costing, accepted quote, material quantities/references and PPC schedule.

Call \`GET /api/releases/{releaseId}/handover\` to retrieve the immutable JSON package. Repeating GET returns the same stored payload and outbox ID. The application **does not deliver** or acknowledge this package to any external system.

## 5. Future ERP/APS/MES integration

Before enabling actual dispatch:
1. Enterprise SSO/OIDC, role-specific authorizations, approval/evidence audit and factory scoping
2. EF Core migrations, concurrency locks and a secure secret/configuration store
3. ERP/WMS read-only inventory and reservation source, APS line calendar and planning acknowledgments
4. Outbox delivery worker with idempotency key \`ProductionReleaseId\`, retries/backoff, dead-letter queue, reconciliation and signed acknowledgment
5. Live MES confirmation of style/routing/line receipt rather than assuming success

## Known limitations

- Manual entered inventory can double-count available stock across multiple styles/orders. No stock reservations or aging checks.
- No size/color/width-specific BOM, lead-time modeling, substitutions or partial material releases.
- No tolerance rules for packing units, UOM conversions or purchasing MOQ.
- No stock/APS source provenance verification, no actual network integration.
- Approvers are free-text, not authenticated identities; do not deploy for real operational sign-off.
- Browser UI is not covered by automation; the CI only tests API and JavaScript syntax.
- Existing SQLite demo schemas created with \`EnsureCreated\` are not automatically upgraded when new tables are added; use a fresh **disposable demo database** or implement migrations before retaining business data.
