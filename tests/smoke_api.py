#!/usr/bin/env python3
"""End-to-end API smoke test using only Python stdlib and a local ASP.NET Core process.

Run after: dotnet build src/GarmentStylePilot.Api -c Release
This script creates and destroys an isolated SQLite database in a temp directory.
"""
from __future__ import annotations

import json
import os
import socket
import subprocess
import tempfile
import time
import urllib.error
import urllib.request
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
API = ROOT / "src" / "GarmentStylePilot.Api"


def unused_port() -> int:
    with socket.socket() as s:
        s.bind(("127.0.0.1", 0))
        return s.getsockname()[1]


def request(base: str, method: str, path: str, body=None):
    payload = json.dumps(body).encode() if body is not None else None
    headers = {"Content-Type": "application/json"}
    req = urllib.request.Request(base + path, data=payload, method=method, headers=headers)
    try:
        with urllib.request.urlopen(req, timeout=10) as res:
            content = res.read()
            return res.status, json.loads(content) if content else None
    except urllib.error.HTTPError as ex:
        content = ex.read()
        try:
            data = json.loads(content)
        except (ValueError, UnicodeDecodeError):
            data = {"error": content.decode(errors="replace")}
        return ex.code, data


def assert_status(base, method, path, body=None, expected=200):
    status, data = request(base, method, path, body)
    if status != expected:
        raise AssertionError(f"{method} {path}: expected {expected}, got {status}: {data}")
    return data


