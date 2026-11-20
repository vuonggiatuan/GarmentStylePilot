# Phase 4 · Workstation Grouping and Operator Skills

## Purpose
Improve over Phase 3 dedicated-operation staffing estimates while preserving an auditable, IE-controlled path to production. This is a **heuristic simulation**, not a final validated physical line balance, and it never uses AI-predicted SMV as authoritative.

## Definitions
- \`LineOperators\`: each active employee has one assigned line and unique employee code.
- \`OperatorSkills\`: the employee's proficiency level 1–5 by machine type. A minimum level is chosen per scenario; default 2.
- \`WorkstationPlans\`: a saved snapshot for style, line, approved IE routing revision, line capacity revision, target/day, required & assigned operators, labor/machine/skill feasibility, IE approval.
- \`WorkstationGroups\`: station number, machine type, combined SMV, operators required, estimated capacity, operation sequences and distinct assigned employee codes.

## Grouping heuristic

1. **Only official IE-approved routing** can be simulated. AI predictions remain separate.
2. Set effective shift minutes \`M_e = shiftMinutes × efficiencyPercent / 100\` and target takt \`T = M_e / targetDailyQuantity\`.
3. Traverse routing in increasing sequence number. A new operation may be appended to the previous group **only if its machine type matches and total group SMV stays within T**. Otherwise create a new station. An operation with SMV above T is a dedicated multi-operator station.
4. Each station needs \`ceil(combinedSMV / T)\` operators. Machine demand per type is the sum of stationed operators on that machine.
5. Match qualified operators to required slots using bipartite augmenting-path matching. Each employee can fill **one slot maximum**, even if qualified for many machine types.
6. Feasibility requires: (a) group operators ≤ line max; (b) active operator roster ≥ group operators; (c) each station slot matched with a qualified operator; (d) enough configured machines by type.
7. Estimated station output/day is \`floor(M_e × allocatedOperators / stationSMV)\`; projected line output is the minimum over stations.

The heuristic **does not optimize** precedence relationships beyond preserving adjacent sequence, nonadjacent shared equipment, movement time, WIP, operator variability, staff absenteeism, layout distances, machine downtime, learning curves or multiple daily shifts.

## Worked example (not factory data)
Line: 480 minutes/shift, 85% planned efficiency, 100 pcs/day. Effective minutes = 408 and takt = 4.08 min/piece. Three official operations: Overlock 0.62, Overlock 0.60, Coverstitch 1.24 minutes.

- Phase 3 dedicated-operation baseline: 3 operators (one per operation).
- Phase 4 grouped: workstation 1 combines Overlock 0.62 + 0.60 = 1.22 min (1 operator); workstation 2 uses Coverstitch 1.24 min (1 operator). **Total 2**, assuming enough machines and two distinct qualified operators.

One multi-skilled operator with both Overlock and Coverstitch training still cannot run two separate workstations simultaneously. If they are the only employee, only one slot is filled and IE approval is blocked. With a second qualified worker, both can be assigned.

## Approval and release controls
- Create/edit operator: line capacity revision increments. Existing Phase 3/4 approved plans become stale automatically.
- Create/edit BOM or official IE routing: the Style routing/BOM revision and manual readiness checks invalidate as before.
- WorkstationPlan approval requires matching approved Phase 3 baseline for **same line, current routing revision, line capacity revision and target/day**.
- Production Release additionally requires a feasible approved WorkstationPlan with unique staff assignments and matching current baseline. The release records \`WorkstationPlanId\`.
- All plan snapshots remain for traceability. A stale approved status **does not mean** a plan can be used for release.

## API sequence
1. Configure line and machines.
2. Create active employee records with machine skills: \`POST /api/lines/{lineId}/operators\` and \`PUT /api/operators/{operatorId}\`.
3. Approve latest IE routing.
4. Create and approve **Phase 3** line baseline for selected target/day.
5. Preview \`POST /api/styles/{styleId}/workstation-plans/preview\`.
6. Save \`POST /api/styles/{styleId}/workstation-plans\` and approve \`POST /api/workstation-plans/{planId}/approve\`.
7. Complete sample, cost, quotation, material & PP readiness then Release to the matching line.

## Safety and deployment caveats
This MVP uses free-text approver names, not company identity verification. Operator skills and attendance are manual values, not synchronized from HR. The system still needs SSO/RBAC, audit, EF database migrations, transactional concurrency, versioned evidence files, ERP inventory verification and APS line scheduling before production deployment. The SQLite schema is created with EnsureCreated and is **not** automatically upgradeable from previous demo versions. Do not delete existing business data.
