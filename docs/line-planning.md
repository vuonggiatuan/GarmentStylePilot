# Phase 3 · Production Line Engineering

## Purpose
GarmentStylePilot is a pre-production engineering platform, not an execution MES. This version ensures that a style cannot be *Production Released* to a given sewing line until IE has accepted a feasible staffing/machine plan tied to the currently approved routing and current line capacity.

## IE reference catalog
The `OperationStandards` table holds global reusable IE operation references. `Code` is unique, `Revision` increments on every edit, and `IsActive=false` blocks new imports. Importing a set of standards creates a new style routing revision with copied SMV, machine type and source standard/version metadata. The import is *not* an IE approval. A later reference change does not silently affect already-imported style routing.

## Data entities
| Table | Grain | Details |
| --- | --- | --- |
| `ProductionLines` | One physical sewing line (unique code in this MVP) | Factory, code, operators, shift, efficiency, capacity revision |
| `LineMachineCapacities` | Line × machine type | Machine counts |
| `LineBalancePlans` | One scenario snapshot | Style, routing revision, line capacity revision, target/day, takt, operators, feasibility, approver |
| `LineBalanceAssignments` | Plan × operation sequence | Official SMV, machine type, recommended dedicated operators and projected capacity |

## Calculation
Given official IE-approved operations `i`, with `SMV_i` in minutes/piece, line shift `M` minutes, planned efficiency `E` percentage and daily target `Q` pieces:

- Effective minutes/operator = `M × E / 100`.
- Target takt minutes/piece = `effective minutes / Q`.
- Staff for operation `i` = `ceiling(SMV_i / takt)`.
- Total required staff = sum across operations. This assumes each operation has dedicated staffing.
- Machine requirements for machine type `t` = sum of staff on operations assigned that machine type. Compare with available machine units.
- Feasible = total staff ≤ line's configured staff AND every machine requirement ≤ available count.
- Estimated bottleneck output/day = minimum of `floor(effective minutes × assigned staff_i / SMV_i)`.

**This is a conservative feasibility screen, not an optimized line balance**. It does not combine short operations into stations, schedule shared equipment, model WIP, shared operators, shift breaks, machine maintenance or learning curves. It also does not import live MES efficiency. "Feasible" does not guarantee actual production output.

## Approval and change control
- Only an approved IE routing revision can be planned.
- Saved line plans are immutable snapshots; editing staffing/machines/efficiency creates a new line capacity revision.
- An approved plan becomes **stale automatically** if the style's routing revision or the production line's capacity revision changes.
- The line-capacity approval is **in addition** to the manual `LineBalanceApproved` readiness checklist item.
- `POST /api/styles/{id}/release` requires both a current Phase 3 line baseline and Phase 4 qualified WorkstationPlan for the exact requested line and same target/day.
- Released styles are immutable via existing style mutation endpoints. The release document stores line code, quote and costing references.

## Example using a sewing line
A line with 20 available operators, 480-minute shift and 85% effective efficiency has 408 effective minutes/operator. For target 100 pieces/day, takt = 4.08 min/piece. A 0.62-minute overlock operation recommends 1 dedicated operator; a 1.24-minute coverstitch operation recommends 1 operator. The line must have at least 1 machine of each type.

For a large 37-operation garment, this baseline may overestimate staff compared with a properly balanced multi-operation workstation layout. IE review is mandatory.

## Next engineering milestones
1. Extend the implemented workstation grouping heuristic and operator skills matrix to precedence-aware global balancing with actual employee shift availability.
2. Factory-scoped line IDs, operator shift calendars and machine downtime.
3. Material availability from ERP/MRP and confirmed production window from APS.
4. IAM/SSO, role-based approvals, verified audit trail, transactional revision locking, migrations and evidence attachments.
5. ERP/MES release outbox with idempotent acknowledgment and operational handoff.

## Limitations
The UI is an internal **development prototype**. Approver identity is currently free-text, not verified. The API-key mechanism is not a substitute for enterprise SSO. Legacy SQLite demo databases made with `EnsureCreated` cannot be upgraded automatically to the new schema; use disposable demo data or implement EF migrations before handling business records.