def run_scenario(base):
    create = assert_status(base, "POST", "/api/styles",
        {"code": "SMOKE-001", "customer": "Demo Apparel", "description": "Training tee",
         "currency": "USD"}, 201)
    style_id = create["id"]
    url = f"/api/styles/{style_id}"
    print("✓ created style")

    # Model-derived SMV alone must NEVER satisfy engineering approval.
    estimate = assert_status(base, "POST", url + "/smv-estimates", {
        "modelVersion": "0.1.0", "source": "GST AI",
        "predictedOperationCount": 37, "predictedSmvMinutes": 26.07,
        "smvLower80": 16.91, "smvUpper80": 35.22,
        "viewConsistencyPercent": 65.3, "confidence": "High"
    }, 201)
    assert estimate["classification"] == "AI_ESTIMATE_NOT_IE_APPROVED"
    detail = assert_status(base, "GET", url)
    assert len(detail["smvEstimates"]) == 1
    assert detail["style"]["routingRevision"] == 0
    readiness = assert_status(base, "GET", url + "/readiness")
    assert not readiness["ready"]
    assert any("IE routing/SMV" in b for b in readiness["blockers"])
    assert_status(base, "POST", url + "/smv-estimates", {
        "modelVersion": "0.1.0", "predictedOperationCount": 3,
        "predictedSmvMinutes": 60, "smvLower80": 10, "smvUpper80": 30
    }, 400)
    print("✓ AI estimate remains separate from official IE routing")

    sample = assert_status(base, "POST", url + "/samples",
        {"sampleType": "PP", "revision": "A", "notes": "Fit approved"}, 201)
    assert_status(base, "POST", f"/api/samples/{sample['id']}/approve",
        {"approvedBy": "Sample Leader"})

    bom = [
        {"materialCode": "FAB-001", "category": "Fabric", "unit": "m",
         "consumption": 1.25, "unitPrice": 2.8, "wastagePercent": 5},
        {"materialCode": "TRIM-001", "category": "Trim", "unit": "pcs",
         "consumption": 1, "unitPrice": 0.1, "wastagePercent": 0},
    ]
    operations = [
        {"sequenceNo": 1, "name": "Join shoulder", "machineType": "Overlock", "smvMinutes": 0.62},
        {"sequenceNo": 2, "name": "Attach neck rib", "machineType": "Coverstitch", "smvMinutes": 1.24}
    ]
    assert_status(base, "PUT", url + "/bom", bom)
    assert_status(base, "PUT", url + "/operations", operations)
    assert_status(base, "POST", url + "/bom/approve", {"approvedBy": "Materials Lead"})
    assert_status(base, "POST", url + "/operations/approve", {"approvedBy": "IE Lead"})

    # A release requires a separate approved engineering line capacity plan.
    line = assert_status(base, "POST", "/api/lines", {
        "code": "A01", "name": "Sewing A01", "factoryCode": "MY-PHUOC",
        "maximumOperators": 20, "shiftMinutes": 480, "plannedEfficiencyPercent": 85
    }, 201)
    assert_status(base, "PUT", f"/api/lines/{line['id']}/machines", [
        {"machineType": "Overlock", "availableCount": 5},
        {"machineType": "Coverstitch", "availableCount": 5}
    ])

    # Infeasible machine/staff plan must not receive IE approval.
    constrained = assert_status(base, "POST", "/api/lines", {
        "code": "A02", "name": "Limited A02", "factoryCode": "MY-PHUOC",
        "maximumOperators": 1, "shiftMinutes": 480, "plannedEfficiencyPercent": 85
    }, 201)
    assert_status(base, "PUT", f"/api/lines/{constrained['id']}/machines", [
        {"machineType": "Overlock", "availableCount": 1},
        {"machineType": "Coverstitch", "availableCount": 0}
    ])
    bad_plan = assert_status(base, "POST", url + "/line-plans", {
        "productionLineId": constrained["id"], "targetDailyQuantity": 100
    }, 201)
    assert not bad_plan["calculation"]["laborFeasible"]
    assert not bad_plan["calculation"]["machineFeasible"]
    assert_status(base, "POST", f"/api/line-plans/{bad_plan['plan']['id']}/approve",
        {"approvedBy": "IE Lead"}, 409)

    preview = assert_status(base, "POST", url + "/line-plans/preview", {
        "productionLineId": line["id"], "targetDailyQuantity": 100
    })
    assert preview["calculation"]["canMeetTarget"]
    plan = assert_status(base, "POST", url + "/line-plans", {
        "productionLineId": line["id"], "targetDailyQuantity": 100
    }, 201)
    assert plan["calculation"]["laborFeasible"] and plan["calculation"]["machineFeasible"]
    assert_status(base, "POST", f"/api/line-plans/{plan['plan']['id']}/approve",
        {"approvedBy": "IE Lead"})
    print("✓ engineering line balance blocks insufficient staffing/machines")

    cost = assert_status(base, "POST", url + "/costs", {
        "minuteRate": 0.08, "overheadPerPiece": 0.5, "targetMarginPercent": 20
    }, 201)
    assert float(cost["smvMinutes"]) == 1.86
    assert float(cost["totalCost"]) > float(cost["materialCost"])
    assert_status(base, "POST", f"/api/costs/{cost['id']}/approve",
        {"approvedBy": "Cost Lead"})
    quote = assert_status(base, "POST", url + "/quotes",
        {"costSheetId": cost["id"], "unitPrice": 6.5}, 201)
    assert_status(base, "POST", f"/api/quotes/{quote['id']}/accept")
    print("✓ sample → BOM → IE → costing → accepted quotation")

    codes = assert_status(base, "GET", "/api/readiness-codes")
    assert len(codes) == 8
    for code in codes:
        assert_status(base, "PUT", url + "/readiness/" + code, {
            "verified": True, "verifiedBy": "Pre-production Team",
            "notes": "Pre-production review"
        })
    assert assert_status(base, "GET", url + "/readiness")["ready"]

    # Editing IE routing must invalidate its approval, cost sheet, quote, and all previous checks.
    operations[1]["smvMinutes"] = 1.58
    assert_status(base, "PUT", url + "/operations", operations)
    detail = assert_status(base, "GET", url)
    assert all(not item["verified"] for item in detail["checks"])
    readiness = assert_status(base, "GET", url + "/readiness")
    assert not readiness["ready"]
    assert any("IE routing/SMV" in b for b in readiness["blockers"])
    assert any("Approved costing" in b for b in readiness["blockers"])
    assert any("line balance" in b.lower() for b in readiness["blockers"])
    assert_status(base, "POST", url + "/release", {
        "lineCode": "A01", "plannedStartDate": "2027-01-15",
        "approvedBy": "Production Lead"
    }, 409)
    print("✓ changed routing invalidates earlier readiness and blocks release")

    assert_status(base, "POST", url + "/operations/approve", {"approvedBy": "IE Lead"})
    fresh_plan = assert_status(base, "POST", url + "/line-plans", {
        "productionLineId": line["id"], "targetDailyQuantity": 100
    }, 201)
    assert_status(base, "POST", f"/api/line-plans/{fresh_plan['plan']['id']}/approve",
        {"approvedBy": "IE Lead"})
    cost2 = assert_status(base, "POST", url + "/costs", {
        "minuteRate": 0.08, "overheadPerPiece": 0.5, "targetMarginPercent": 20
    }, 201)
    assert float(cost2["smvMinutes"]) == 2.2
    assert_status(base, "POST", f"/api/costs/{cost2['id']}/approve",
        {"approvedBy": "Cost Lead"})
    quote2 = assert_status(base, "POST", url + "/quotes",
        {"costSheetId": cost2["id"], "unitPrice": 6.8}, 201)
    assert_status(base, "POST", f"/api/quotes/{quote2['id']}/accept")
    for code in codes:
        assert_status(base, "PUT", url + "/readiness/" + code, {
            "verified": True, "verifiedBy": "PP Team", "notes": "Rechecked after revision"
        })
    assert assert_status(base, "GET", url + "/readiness")["ready"]

    # Changing machine availability invalidates an approved plan by line revision.
    assert_status(base, "PUT", f"/api/lines/{line['id']}/machines", [
        {"machineType": "Overlock", "availableCount": 6},
        {"machineType": "Coverstitch", "availableCount": 5}
    ])
    stale = assert_status(base, "GET", url + "/readiness")
    assert not stale["ready"]
    assert any("line balance" in x.lower() for x in stale["blockers"])
    assert_status(base, "POST", url + "/release", {
        "lineCode": "A01", "plannedStartDate": "2027-01-15",
        "approvedBy": "Production Lead"
    }, 409)
    replanned = assert_status(base, "POST", url + "/line-plans", {
        "productionLineId": line["id"], "targetDailyQuantity": 100
    }, 201)
    assert_status(base, "POST", f"/api/line-plans/{replanned['plan']['id']}/approve",
        {"approvedBy": "IE Lead"})
    assert assert_status(base, "GET", url + "/readiness")["ready"]
    print("✓ new machine configuration invalidates historical line approvals")

    release = assert_status(base, "POST", url + "/release", {
        "lineCode": "A01", "plannedStartDate": "2027-01-15",
        "approvedBy": "Production Lead"
    }, 201)
    assert release["quoteId"] == quote2["id"]
    assert_status(base, "POST", url + "/release", {
        "lineCode": "A02", "plannedStartDate": "2027-01-16",
        "approvedBy": "Production Lead"
    }, 409)
    assert_status(base, "PUT", url + "/bom", bom, 404)
    final = assert_status(base, "GET", url)
    assert final["style"]["status"] == "Released"
    print("✓ release succeeds once, links current cost/quote, and freezes style")


