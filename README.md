# GarmentStylePilot

**Sample → Product Engineering → Costing & Quotation → Production Readiness**

An ASP.NET Core 10 MVP for apparel style development and pre-production handover. Supports BOM, GST/SMV routing, approvals, costing, quotations, and line release controls.

## Quick start

Install the .NET 10 SDK, then:

```bash
dotnet restore src/GarmentStylePilot.Api/GarmentStylePilot.Api.csproj
dotnet run --project src/GarmentStylePilot.Api --launch-profile http
```

Open http://localhost:5090 for the initial dashboard, or http://localhost:5090/openapi/v1.json for the OpenAPI contract.

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

## Production gate

Release blocks missing approved sample, incomplete BOM, unapproved latest BOM/routing revision, invalid operations, missing current approved costing, missing accepted quote or failed readiness checks. A release records the selected approved costing/quote and line. Released styles become read-only in these endpoints.

## Architecture

- `Domain`: style, sample, BOM, operations, costing, quote, readiness, release.
- `Application`: deterministic cost calculator and production gate.
- `Data`: EF Core persistence (SQLite or SQL Server).
- `Features`: minimal API route handlers.
- `wwwroot`: basic dashboard; operational forms are not yet implemented.

## Security and production limitations

**Prototype, not production-ready**. API key may be supplied as `GSP_API_KEY` and is mandatory when `ASPNETCORE_ENVIRONMENT=Production`. Replace it with SSO/OIDC, role-based authorizations, immutable approval audit, unique authenticated identities and tenant/factory boundaries. Business approvals currently use user-supplied strings, not verified accounts. Add optimistic concurrency, release locking, versioned costing snapshots, migrations, functional tests, error-handling middleware, ERP/MES integration and robust transactional concurrency before production use. The dashboard is read-only except for style creation.

## Roadmap

1. Identity/RBAC, audit trail, change control and versioned technical packs.
2. Fabric consumption, BOM variants, size/color matrices, supplier quotes, USD/VND/TWD and landed costs.
3. GST/SMV estimated vs IE-approved data, operation library, machine capability, line balancing.
4. PPC material availability, capacity, PP meeting and pilot-run evidence attachments.
5. ERP/MES/APS adapters, idempotent release/outbox, permissions and cross-factory deployment.

**Status:** Initial MVP implementation in feature branch; verify build and API behavior in GitHub Actions and local .NET SDK before using.
