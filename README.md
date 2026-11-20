# GarmentStylePilot

**Sample → Product Engineering → Costing & Quotation → Production Readiness**

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
5. Costing: `POST /api/styles/{id}/costs`, `POST /api/costs/{costId}/approve`.
6. Quotation: `POST /api/styles/{id}/quotes`, `POST /api/quotes/{quoteId}/accept`.
7. Verify eight readiness checks (see `GET /api/readiness-codes`) using `PUT /api/styles/{id}/readiness/{code}`.
8. View release blockers: `GET /api/styles/{id}/readiness`.
9. Production release: `POST /api/styles/{id}/release` with line, planned start date and approver.

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

## Developer verification

```bash
dotnet build src/GarmentStylePilot.Api -c Release
node --check src/GarmentStylePilot.Api/wwwroot/app.js
python3 tests/smoke_api.py
```

GitHub Actions runs all three commands. The Python smoke test starts an isolated local API and temporary SQLite database; it verifies AI/IE separation, sample/BOM/IE/cost/quote, revision invalidation and production release. On existing demo SQLite databases, adding new fields/tables to an `EnsureCreated` schema requires recreating the **disposable demo database** first. Never delete a production database; introduce EF migrations before upgrade.

## Production gate

Release blocks missing approved sample, incomplete BOM, unapproved latest BOM/routing revision, invalid operations, missing current approved costing, missing accepted quote or failed readiness checks. A release records the selected approved costing/quote and line. Released styles become read-only in these endpoints.

## Architecture

- `Domain`: style, sample, BOM, operations, costing, quote, readiness, release.
- `Application`: deterministic cost calculator and production gate.
- `Data`: EF Core persistence (SQLite or SQL Server).
- `Features`: minimal API route handlers.
- `wwwroot`: operational workspace for style, samples, BOM, IE, AI SMV estimates, costing, quote and release.

## Security and production limitations

**Prototype, not production-ready**. API key may be supplied as `GSP_API_KEY` and is mandatory when `ASPNETCORE_ENVIRONMENT=Production`. Replace it with SSO/OIDC, role-based authorizations, immutable approval audit, unique authenticated identities and tenant/factory boundaries. Business approvals currently use user-supplied strings, not verified accounts. Add optimistic concurrency, release locking, versioned costing snapshots, migrations, functional tests, error-handling middleware, ERP/MES integration and robust transactional concurrency before production use. **The dashboard now includes operational forms** (still a prototype, not a secured production application).

## Roadmap

1. Identity/RBAC, audit trail, change control and versioned technical packs.
2. Fabric consumption, BOM variants, size/color matrices, supplier quotes, USD/VND/TWD and landed costs.
3. GST/SMV estimated vs IE-approved data, operation library, machine capability, line balancing.
4. PPC material availability, capacity, PP meeting and pilot-run evidence attachments.
5. ERP/MES/APS adapters, idempotent release/outbox, permissions and cross-factory deployment.

**Status:** Initial MVP implementation in feature branch; verify build and API behavior in GitHub Actions and local .NET SDK before using.