def main():
    port = unused_port()
    base = f"http://127.0.0.1:{port}"
    with tempfile.TemporaryDirectory(prefix="gsp-smoke-") as temp:
        env = os.environ.copy()
        env["ASPNETCORE_ENVIRONMENT"] = "Development"
        env["Database__Provider"] = "Sqlite"
        env["ConnectionStrings__Sqlite"] = "Data Source=" + str(Path(temp) / "smoke.db")
        env.pop("GSP_API_KEY", None)
        with open(Path(temp) / "app.log", "wb") as log:
            command = ["dotnet", "run", "--no-build", "-c", "Release",
                       "--project", str(API), "--no-launch-profile",
                       "--", "--urls", base]
            process = subprocess.Popen(command, cwd=ROOT, env=env, stdout=log, stderr=subprocess.STDOUT)
            try:
                deadline = time.monotonic() + 30
                while time.monotonic() < deadline:
                    if process.poll() is not None:
                        raise RuntimeError("API exited unexpectedly before becoming healthy.")
                    try:
                        status, data = request(base, "GET", "/healthz")
                        if status == 200 and data["status"] == "ok":
                            break
                    except (urllib.error.URLError, TimeoutError):
                        pass
                    time.sleep(0.3)
                else:
                    raise TimeoutError("ASP.NET Core did not start within 30 seconds")
                run_scenario(base)
            except Exception:
                log.flush()
                print("\n--- API log ---")
                print(Path(temp, "app.log").read_text(errors="replace")[-9000:])
                raise
            finally:
                process.terminate()
                try:
                    process.wait(timeout=5)
                except subprocess.TimeoutExpired:
                    process.kill()
                    process.wait()


if __name__ == "__main__":
    main()
