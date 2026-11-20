# GarmentStylePilot

**Sample → IE Engineering → Line & Workstation Planning → Costing & Quotation → Production Readiness**

An ASP.NET Core 10 MVP for apparel style development and pre-production handover. Supports BOM, GST/SMV routing, approvals, costing, quotations, and line release controls.

## Quick start

Install the .NET 10 SDK, then:

```bash
dotnet restore src/GarmentStylePilot.Api/GarmentStylePilot.Api.csproj
dotnet run --project src/GarmentStylePilot.Api --launch-profile http
```

Open http://localhost:5090 for the operational development workspace, or http://localhost:5090/openapi/v1.json for the OpenAPI contract.

By default the database is SQLite (created automatically). To use SQL Server, set `Database__Provider=SqlServer` and `ConnectionStrings__SqlServer` with a valid connection string. **The prototype uses EnsureCreated and does not apply EF migrations**. For real deployment, introduce migrations and protect/backup business data.

## Workflow

1. Create style: `POST /api/styles`.
2. Add sample and approve it: `POST /api/styles/{id}/samples`, `POST /api/samples/{sampleId}/approve`.
3. Set BOM and routing: `PUT /api/styles/{id}/bom`, `PUT /api/styles/{id}/operations`.
4. Approve their latest revisions: `POST /api/styles/{id}/bom/approve`, `POST /api/styles/{id}/operations/approve`.
5. Reuse IE operation standards: `GET /api/operation-standards`, `POST /api/operation-standards`, `POST /api/styles/{id}/operations/from-library`. Every import creates a new routing revision that requires IE approval.
6. Register line capacity and available machines: `POST /api/lines`, `PUT /api/lines/{lineId}/machines`.
7. Preview staffing vs target/day: `POST /api/styles/{id}/line-plans/preview`.
8. Save a line plan and have IE approve it: `POST /api/styles/{id}/line-plans`, `POST /api/line-plans/{planId}/approve`.
9. Register line operators and machine skills: `POST /api/lines/{lineId}/operators`, `PUT /api/operators/{id}`.
10. Preview and save skill-aware workstation grouping: `POST /api/styles/{id}/workstation-plans/preview` and `POST /api/styles/{id}/workstation-plans`.
11. IE approves a current feasible workstation plan with the **same daily target** as the Phase 3 baseline: `POST /api/workstation-plans/{planId}/approve`.
12. Costing: `POST /api/styles/{id}/costs`, `POST /api/costs/{costId}/approve`.
13. Quotation: `POST /api/styles/{id}/quotes`, `POST /api/quotes/{quoteId}/accept`.
14. Verify eight readiness checks (see `GET /api/readiness-codes`) using `PUT /api/styles/{id}/readiness/{code}`.
15. View release blockers: `GET /api/styles/{id}/readiness`.
16. Production release: `POST /api/styles/{id}/release` with the approved target line code, planned start date and approver. Both a current Phase 3 line baseline and a current Phase 4 qualified workstation plan are mandatory.

### Example

```bash
curl -X POST http://localhost:5090/api/styles \
  -H 'Content-Type: application/json' \
  -d '{"code":"ST-001","customer":"Demo","description":"Performance T-shirt","currency":"USD"}'
```

See the sample HTTP requests in `docs/api-examples.http`.

## Cost formula (MVP)

Material cost = sum(consumption × unit price × (1 + wastage% / 100)).

Labor cost = sum(operation SMV) × minute rate.

Total cost = material + labor + overhead per piece.

Suggested quote price = total cost / (1 - target gross margin%).

All costs are interpreted in the style's declared currency. The MVP does **not** yet calculate graded size consumption, landed cost, FX, fabric width/yield, taxes, subcontracting or real-time inventory. SMV is engineering-entered and must be approved; model-predicted SMV is **not** an IE-approved SMV.

## AI prediction vs. IE-approved SMV

Upload a forecast through `POST /api/styles/{id}/smv-estimates`. The payload contains model version, operation count, predicted SMV, 80% bounds, confidence and view consistency. AI estimates are **informational only**: they do not alter operation routing, approved SMV, costing or production readiness. IE must explicitly maintain `/operations` and approve the current revision.

When BOM or IE routing changes, all verified production readiness checks are reset and require explicit re-verification. Approved cost sheets and customer quotes from older revisions cannot pass the gate.

## Phase 3: IE operation library

Create reusable GST/IE operation reference standards with `Code`, `ComponentName`, `OperationName`, `MachineType`, `MethodCode` and `ReferenceSmvMinutes`. Changing an existing standard increments `Revision`. Styles can import a selected list of standards into a **new routing revision** via `POST /api/styles/{id}/operations/from-library`.

The imported operation keeps a snapshot of name, machine, SMV, standard ID and standard revision. Editing the shared standard later does **not** alter historical style routing; importing a reference does **not** approve it. IE still has to review operations and explicitly approve the current routing revision before costing, line planning or release. See `docs/api-examples.http`.

## Phase 3: Line capacity & engineering staffing

The Line Planning tab lets IE configure a production line's factory, code, available operators, shift length, planned efficiency and machine inventory. A saved plan captures the official IE routing revision and the line capacity revision. Approval is blocked if **either operators or machines are insufficient**; a release is blocked unless it references a still-current approved plan for the requested line.

The simulator uses a **conservative dedicated-operation allocation**, not a fully optimized line balance: effective operator minutes = shift minutes × planned efficiency / 100; target takt = effective minutes / target daily quantity; recommended staff per operation = ceiling(operation SMV / target takt). Operators are totaled over operations, and staffing demand by machine type is compared to available machines. A cross-trained worker may cover multiple operations in real production; work sharing and constraints such as bottleneck recovery, feeding, changeovers and absenteeism are **not modeled yet**. IE must validate the result.

See [engineering planning notes](docs/line-planning.md) and `docs/api-examples.http`.

## Phase 4: Operator Skill Matrix and Workstation Balancing

The **Workstations** tab lets IE register assigned operators with machine skill levels 1–5. A minimum skill level (default **2**) controls who is considered qualified. A cross-trained person is never counted twice, because the simulator matches distinct employees to workstation slots.

**Workstation Grouping** uses only the current approved IE routing. Adjacent operations of the same machine type may be combined into one station while their combined SMV stays within takt. Operations longer than takt are assigned enough parallel workers to meet the proposed output. The simulator checks the operator roster, line headcount and machine inventory.

This is a **deterministic heuristic, not global optimization**. It does not model equipment sharing between different machine types, formal precedence diagrams, walking distance, real attendance, interchangeable machines or dynamic changes in shift efficiency. IE validates every proposed layout and workstation allocation before approval.

Creating or editing an operator or skills increments the line capacity revision and makes previously approved line/workstation scenarios stale. A workstation scenario can be approved only if a current, feasible Phase 3 baseline exists for the **same line and target/day**. Production Release requires both approved plans. Release records the chosen WorkstationPlanId for traceability.

See [Workstation balancing notes](docs/workstation-balancing.md).

## Developer verification

```bash
dotnet build src/GarmentStylePilot.Api -c Release
node --check src/GarmentStylePilot.Api/wwwroot/app.js
python3 tests/smoke_api.py
```

GitHub Actions runs all three commands. The Python smoke test starts an isolated local API and temporary SQLite database; it verifies AI/IE separation, sample/BOM/IE/cost/quote, line staffing and machine shortfalls, distinct-employee skill matching, same-machine workstation grouping, revision invalidation and production release. On existing demo SQLite databases, adding new fields/tables to an `EnsureCreated` schema requires recreating the **disposable demo database** first. Never delete a production database; introduce EF migrations before upgrade.

## Production gate

Release blocks missing approved sample, incomplete BOM, unapproved latest BOM/routing revision, invalid operations, **no approved feasible line baseline and qualified workstation plan for the destination line and matching target**, missing current approved costing, missing accepted quote or failed readiness checks. A release records the selected approved costing/quote and line. Released styles become read-only in these endpoints.

## Architecture

- `Domain`: style, sample, BOM, revisioned IE standard operations, routing, costing, quote, production lines, machine capacity, operator skills, baseline/workstation plans and production release.
- `Application`: deterministic cost calculator, dedicated-operation baseline, skill-aware workstation grouping and production gate.
- `Data`: EF Core persistence (SQLite or SQL Server).
- `Features`: minimal API route handlers.
- `wwwroot`: operational workspace for style, samples, BOM, IE, AI SMV estimates, line/workstation planning, employee skills, costing, quote and release.

## Security and production limitations

**Prototype, not production-ready**. API key may be supplied as `GSP_API_KEY` and is mandatory when `ASPNETCORE_ENVIRONMENT=Production`. Replace it with SSO/OIDC, role-based authorizations, immutable approval audit, unique authenticated identities and tenant/factory boundaries. Business approvals currently use user-supplied strings, not verified accounts. Add optimistic concurrency, release locking, versioned costing snapshots, migrations, functional tests, error-handling middleware, ERP/MES integration and robust transactional concurrency before production use. **The dashboard now includes operational forms** (still a prototype, not a secured production application).

## Roadmap

1. Identity/RBAC, audit trail, change control and versioned technical packs.
2. Fabric consumption, BOM variants, size/color matrices, supplier quotes, USD/VND/TWD and landed costs.
3. Extend the new IE library, skill matrix and heuristic workstation grouping to real precedence-aware line balancing, operator scheduling and APS capacity constraints.
4. PPC material availability, capacity, PP meeting and pilot-run evidence attachments.
5. ERP/MES/APS adapters, idempotent release/outbox, permissions and cross-factory deployment.

**Status:** Phase 3 MVP available in a feature pull request; GitHub Actions verifies build, JavaScript syntax and an API smoke test. Browser UI is not automated-tested. Review and merge the PR manually before using the `main` branch.
