// GarmentStylePilot operational MVP (browser-only UI; permissions are enforced server-side).
const state = { styles: [], stats: null, detail: null, readiness: null, codes: [], lines: [], plans: [], standards: [],
  workers: [], workstationPlans: [], workstationPreview: null, wsSkillLevel: 2,
  materialReviews: [], schedules: [], handover: null, plannedOrderQuantity: 1000,
  selectedId: null, selectedLineId: null, tab: "overview", query: "",
  planTargetQty: 100, linePreview: null, expandedPlan: null };
const $ = (id) => document.getElementById(id);
const h = (value) => String(value ?? "").replace(/[&<>"']/g, (c) => ({ "&": "&amp;", "<": "&lt;", ">": "&gt;", '"': "&quot;", "'": "&#39;" }[c]));
const fmt = (n, digits = 2) => Number(n ?? 0).toLocaleString("en-US", { minimumFractionDigits: digits, maximumFractionDigits: digits });
const dateText = (value) => value ? new Date(value).toLocaleString("vi-VN") : "—";
const active = () => state.detail && state.detail.style.status !== "Released";
const actor = () => {
  const name = $("actor").value.trim();
  if (!name) throw new Error("Nhập tên người thao tác ở góc trên trước khi phê duyệt.");
  return name;
};

const checkLabels = {
  TechPackApproved: ["Tech Pack được duyệt", "Tài liệu kỹ thuật / bảng thông số chính thức"],
  MaterialAvailable: ["Nguyên phụ liệu sẵn sàng", "Fabric, trims và nguồn cung đã xác nhận"],
  CapacityConfirmed: ["Công suất được xác nhận", "PPC đã bố trí chuyền, thời gian, nhân lực"],
  LineBalanceApproved: ["Cân bằng chuyền được duyệt", "IE xác nhận routing, SMV, layout"],
  PpMeetingCompleted: ["Hoàn thành PP Meeting", "Pre-production meeting và hành động tồn đọng"],
  PilotRunApproved: ["Pilot Run được duyệt", "Chạy thử, xác nhận rủi ro sản xuất"],
  QualityPlanApproved: ["Kế hoạch chất lượng", "QC checkpoints, tiêu chuẩn nghiệm thu"],
  MachineReady: ["Máy móc sẵn sàng", "Thiết bị, cữ gá, kỹ thuật, bảo trì"]
};

let toastTimer;
function notify(message, error = false) {
  const el = $("toast");
  el.textContent = message; el.hidden = false; el.classList.toggle("error", error);
  clearTimeout(toastTimer);
  toastTimer = setTimeout(() => { el.hidden = true; }, 5200);
}
async function api(path, options = {}) {
  const key = sessionStorage.getItem("gsp_api_key");
  const response = await fetch(path, {
    ...options, headers: {
      "Content-Type": "application/json",
      ...(key ? { "X-Api-Key": key } : {}),
      ...(options.headers || {})
    }
  });
  const text = await response.text();
  let data = null;
  try { data = text ? JSON.parse(text) : null; } catch { data = { error: text.slice(0, 200) }; }
  if (!response.ok) {
    const blocks = data?.blockers?.length ? ": " + data.blockers.join(" • ") : "";
    throw new Error((data?.error || data?.title || "HTTP " + response.status) + blocks);
  }
  return data;
}
async function reload() {
  const [stats, styles, codes, lines, standards] = await Promise.all([
    api("/api/dashboard"), api("/api/styles"), api("/api/readiness-codes"), api("/api/lines"),
    api("/api/operation-standards")
  ]);
  state.stats = stats; state.styles = styles; state.codes = codes;
  state.lines = lines; state.standards = standards;
  renderSummary(); renderStyles();
  if (state.selectedId) await openStyle(state.selectedId, false);
  $("lastUpdated").textContent = "Đồng bộ lúc " + new Date().toLocaleTimeString("vi-VN");
}
async function openStyle(id, updateList = true) {
  state.selectedId = id;
  try {
    const currentLine = state.lines.find(x=>x.id===state.selectedLineId) || state.lines[0];
    if (currentLine && !state.selectedLineId) state.selectedLineId = currentLine.id;
    const [detail, readiness, plans, workstationPlans, workers, materialReviews, schedules] = await Promise.all([
      api("/api/styles/" + id), api("/api/styles/" + id + "/readiness"),
      api("/api/styles/" + id + "/line-plans"),
      api("/api/styles/" + id + "/workstation-plans"),
      currentLine ? api("/api/lines/" + currentLine.id + "/operators") : Promise.resolve([]),
      api("/api/styles/" + id + "/material-reviews"),
      api("/api/styles/" + id + "/schedules")
    ]);
    state.detail = detail; state.readiness = readiness; state.plans = plans;
    state.workstationPlans = workstationPlans; state.workers = workers;
    state.materialReviews=materialReviews; state.schedules=schedules;
    state.handover = null;
    if (detail.release?.id) {
      try { state.handover=await api("/api/releases/"+detail.release.id+"/handover"); }
      catch (error) { console.warn("Handover not found for legacy release:",error.message); }
    }
    renderDetail();
    if (updateList) renderStyles();
  } catch (error) {
    state.selectedId = null; state.detail = null;
    renderStyles(); throw error;
  }
}
function renderSummary() {
  const s = state.stats;
  const entries = [
    ["Mã hàng", s.styles, "Tổng mã hàng đang quản lý"],
    ["Đang phát triển", s.inDevelopment, "Sample / IE / Costing"],
    ["Mẫu đã duyệt", s.samplesApproved, "Sample approved"],
    ["Đã phát hành", s.released, "Production released"]
  ];
  $("summary").innerHTML = entries.map(([label, value, sub]) =>
    "<div class='stat-card'><div class='value'>" + h(value) + "</div><div class='label'>" +
    h(label) + "</div><div class='hint'>" + h(sub) + "</div></div>"
  ).join("");
}
function pill(status) {
  const css = status === "Released" || status === "Approved" || status === "Accepted" ? "good" :
    status === "InDevelopment" || status === "Proposed" ? "blue" : "warning";
  const names = { Released: "Production Released", InDevelopment: "Đang phát triển", Approved: "Đã duyệt", Accepted: "Đã chấp nhận", Draft: "Nháp", Proposed: "Đề xuất" };
  return "<span class='pill " + css + "'>" + h(names[status] || status) + "</span>";
}
function renderStyles() {
  const matches = state.styles.filter(s =>
    (s.code + " " + s.customer + " " + s.description).toLowerCase().includes(state.query.toLowerCase()));
  $("styleCount").textContent = matches.length;
  $("styleList").innerHTML = matches.map(s =>
    "<button class='style-item " + (s.id === state.selectedId ? "selected" : "") +
    "' data-action='select-style' data-id='" + h(s.id) + "'>" +
    "<span class='name'>" + h(s.code) + "</span><span class='meta'>" + h(s.customer) + "</span>" +
    pill(s.status) + "</button>"
  ).join("") || "<div class='empty'>Không tìm thấy mã hàng nào.</div>";
}
function renderDetail() {
  const d = state.detail, s = d.style;
  const tabs = [
    ["overview", "Tổng quan"], ["sample", "Sample"], ["bom", "BOM"],
    ["ie", "IE · GST/SMV"], ["line", "Line Planning"],
    ["workstations", "Workstations"], ["cost", "Costing & Quote"],
    ["handover", "PPC Handover"], ["readiness", "Line Readiness"]
  ];
  $("detailPanel").innerHTML =
    "<div class='detail-head'><div><span class='eyebrow'>STYLE / " + h(s.customer) +
    "</span><h3>" + h(s.code) + "</h3><p>" + h(s.description || "Chưa có mô tả") +
    "</p></div><div>" + pill(s.status) + "</div></div>" +
    "<div class='tabs' role='tablist'>" +
    tabs.map(([key,label]) =>
      "<button role='tab' aria-selected='" + (state.tab === key) + "' class='tab " +
      (state.tab === key ? "active" : "") + "' data-action='tab' data-tab='" + h(key) + "'>" +
      h(label) + "</button>").join("") + "</div>" +
    "<div class='tab-pane'>" + renderTab() +
    (active() ? "" : "<div class='readonly-note'>Mã hàng đã được phát hành. Các API chỉnh sửa sẽ bị từ chối.</div>") +
    "</div>";
}
function renderTab() {
  switch (state.tab) {
    case "sample": return sampleTab();
    case "bom": return bomTab();
    case "ie": return ieTab();
    case "line": return lineTab();
    case "workstations": return workstationTab();
    case "cost": return costTab();
    case "handover": return handoverTab();
    case "readiness": return readinessTab();
    default: return overviewTab();
  }
}
function overviewTab() {
  const d=state.detail, s=d.style;
  const currentCost=d.costs.filter(c=>c.isApproved && c.bomRevision===s.bomRevision && c.routingRevision===s.routingRevision).at(-1);
  const accepted=d.quotes.find(q=>q.status==="Accepted" && d.costs.some(c=>c.id===q.costSheetId && c.isApproved && c.bomRevision===s.bomRevision && c.routingRevision===s.routingRevision));
  const official = d.operations.reduce((a, o)=>a+Number(o.smvMinutes),0);
  const latest = d.smvEstimates[0];
  const balanced = state.plans.some(({plan}) => {
    const line = state.lines.find(x=>x.id===plan.productionLineId);
    return line && line.isActive && line.capacityRevision===plan.lineCapacityRevision &&
      plan.routingRevision===s.routingRevision && plan.status==="Approved" &&
      plan.laborFeasible && plan.machineFeasible;
  });
  const stationApproved=state.workstationPlans.some(({plan})=>{
    const line=state.lines.find(x=>x.id===plan.productionLineId);
    return line && line.isActive && line.capacityRevision===plan.lineCapacityRevision &&
      plan.routingRevision===s.routingRevision && plan.status==="Approved" &&
      plan.laborFeasible && plan.machineFeasible && plan.skillFeasible &&
      plan.assignedOperators===plan.requiredOperators &&
      state.plans.some(({plan:baseline})=>baseline.productionLineId===line.id &&
        baseline.targetDailyQuantity===plan.targetDailyQuantity &&
        baseline.routingRevision===s.routingRevision &&
        baseline.lineCapacityRevision===line.capacityRevision &&
        baseline.status==="Approved" && baseline.laborFeasible && baseline.machineFeasible);
  });
  const steps = [
    ["01", "Sample", d.samples.some(x=>x.status==="Approved"), "Fit / PP Sample approval"],
    ["02", "BOM", s.bomRevision>0 && s.bomRevision===s.approvedBomRevision, "Material consumption revision " + s.bomRevision],
    ["03", "IE Routing", s.routingRevision>0 && s.routingRevision===s.approvedRoutingRevision, "Official SMV " + fmt(official) + " min"],
    ["04", "Line Planning", balanced, "Feasible IE-approved line staffing and machine capacity"],
    ["05", "Workstations", stationApproved, "Qualified operators & grouped workstations"],
    ["06", "Costing", !!currentCost, "Approved cost sheet"],
    ["07", "Quotation", !!accepted, "Customer-accepted quote"],
    ["08", "Material Coverage", state.materialReviews.some(({review})=>
        review.status==="Confirmed" && review.fullyCovered && review.bomRevision===s.bomRevision),
        "Material quantities for full order"],
    ["09", "PPC Schedule", state.schedules.some(x=>x.status==="Confirmed" &&
        x.bomRevision===s.bomRevision && x.routingRevision===s.routingRevision &&
        state.lines.some(l=>l.id===x.productionLineId && l.capacityRevision===x.lineCapacityRevision)),
        "Approved line/date and order quantity"],
    ["10", "Line Readiness", state.readiness.ready || s.status==="Released", "8 production checkpoints"]
  ];
  return "<h4 class='work-title'>Trạng thái mã hàng</h4>" +
    "<p class='work-desc'>Một mã hàng chỉ được bàn giao lên chuyền khi toàn bộ phê duyệt và điều kiện sản xuất đáp ứng yêu cầu.</p>" +
    "<div class='meta-grid'><div class='meta-card'><span>BOM Revision</span><strong>R" + h(s.bomRevision) +
    "</strong><small class='hint'>Approved: R" + h(s.approvedBomRevision) + "</small></div>" +
    "<div class='meta-card'><span>IE Routing Revision</span><strong>R" + h(s.routingRevision) +
    "</strong><small class='hint'>Approved: R" + h(s.approvedRoutingRevision) + "</small></div>" +
    "<div class='meta-card'><span>IE Official SMV</span><strong>" + fmt(official) +
    "</strong><small class='hint'>phút / sản phẩm" + (latest ? " · AI: " + fmt(latest.predictedSmvMinutes) : "") + "</small></div></div>" +
    "<div class='section-card'><h4>Development Stage Gates</h4>" +
    "<table class='compact-table'><thead><tr><th>Stage</th><th>Phạm vi</th><th>Trạng thái</th></tr></thead><tbody>" +
    steps.map(([n,name,passed,description]) =>
      "<tr><td>" + h(n) + " · " + h(name) + "</td><td class='data-muted'>" + h(description) +
      "</td><td>" + (passed ? "<span class='pill good'>✓ Hoàn tất</span>" : "<span class='pill warning'>Chưa hoàn tất</span>") +
      "</td></tr>").join("") + "</tbody></table></div>" +
    (s.status==="Released"
      ? "<div class='alert success'><strong>Đã Production Release</strong> — Chuyền " + h(d.release?.lineCode) +
        ", ngày dự kiến " + h(d.release?.plannedStartDate) + ", duyệt bởi " + h(d.release?.approvedBy) + ".</div>"
      : "<div class='alert " + (state.readiness.ready ? "success" : "warning") + "'><strong>" +
        (state.readiness.ready ? "Sẵn sàng phát hành sản xuất" : "Production Gate chưa đạt") +
        "</strong><br>" + (state.readiness.ready ? "Có thể chuyển sang tab Line Readiness để phát hành." :
        "Còn " + h(state.readiness.blockers.length) + " điều kiện chưa đạt.") + "</div>") +
    "<div class='flex'><button class='button secondary' data-action='tab' data-tab='sample'>Đi đến Sample →</button>" +
    "<button class='button ghost' data-action='tab' data-tab='readiness'>Xem Production Gate</button></div>";
}
function sampleTab() {
  const d=state.detail, disabled=active()?"":"disabled";
  return "<h4 class='work-title'>Sample Development</h4><p class='work-desc'>Quản lý các lần lấy mẫu, thử fit, và phê duyệt mẫu trước khi lên chuyền.</p>" +
    "<div class='section-card'><h4>Danh sách mẫu</h4>" +
    (d.samples.length ?
      "<div class='table-wrap'><table><thead><tr><th>Loại mẫu</th><th>Version</th><th>Ghi chú</th><th>Trạng thái</th><th>Thao tác</th></tr></thead><tbody>" +
      d.samples.map(x => "<tr><td>" + h(x.sampleType) + "</td><td>" + h(x.revision) + "</td><td>" +
      h(x.notes) + "</td><td>" + pill(x.status) + "</td><td>" +
      (x.status==="Approved" ? "<span class='data-muted'>" + h(x.approvedBy) + "</span>" :
        "<button class='button tiny secondary' data-action='approve-sample' data-id='" + h(x.id) + "' " + disabled + ">Duyệt</button>") +
      "</td></tr>").join("") + "</tbody></table></div>" :
      "<p>Chưa có mẫu nào. Nhập mẫu đầu tiên bên dưới.</p>") + "</div>" +
    "<div class='section-card'><h4>Thêm mẫu</h4><form id='sampleForm' class='form-row'>" +
    "<label>Loại mẫu<select name='sampleType'><option>Fit</option><option>Proto</option><option>Salesman</option><option>PP</option><option>Size Set</option></select></label>" +
    "<label>Revision<input name='revision' placeholder='A' required></label>" +
    "<label>Ghi chú<input name='notes' placeholder='Fit comments, test result'></label>" +
    "<button class='button primary' " + disabled + ">Lưu mẫu</button></form></div>";
}
function bomRow(x={}) {
  return "<tr><td><input name='materialCode' required value='" + h(x.materialCode || "") + "' placeholder='FAB-001'></td>" +
  "<td><select name='category'>" + ["Fabric","Trim","Packaging","Other"].map(k=>"<option " +(x.category===k?"selected":"")+">"+k+"</option>").join("")+"</select></td>" +
  "<td><input name='unit' required value='" + h(x.unit || "m") + "'></td>" +
  "<td><input type='number' name='consumption' required step='any' min='0.000001' value='" + h(x.consumption ?? 1) + "'></td>" +
  "<td><input type='number' name='unitPrice' required step='any' min='0' value='" + h(x.unitPrice ?? 0) + "'></td>" +
  "<td><input type='number' name='wastagePercent' required step='any' min='0' max='100' value='" + h(x.wastagePercent ?? 0) + "'></td>" +
  "<td><button class='button tiny danger' type='button' data-action='remove-row'>×</button></td></tr>";
}
function bomTab() {
  const d=state.detail,s=d.style,disabled=active()?"":"disabled";
  const approved=s.bomRevision>0 && s.bomRevision===s.approvedBomRevision;
  const est=d.bom.reduce((a,x)=>a+Number(x.consumption)*Number(x.unitPrice)*(1+Number(x.wastagePercent)/100),0);
  return "<h4 class='work-title'>BOM · Bill of Materials</h4>" +
  "<p class='work-desc'>Định mức nguyên phụ liệu theo mã hàng. Lưu thay đổi sẽ tạo revision mới và yêu cầu phê duyệt lại.</p>" +
  "<div class='meta-grid'><div class='meta-card'><span>BOM hiện tại</span><strong>R" + h(s.bomRevision) + "</strong></div>" +
  "<div class='meta-card'><span>Phê duyệt</span><strong>" + (approved?"✓":"—") + "</strong><small class='hint'>" + (approved?"Đã duyệt":"Chưa duyệt phiên bản hiện tại") + "</small></div>" +
  "<div class='meta-card'><span>Chi phí NPL tạm tính</span><strong>" + fmt(est) + "</strong><small class='hint'>" + h(s.currency) + " / chiếc</small></div></div>" +
  "<div class='section-card'><form id='bomForm'><div class='table-wrap'><table id='bomTable'><thead><tr><th>Mã NPL</th><th>Nhóm</th><th>ĐVT</th><th>Định mức</th><th>Đơn giá</th><th>Hao hụt %</th><th></th></tr></thead>" +
  "<tbody>" + (d.bom.length ? d.bom.map(bomRow).join("") : bomRow()) + "</tbody></table></div>" +
  "<div class='flex' style='margin-top:14px'><button type='button' class='button ghost' data-action='add-bom-row' " + disabled + ">+ Thêm dòng</button>" +
  "<span class='push hint'>Chi phí giả định cùng loại tiền tệ của mã hàng.</span><button class='button primary' " + disabled + ">Lưu BOM / tạo revision</button></div></form></div>" +
  "<div class='flex'><button class='button secondary' data-action='approve-bom' " +
  ((approved||!active()||!s.bomRevision)?"disabled":"") + ">✓ Phê duyệt BOM hiện tại</button>" +
  "<span class='hint'>Lưu BOM mới sẽ hủy trạng thái xác nhận của checklist sản xuất.</span></div>";
}
function opRow(x={}, sequence=1) {
  return "<tr><td><input type='number' name='sequenceNo' min='1' step='1' required value='" + h(x.sequenceNo ?? sequence) + "'></td>" +
  "<td><input name='name' required value='" + h(x.name || "") + "' placeholder='Join shoulder'></td>" +
  "<td><input name='machineType' value='" + h(x.machineType || "") + "' placeholder='Overlock'></td>" +
  "<td><input name='smvMinutes' required type='number' step='any' min='0.0001' value='" + h(x.smvMinutes ?? "") + "' placeholder='0.65'></td>" +
  "<td><button type='button' class='button tiny danger' data-action='remove-row'>×</button></td></tr>";
}
function ieTab() {
  const d=state.detail,s=d.style,dis=active()?"":"disabled";
  const approved=s.routingRevision>0 && s.routingRevision===s.approvedRoutingRevision;
  const current=d.operations.reduce((a,x)=>a+Number(x.smvMinutes),0);
  const last=d.smvEstimates[0];
  return "<h4 class='work-title'>Industrial Engineering · GST / SMV</h4>" +
  "<p class='work-desc'>AI dự đoán được lưu để tham khảo; chỉ dữ liệu công đoạn được IE kiểm tra và phê duyệt mới là routing chính thức.</p>" +
  "<div class='two-col'><div class='section-card'><h4>AI / Model Estimate</h4>" +
  (last ? "<div class='meta-card'><span>Đề xuất mới nhất (" + h(last.modelVersion) + ")</span>" +
  "<strong>" + fmt(last.predictedSmvMinutes) + " min</strong><small class='hint'>80%: " + fmt(last.smvLower80) + " – " +
  fmt(last.smvUpper80) + " · " + h(last.predictedOperationCount) + " ops</small></div>" :
  "<p>Chưa có dữ liệu model. Có thể nhập kết quả GST AI ở form phía dưới.</p>") +
  "<div class='alert warning'><strong>Model Prediction ≠ IE Approved SMV.</strong> Không dùng trực tiếp dự báo để khóa giá hoặc phát hành sản xuất.</div></div>" +
  "<div class='section-card'><h4>IE Official Routing</h4>" +
  "<div class='meta-card'><span>Tổng SMV công đoạn</span><strong>" + fmt(current) + " min</strong>" +
  "<small class='hint'>Revision R" + h(s.routingRevision) + " · " +
  (approved?"IE approved":"Chưa duyệt") + "</small></div>" +
  "<p>IE chịu trách nhiệm rà soát từng công đoạn, máy sử dụng và thời gian chuẩn.</p></div></div>" +
  "<div class='section-card'><h4>Nhập kết quả AI GST / SMV</h4><form id='estimateForm'>" +
  "<label class='field'>Dán JSON của GST Model (tùy chọn)<textarea id='gstJson' rows='3' placeholder='{\"model_version\":\"0.1.0\", \"prediction\":{\"operation_count\":37,\"smv_minutes\":26.07,\"smv_interval_80\":[16.91,35.22]}}'></textarea></label>" +
  "<div class='flex' style='margin:10px 0'><button type='button' class='button ghost' data-action='parse-estimate' " + dis + ">Đọc JSON vào form</button>" +
  "<span class='hint'>Không tự cập nhật routing IE.</span></div>" +
  "<div class='two-col'><label class='field'>Model Version<input name='modelVersion' required placeholder='0.1.0'></label>" +
  "<label class='field'>Nguồn AI<input name='source' value='GST AI Model'></label>" +
  "<label class='field'>Số công đoạn dự đoán<input type='number' name='predictedOperationCount' min='1' required></label>" +
  "<label class='field'>SMV dự đoán (phút)<input type='number' step='any' name='predictedSmvMinutes' min='0.0001' required></label>" +
  "<label class='field'>Khoảng 80% - Min<input type='number' step='any' name='smvLower80' min='0.0001' required></label>" +
  "<label class='field'>Khoảng 80% - Max<input type='number' step='any' name='smvUpper80' min='0.0001' required></label>" +
  "<label class='field'>View consistency %<input type='number' step='any' min='0' max='100' name='viewConsistencyPercent'></label>" +
  "<label class='field'>Confidence<input name='confidence' placeholder='High / Medium / Low'></label></div>" +
  "<div class='flex' style='margin-top:14px'><button class='button primary' " + dis + ">Lưu AI Estimate</button></div></form></div>" +
  "<div class='section-card'><h4>IE Operation Standards · Thư viện công đoạn</h4>" +
  "<p>Mỗi công đoạn chuẩn là SMV tham chiếu có revision. Import chỉ sao chép dữ liệu vào routing, IE phải duyệt lại.</p>" +
  "<form id='standardForm'><div class='two-col'>" +
  "<label class='field'>Mã công đoạn chuẩn<input name='code' placeholder='GST-101' required></label>" +
  "<label class='field'>Bộ phận / Component<input name='componentName' placeholder='Body / Sleeve' required></label>" +
  "<label class='field'>Tên công đoạn<input name='operationName' placeholder='Join shoulder' required></label>" +
  "<label class='field'>Loại máy<input name='machineType' placeholder='OVERLOCK' required></label>" +
  "<label class='field'>Mã phương pháp<input name='methodCode' value='IE-STD'></label>" +
  "<label class='field'>Reference SMV (min)<input name='referenceSmvMinutes' type='number' min='0.0001' max='1000' step='any' required></label>" +
  "</div><div class='flex' style='margin-top:12px'><button class='button secondary'>+ Thêm công đoạn chuẩn</button></div></form>" +
  "<div class='rule'></div>" +
  (state.standards.length ? "<form id='importStandardsForm'><div class='table-wrap'><table><thead><tr><th>Chọn</th><th>Mã / Revision</th><th>Component</th><th>Operation</th><th>Machine</th><th>SMV</th></tr></thead><tbody>" +
    state.standards.map(x=>"<tr><td><input type='checkbox' name='standardId' value='"+h(x.id)+"' "+(x.isActive?"":"disabled")+"></td>"+
    "<td>"+h(x.code)+" · R"+h(x.revision)+(x.isActive?"":" [Inactive]")+"</td>"+
    "<td>"+h(x.componentName)+"</td><td>"+h(x.operationName)+"</td><td>"+h(x.machineType)+
    "</td><td>"+fmt(x.referenceSmvMinutes)+"</td></tr>").join("")+
    "</tbody></table></div><div class='flex' style='margin-top:12px'>"+
    "<span class='hint'>Chọn các công đoạn theo thứ tự mã để thay thế routing hiện tại.</span>"+
    "<button class='button primary push' "+(active()?"":"disabled")+">Import vào mã hàng</button></div></form>" :
    "<p>Thư viện chưa có công đoạn. Tạo công đoạn chuẩn ở form bên trên.</p>")+
  "</div>" +
  "<div class='section-card'><h4>Quy trình công đoạn chính thức — IE</h4><form id='operationsForm'>" +
  "<div class='table-wrap'><table id='operationTable'><thead><tr><th>Seq</th><th>Tên công đoạn</th><th>Thiết bị</th><th>SMV (min)</th><th></th></tr></thead><tbody>" +
  (d.operations.length?d.operations.map((x,i)=>opRow(x,i+1)).join(""):opRow()) +
  "</tbody></table></div><div class='flex' style='margin-top:14px'><button type='button' class='button ghost' data-action='add-op-row' " + dis + ">+ Thêm công đoạn</button>" +
  "<span class='push hint'>Đây là dữ liệu IE, không phải model.</span><button class='button primary' " + dis + ">Lưu Routing / SMV</button></div></form></div>" +
  "<div class='flex'><button class='button secondary' data-action='approve-operations' " +
  ((approved||!active()||!s.routingRevision)?"disabled":"") + ">✓ IE phê duyệt Routing</button>" +
  "<span class='hint'>Sau khi thay đổi routing phải tính giá và kiểm tra lại readiness.</span></div>" +
  (d.smvEstimates.length>1 ? "<div class='section-card' style='margin-top:16px'><h4>Lịch sử AI Estimates</h4>" +
    "<table><thead><tr><th>Model</th><th>Ops</th><th>SMV</th><th>Ngày tạo</th></tr></thead><tbody>" +
    d.smvEstimates.map(x=>"<tr><td>"+h(x.modelVersion)+"</td><td>"+h(x.predictedOperationCount)+"</td><td>"+fmt(x.predictedSmvMinutes)+"</td><td>"+dateText(x.createdAtUtc)+"</td></tr>").join("")+
    "</tbody></table></div>" : "");
}

function machineRow(machine={}) {
  return "<tr><td><input name='machineType' required placeholder='Overlock' value='" + h(machine.machineType||"") +
    "'></td><td><input name='availableCount' required type='number' min='0' step='1' value='" +
    h(machine.availableCount??0) + "'></td><td><button class='button tiny danger' type='button' data-action='remove-row'>×</button></td></tr>";
}
function lineTab() {
  const d=state.detail, s=d.style, locked=!active(), lines=state.lines;
  const current=lines.find(x=>x.id===state.selectedLineId) || lines[0];
  if(current && !state.selectedLineId)state.selectedLineId=current.id;
  const machines=current?.machines||[];
  const planItems=state.plans;
  const preview=state.linePreview && state.linePreview.lineId===current?.id ? state.linePreview.result : null;
  const expanded=planItems.find(x=>x.plan.id===state.expandedPlan);
  return "<h4 class='work-title'>Production Engineering · Line Capacity Planning</h4>" +
    "<p class='work-desc'>Kế hoạch công đoạn theo SMV chính thức, số người và loại máy của chuyền. Mô hình phân bổ riêng theo công đoạn mang tính bảo thủ, không thay thế tối ưu hóa IE thực địa.</p>" +
    "<div class='two-col'><div class='section-card'><h4>Thiết lập chuyền sản xuất</h4>" +
    "<form id='lineForm'><div class='two-col'>" +
    "<label class='field'>Mã chuyền<input name='code' placeholder='A01' required></label>" +
    "<label class='field'>Nhà máy<input name='factoryCode' placeholder='MY-PHUOC' required></label>" +
    "<label class='field'>Tên chuyền<input name='name' placeholder='Sewing Line A01' required></label>" +
    "<label class='field'>Số người tối đa<input name='maximumOperators' type='number' min='1' max='5000' value='20' required></label>" +
    "<label class='field'>Phút / ca<input name='shiftMinutes' type='number' min='60' max='1440' value='480' required></label>" +
    "<label class='field'>Hiệu suất dự kiến (%)<input name='plannedEfficiencyPercent' type='number' min='0.1' max='100' step='any' value='85' required></label>" +
    "</div><div class='flex' style='margin-top:12px'><button class='button primary' type='submit'>+ Tạo chuyền</button></div></form>" +
    "<p>Chuyền tạo mới phải được khai báo số lượng máy trước khi đánh giá năng lực.</p></div>" +
    "<div class='section-card'><h4>Cấu hình năng lực chuyền</h4>" +
    (lines.length ?
      "<label class='field'>Chọn chuyền<select id='lineSelector'>" +
        lines.map(line=>"<option value='"+h(line.id)+"' "+(line.id===current?.id?"selected":"")+">"+
          h(line.factoryCode)+" / "+h(line.code)+" ("+h(line.maximumOperators)+" người)"+(line.isActive?"":" [Inactive]")+
          "</option>").join("")+"</select></label>" +
      "<div class='meta-grid'><div class='meta-card'><span>Capacity rev</span><strong>R"+h(current.capacityRevision)+"</strong></div>"+
      "<div class='meta-card'><span>Nhân lực</span><strong>"+h(current.maximumOperators)+"</strong></div>"+
      "<div class='meta-card'><span>Efficiency</span><strong>"+fmt(current.plannedEfficiencyPercent,1)+"%</strong></div></div>" +
      "<form id='machineForm'><div class='table-wrap'><table id='machineTable'><thead><tr><th>Loại máy</th><th>Số lượng</th><th></th></tr></thead><tbody>" +
        (machines.length?machines.map(machineRow).join(""):machineRow())+
      "</tbody></table></div><div class='flex' style='margin-top:11px'><button type='button' class='button ghost' data-action='add-machine-row'>+ Máy</button>" +
      "<button class='button secondary push'>Lưu cấu hình máy</button></div></form>" +
      "<p>Thay đổi công suất máy tăng revision, làm kế hoạch duyệt cũ không còn hiệu lực.</p>"
      : "<p>Chưa có chuyền. Tạo chuyền bên trái trước khi lập kế hoạch.</p>") +
    "</div></div>" +
    "<div class='section-card'><h4>Mô phỏng phân bổ nhân sự và máy</h4>" +
    (!current?"<div class='alert warning'>Tạo chuyền trước.</div>":
      (!s.routingRevision||s.routingRevision!==s.approvedRoutingRevision?
        "<div class='alert warning'>Chỉ mô phỏng sau khi IE duyệt routing / SMV chính thức.</div>":"")+
      "<form id='linePlanForm' class='form-row'><label>Chuyền mục tiêu<input value='"+h(current.code)+"' disabled></label>"+
      "<label>Số lượng / ngày<input type='number' name='targetDailyQuantity' min='1' max='200000' value='"+h(state.planTargetQty)+"' required></label>"+
      "<button type='button' class='button secondary' data-action='preview-plan' "+(locked?"disabled":"")+">Mô phỏng</button>"+
      "<button class='button primary' "+(locked?"disabled":"")+">Lưu kế hoạch</button></form>") +
    (preview ? "<div class='meta-grid'><div class='meta-card'><span>SMV tổng (IE)</span><strong>"+
      fmt(preview.calculation.totalSmvMinutes)+"</strong></div><div class='meta-card'><span>Operators required</span><strong>"+
      h(preview.calculation.requiredOperators)+" / "+h(preview.calculation.availableOperators)+
      "</strong></div><div class='meta-card'><span>Daily bottleneck capacity</span><strong>"+
      h(preview.calculation.estimatedDailyCapacity)+"</strong></div></div>"+
      "<div class='alert "+(preview.calculation.canMeetTarget?"success":"danger")+"'>"+
      (preview.calculation.canMeetTarget?"Công suất dự kiến đáp ứng mục tiêu.":"Không đủ nhân lực hoặc máy cho mục tiêu hiện tại.")+
      (preview.calculation.machineShortfalls.length ?
      "<ul>"+preview.calculation.machineShortfalls.map(x=>"<li>"+h(x.machineType)+": "+h(x.required)+
      " cần / "+h(x.available)+" hiện có</li>").join("")+"</ul>":"")+"</div>"+
      "<div class='table-wrap'><table><thead><tr><th>STT</th><th>Công đoạn</th><th>Máy</th><th>SMV</th><th>Người cần</th><th>Output/day</th></tr></thead><tbody>"+
      preview.calculation.assignments.map(x=>"<tr><td>"+h(x.sequenceNo)+"</td><td>"+h(x.operationName)+"</td><td>"+
        h(x.machineType)+"</td><td>"+fmt(x.smvMinutes)+"</td><td>"+h(x.requiredOperators)+
        "</td><td>"+h(x.estimatedDailyCapacity)+"</td></tr>").join("")+
      "</tbody></table></div>" : "")+
    "</div>"+
    "<div class='section-card'><h4>Lịch sử kế hoạch chuyền ("+planItems.length+")</h4>" +
    (!planItems.length?"<p>Chưa lưu kế hoạch chuyền.</p>":
      "<div class='table-wrap'><table><thead><tr><th>Chuyền</th><th>IE Rev / Line Rev</th><th>Target</th><th>Staff</th><th>Trạng thái</th><th>Thao tác</th></tr></thead><tbody>"+
      planItems.map(({plan})=>{
        const line=lines.find(x=>x.id===plan.productionLineId);
        const currentPlan=!!line && line.isActive &&
          line.capacityRevision===plan.lineCapacityRevision &&
          s.routingRevision===plan.routingRevision;
        const feasible=plan.laborFeasible&&plan.machineFeasible;
        return "<tr><td>"+h(line?.code||"Unknown")+"</td><td>R"+h(plan.routingRevision)+
          " / R"+h(plan.lineCapacityRevision)+"</td><td>"+h(plan.targetDailyQuantity)+
          "</td><td>"+h(plan.requiredOperators)+" / "+h(plan.availableOperators)+"</td><td>"+
          pill(plan.status)+(currentPlan?"":" <span class='pill danger'>Stale</span>")+
          (!feasible?" <span class='pill warning'>Insufficient</span>":"")+"</td><td>"+
          "<button class='button tiny ghost' data-action='expand-plan' data-id='"+h(plan.id)+"'>Chi tiết</button> "+
          (plan.status==="Approved"?"":"<button class='button tiny secondary' data-action='approve-plan' data-id='"+h(plan.id)+"' "+
          ((!feasible||!currentPlan||locked)?"disabled":"")+">IE duyệt</button>")+
          "</td></tr>";
      }).join("")+"</tbody></table></div>")+
    (expanded?"<div class='rule'></div><strong>Chi tiết kế hoạch · "+h(expanded.plan.targetDailyQuantity)+" sp/ngày</strong>"+
      "<div class='table-wrap'><table><thead><tr><th>Seq</th><th>Operation</th><th>Máy</th><th>SMV</th><th>Operators</th><th>Output</th></tr></thead><tbody>"+
      expanded.assignments.map(x=>"<tr><td>"+h(x.sequenceNo)+"</td><td>"+h(x.operationName)+"</td><td>"+
        h(x.machineType)+"</td><td>"+fmt(x.smvMinutes)+"</td><td>"+h(x.requiredOperators)+
        "</td><td>"+h(x.estimatedDailyCapacity)+"</td></tr>").join("")+"</tbody></table></div>":"")+
    "</div>";
}


function skillList(raw) {
  const entries=raw.split(",").map(s=>s.trim()).filter(Boolean);
  if(entries.length===0 || entries.length>20) throw new Error("Nhập ít nhất một loại máy:LEVEL.");
  const skills=entries.map(e=>{
    const match=/^([^:,]{1,80}):([1-5])$/.exec(e);
    if(!match) throw new Error("Định dạng tay nghề: OVERLOCK:4,COVERSTITCH:3 (cấp 1–5).");
    return {machineType:match[1].trim(),level:Number(match[2])};
  });
  if(new Set(skills.map(x=>x.machineType.toUpperCase())).size!==skills.length)
    throw new Error("Không nhập trùng loại máy.");
  return skills;
}
function workstationTab() {
  const s=state.detail.style, lines=state.lines;
  const current=lines.find(x=>x.id===state.selectedLineId)||lines[0];
  const activeLine=!!current && current.isActive;
  const preview=state.workstationPreview &&
    state.workstationPreview.lineId===current?.id ? state.workstationPreview.result : null;
  const history=state.workstationPlans.filter(x=>x.plan.productionLineId===current?.id);
  const released=!active(), locked=!activeLine||released;
  const roster=state.workers;
  const target=state.planTargetQty,skill=state.wsSkillLevel;
  return "<h4 class='work-title'>Workstation Balancing · Skill Matrix</h4>" +
    "<p class='work-desc'>Gộp các công đoạn liên tiếp cùng loại máy thành trạm, tính nhân lực theo takt, sau đó phân công mỗi nhân viên vào tối đa một trạm dựa trên tay nghề.</p>" +
    (!lines.length?"<div class='alert warning'>Chưa có chuyền. Tạo chuyền trong tab Line Planning.</div>":
    "<label class='field' style='max-width:400px'>Chọn chuyền<select id='wsLineSelector'>"+
      lines.map(x=>"<option value='"+h(x.id)+"' "+(x.id===current?.id?"selected":"")+">"+
        h(x.factoryCode)+" / "+h(x.code)+" · "+h(x.maximumOperators)+" operators"+(x.isActive?"":" (inactive)")+
        "</option>").join("")+"</select></label>"+
    "<div class='meta-grid'><div class='meta-card'><span>Nhân viên đăng ký</span><strong>"+
      h(roster.length)+"</strong></div><div class='meta-card'><span>Tay nghề đạt</span><strong>"+
      h(roster.filter(x=>x.isActive).length)+"</strong><small class='hint'>Active; skill kiểm tra theo trạm</small></div>"+
    "<div class='meta-card'><span>Line Capacity Rev</span><strong>R"+h(current.capacityRevision)+
      "</strong></div></div>")+
    "<div class='two-col'><div class='section-card'><h4>Operator Skill Matrix</h4>"+
      "<p>Level 1–5 theo loại máy. Level 2 là ngưỡng tối thiểu mặc định trong mô phỏng.</p>"+
      (roster.length?"<div class='table-wrap'><table><thead><tr><th>Nhân viên</th><th>Machine Skills</th><th>Trạng thái</th><th></th></tr></thead><tbody>"+
        roster.map(person=>"<tr><td><strong>"+h(person.employeeCode)+"</strong><div class='data-muted'>"+
        h(person.displayName)+"</div></td><td>"+h(person.skills.map(x=>x.machineType+":"+x.level).join(", "))+
        "</td><td>"+(person.isActive?"<span class='pill good'>Active</span>":"<span class='pill warning'>Inactive</span>")+
        "</td><td><button class='button tiny ghost' data-action='edit-operator' data-id='"+h(person.id)+"' "+
        (released?"disabled":"")+">Sửa</button></td></tr>").join("")+"</tbody></table></div>":
        "<p>Chưa có nhân viên cho chuyền này.</p>")+"</div>"+
    "<div class='section-card'><h4>Thêm nhân viên & tay nghề</h4>"+
      (current?"<form id='operatorForm'><label class='field'>Mã nhân viên<input name='employeeCode' maxlength='40' placeholder='OP-001' required></label>"+
        "<label class='field' style='margin-top:10px'>Tên nhân viên<input name='displayName' placeholder='Operator name' required></label>"+
        "<label class='field' style='margin-top:10px'>Tay nghề (Machine:Level)<input name='skillsText' placeholder='OVERLOCK:4,COVERSTITCH:3' required></label>"+
        "<p>Thêm/sửa hồ sơ sẽ tạo phiên bản năng lực chuyền mới. Kế hoạch cũ phải được mô phỏng và duyệt lại.</p>"+
        "<button class='button primary' "+(released?"disabled":"")+">+ Thêm nhân viên</button></form>":
        "<p>Chưa có chuyền.</p>")+"</div></div>"+
    "<div class='section-card'><h4>Mô phỏng & phân nhóm Workstation</h4>"+
      "<p>Chỉ sử dụng IE routing đã duyệt. Workstation grouping là heuristic dựa trên các công đoạn liên tiếp cùng loại máy, chưa tối ưu toàn cục.</p>"+
      (current?"<form id='workstationForm' class='form-row'><label>Số lượng / ngày<input name='targetDailyQuantity' type='number' min='1' max='200000' value='"+
        h(target)+"' required></label><label>Skill tối thiểu<select name='minimumSkillLevel'>"+
        [1,2,3,4,5].map(x=>"<option value='"+x+"' "+(x===skill?"selected":"")+">Level "+x+"</option>").join("")+
        "</select></label><button type='button' class='button secondary' data-action='preview-workstation' "+
        (locked?"disabled":"")+">Mô phỏng</button><button class='button primary' "+
        (locked?"disabled":"")+">Lưu kế hoạch</button></form>":
        "<p>Tạo chuyền trước khi mô phỏng.</p>")+
      (preview?"<div class='meta-grid'><div class='meta-card'><span>Workstations</span><strong>"+
        h(preview.calculation.stations.length)+"</strong></div><div class='meta-card'><span>Required / Assigned</span><strong>"+
        h(preview.calculation.requiredOperators)+" / "+h(preview.calculation.assignedOperators)+"</strong></div>"+
        "<div class='meta-card'><span>Takt (min)</span><strong>"+fmt(preview.calculation.taktMinutes,3)+"</strong></div></div>"+
        "<div class='alert "+(preview.calculation.canMeetTarget?"success":"danger")+"'>"+
        (preview.calculation.canMeetTarget?"Đạt: số người, máy và tay nghề theo mô hình.":
        "Không đạt: kiểm tra thiếu người, tay nghề hoặc máy.")+
        (preview.calculation.machineShortfalls.length?"<ul>"+preview.calculation.machineShortfalls.map(x=>
          "<li>"+h(x.machineType)+": yêu cầu "+h(x.required)+", hiện có "+h(x.available)+"</li>").join("")+"</ul>":"")+
        "</div><div class='table-wrap'><table><thead><tr><th>Station</th><th>Machine</th><th>Operation sequences</th><th>SMV</th><th>Staff</th><th>Assigned Employees</th></tr></thead><tbody>"+
        preview.calculation.stations.map(x=>"<tr><td>#"+h(x.stationNumber)+"</td><td>"+h(x.machineType)+
          "</td><td>"+h(x.operations.map(y=>y.sequenceNo).join(", "))+"</td><td>"+fmt(x.combinedSmvMinutes)+
          "</td><td>"+h(x.requiredOperators)+"</td><td>"+
          h(x.assignedEmployeeCodes.join(", ")||"Chưa đủ người")+"</td></tr>").join("")+
        "</tbody></table></div>":"")+"</div>"+
    "<div class='section-card'><h4>Saved Workstation Plans ("+h(history.length)+")</h4>"+
      (!history.length?"<p>Chưa có kế hoạch trạm cho chuyền này.</p>":
      "<div class='table-wrap'><table><thead><tr><th>Version</th><th>Target</th><th>Stations</th><th>Staffing</th><th>Status</th><th>Approval</th></tr></thead><tbody>"+
        history.map(({plan,stations})=>{
          const fresh=current && plan.lineCapacityRevision===current.capacityRevision &&
            plan.routingRevision===s.routingRevision;
          const baseline=state.plans.some(({plan:x})=>x.productionLineId===current?.id &&
            x.lineCapacityRevision===current?.capacityRevision &&
            x.routingRevision===s.routingRevision && x.targetDailyQuantity===plan.targetDailyQuantity &&
            x.status==="Approved" && x.laborFeasible && x.machineFeasible);
          const feasible=plan.laborFeasible&&plan.machineFeasible&&plan.skillFeasible&&
            plan.assignedOperators===plan.requiredOperators;
          return "<tr><td>IE R"+h(plan.routingRevision)+" / Line R"+h(plan.lineCapacityRevision)+
            "</td><td>"+h(plan.targetDailyQuantity)+"</td><td>"+h(stations.length)+"</td><td>"+
            h(plan.assignedOperators)+"/"+h(plan.requiredOperators)+"</td><td>"+pill(plan.status)+
            (fresh?"":" <span class='pill danger'>Stale</span>")+
            (feasible?"":" <span class='pill warning'>Shortage</span>")+"</td><td>"+
            (plan.status==="Approved"?"<span class='data-muted'>"+h(plan.approvedBy)+"</span>":
            "<button class='button tiny secondary' data-action='approve-workstation' data-id='"+h(plan.id)+"' "+
            (!fresh||!feasible||!baseline||released?"disabled":"")+">IE duyệt</button>")+"</td></tr>";
        }).join("")+"</tbody></table></div>")+"</div>";
}

function costTab() {
  const d=state.detail,s=d.style,dis=active()?"":"disabled";
  const approvedCosts=d.costs.filter(x=>x.isApproved && x.bomRevision===s.bomRevision && x.routingRevision===s.routingRevision);
  const last=d.costs.at(-1);
  const currency=s.currency;
  return "<h4 class='work-title'>Costing & Quotation</h4>" +
  "<p class='work-desc'>Chi phí dựa trên BOM và routing IE đã được duyệt. Mỗi cost sheet gắn với revision để tránh sử dụng giá lỗi thời.</p>" +
  "<div class='meta-grid'><div class='meta-card'><span>Total Cost / Piece</span><strong>" + (last ? fmt(last.totalCost) : "—") +
  "</strong><small class='hint'>"+h(currency)+"</small></div><div class='meta-card'><span>Suggested Quote</span><strong>" +
  (last?fmt(last.suggestedPrice):"—") + "</strong><small class='hint'>"+h(currency)+"</small></div>" +
  "<div class='meta-card'><span>Accepted Quotations</span><strong>" + h(d.quotes.filter(x=>x.status==="Accepted").length) + "</strong></div></div>" +
  "<div class='section-card'><h4>Tính giá thành</h4><p>Công thức MVP: Material + (IE SMV × Minute Rate) + Overhead. Giá đề xuất = Cost / (1 - target margin).</p>" +
  "<form id='costForm' class='form-row'><label>Minute Rate ("+h(currency)+"/min)<input type='number' name='minuteRate' value='0.08' step='any' min='0' required></label>" +
  "<label>Overhead / piece<input type='number' name='overheadPerPiece' value='0.50' step='any' min='0' required></label>" +
  "<label>Gross Margin %<input type='number' name='targetMarginPercent' value='18' step='any' min='0' max='99.999' required></label>" +
  "<button class='button primary' "+dis+">Tính & lưu Costing</button></form></div>" +
  "<div class='section-card'><h4>Cost Sheets</h4>" +
  (d.costs.length?"<div class='table-wrap'><table><thead><tr><th>Revision BOM/IE</th><th>SMV</th><th>Total Cost</th><th>Suggested Price</th><th>Approval</th></tr></thead><tbody>"+
  [...d.costs].reverse().map(c=>"<tr><td>R"+h(c.bomRevision)+" / R"+h(c.routingRevision)+"</td><td>"+fmt(c.smvMinutes)+"</td>"+
  "<td>"+fmt(c.totalCost)+" "+h(currency)+"</td><td>"+fmt(c.suggestedPrice)+" "+h(currency)+"</td>"+
  "<td>"+(c.isApproved?pill("Approved"):"<button class='button tiny secondary' data-action='approve-cost' data-id='"+h(c.id)+"' "+
  ((!active()||c.bomRevision!==s.bomRevision||c.routingRevision!==s.routingRevision)?"disabled":"")+">Duyệt cost</button>")+"</td></tr>").join("")+
  "</tbody></table></div>":"<p>Chưa có cost sheet.</p>")+"</div>" +
  "<div class='section-card'><h4>Quotation</h4><form id='quoteForm' class='form-row'><label>Approved Cost Sheet<select name='costSheetId' required>" +
  (approvedCosts.length ? approvedCosts.map(c=>"<option value='"+h(c.id)+"'>"+fmt(c.totalCost)+" → "+fmt(c.suggestedPrice)+" "+h(currency)+"</option>").join("") :
  "<option value=''>Chưa có Cost Sheet được duyệt</option>")+"</select></label>" +
  "<label>Unit Price ("+h(currency)+")<input type='number' step='any' min='0.0001' name='unitPrice' required placeholder='Offer price'></label>" +
  "<button class='button primary' "+(!active()||!approvedCosts.length?"disabled":"")+">Tạo báo giá</button></form>" +
  (d.quotes.length?"<div class='table-wrap'><table><thead><tr><th>Giá báo</th><th>Trạng thái</th><th>Ngày</th><th>Thao tác</th></tr></thead><tbody>"+
  [...d.quotes].reverse().map(q=>"<tr><td>"+fmt(q.unitPrice)+" "+h(q.currency)+"</td><td>"+pill(q.status)+"</td><td>"+dateText(q.createdAtUtc)+"</td><td>"+
  (q.status==="Accepted"?"Đã chốt":"<button class='button tiny secondary' data-action='accept-quote' data-id='"+h(q.id)+"' "+dis+">Khách hàng chấp nhận</button>")+
  "</td></tr>").join("")+"</tbody></table></div>":"<p>Chưa có báo giá.</p>")+"</div>";
}
function readinessTab() {
  const d=state.detail, s=d.style, ready=state.readiness.ready, released=s.status==="Released", dis=active()?"":"disabled";
  const validLines=state.lines.filter(line => line.isActive && state.workstationPlans.some(({plan})=>
    plan.productionLineId===line.id && plan.lineCapacityRevision===line.capacityRevision &&
    plan.routingRevision===s.routingRevision && plan.status==="Approved" &&
    plan.laborFeasible && plan.machineFeasible && plan.skillFeasible &&
    plan.assignedOperators===plan.requiredOperators &&
    state.plans.some(({plan:baseline})=>baseline.productionLineId===line.id &&
      baseline.lineCapacityRevision===line.capacityRevision &&
      baseline.routingRevision===s.routingRevision &&
      baseline.targetDailyQuantity===plan.targetDailyQuantity &&
      baseline.status==="Approved" && baseline.laborFeasible && baseline.machineFeasible)));
  const checkMap=new Map(d.checks.map(x=>[x.code,x]));
  return "<h4 class='work-title'>Production Readiness Gate</h4><p class='work-desc'>Kiểm tra 8 điều kiện tiền sản xuất trước khi phát hành mã hàng sang chuyền.</p>" +
  (released ?
    "<div class='alert success'><strong>Đã phát hành!</strong> Chuyền " + h(d.release?.lineCode) +
    " · Planned start: " + h(d.release?.plannedStartDate) + " · Người duyệt: " + h(d.release?.approvedBy) + "</div>" :
    "<div class='alert "+(ready?"success":"warning")+"'><strong>"+(ready?"Đủ điều kiện phát hành":"Chưa đủ điều kiện phát hành")+"</strong>"+
    (ready?" — Đã hoàn thành Production Gate.":" — Hãy hoàn thiện những điều kiện bên dưới.")+"</div>")+
  (!ready && !released?"<div class='section-card'><h4>Release Blockers ("+state.readiness.blockers.length+")</h4><ul class='data-muted'>"+
    state.readiness.blockers.map(x=>"<li>"+h(x)+"</li>").join("")+"</ul></div>":"")+
  "<div class='grid-checks'>"+state.codes.map(code=>{
    const c=checkMap.get(code), done=!!c?.verified, names=checkLabels[code]||[code,""];
    return "<div class='check-card "+(done?"verified":"")+"'><div class='title'><span>"+h(names[0])+"</span>"+
    (done?"<span class='pill good'>✓ Checked</span>":"<span class='pill warning'>Pending</span>")+"</div><p>"+h(names[1])+"</p>"+
    "<div class='row'><input data-check-note='"+h(code)+"' placeholder='Evidence / ghi chú' value='"+h(c?.notes||"")+"' "+dis+">"+
    "<button class='button tiny "+(done?"ghost":"secondary")+"' data-action='verify-check' data-code='"+h(code)+"' "+dis+">"+
    (done?"Bỏ duyệt":"Xác nhận")+"</button></div>"+
    (done?"<div class='hint' style='margin-top:7px'>Xác nhận bởi "+h(c.verifiedBy)+" · "+dateText(c.verifiedAtUtc)+"</div>":"")+
    "</div>";
  }).join("")+"</div>"+
  "<div class='section-card' style='margin-top:17px'><h4>Production Release</h4>"+
  "<p>Phát hành chỉ khi tất cả dữ liệu được phê duyệt. Đây là bước bàn giao sang PPC/MES, chưa phải tự động tạo lệnh sản xuất.</p>"+
  "<form id='releaseForm' class='form-row'><label>Chuyền may<select name='lineCode' required>"+
  (validLines.length ? validLines.map(line=>"<option value='"+h(line.code)+"'>"+h(line.code)+" · "+h(line.factoryCode)+"</option>").join("") :
  "<option value=''>Chưa có line balance plan được duyệt</option>")+
  "</select></label>"+
  "<label>Ngày dự kiến lên chuyền<input type='date' name='plannedStartDate' required></label>"+
  "<button class='button primary' "+(!ready||released||!validLines.length?"disabled":"")+">✓ Release to Production</button></form>"+
  "<div class='hint' style='margin-top:12px'>MVP: checklist dùng xác nhận bằng tên nhập tay, chưa có workflow role-based hay tài liệu chứng cứ đính kèm.</div></div>";
}
function rowData(tr, keys, numerics=[]) {
  const result={};
  keys.forEach(key=>{
    const field=tr.querySelector("[name='"+key+"']");
    if (!field) throw new Error("Thiếu trường "+key);
    result[key]=numerics.includes(key)?Number(field.value):field.value.trim();
  });
  return result;
}
async function mutation(path, method="POST", payload=null) {
  const options={ method };
  if(payload!==null) options.body=JSON.stringify(payload);
  const result=await api(path,options);
  await reload();
  return result;
}
async function handleAction(el) {
  const action=el.dataset.action,id=state.selectedId,d=state.detail;
  if(action==="jump") {
    if(el.dataset.tab && state.detail){state.tab=el.dataset.tab;renderDetail();}
    $(el.dataset.target).scrollIntoView({behavior:"smooth"});return;
  }
  if(action==="new-style"){ $("styleDialog").showModal();return; }
  if(action==="close-dialog"){ $("styleDialog").close();return; }
  if(action==="api-key") {
    const key=window.prompt("Nhập API key cho X-Api-Key (để trống để xóa):",sessionStorage.getItem("gsp_api_key")||"");
    if(key!==null){if(key.trim())sessionStorage.setItem("gsp_api_key",key.trim());else sessionStorage.removeItem("gsp_api_key");await reload();}
    return;
  }
  if(action==="select-style"){state.tab="overview";await openStyle(el.dataset.id);return;}
  if(action==="tab"){if(!d)return;state.tab=el.dataset.tab;renderDetail();return;}
  if(!d) throw new Error("Chọn mã hàng trước.");
  if(action==="preview-workstation"){
    const form=$("workstationForm");
    const selected=state.lines.find(x=>x.id===state.selectedLineId)||state.lines[0];
    if(!selected)throw new Error("Chưa có chuyền may.");
    const targetDailyQuantity=Number(form.elements.namedItem("targetDailyQuantity").value);
    const minimumSkillLevel=Number(form.elements.namedItem("minimumSkillLevel").value);
    const result=await api("/api/styles/"+id+"/workstation-plans/preview",{
      method:"POST",body:JSON.stringify({
        productionLineId:selected.id,targetDailyQuantity,minimumSkillLevel
      })
    });
    state.planTargetQty=targetDailyQuantity;state.wsSkillLevel=minimumSkillLevel;
    state.workstationPreview={lineId:selected.id,result};
    renderDetail();notify("Đã mô phỏng workstation và skill matching.");return;
  }
  if(action==="approve-workstation"){
    await mutation("/api/workstation-plans/"+el.dataset.id+"/approve","POST",{
      approvedBy:actor()
    });
    notify("Đã IE phê duyệt Workstation Plan.");return;
  }
  if(action==="edit-operator"){
    const person=state.workers.find(x=>x.id===el.dataset.id);
    if(!person)throw new Error("Không tìm thấy hồ sơ.");
    const existing=person.skills.map(x=>x.machineType+":"+x.level).join(",");
    const edited=window.prompt("Cập nhật tay nghề MACHINE:LEVEL, ngăn cách dấu phẩy:",existing);
    if(edited===null)return;
    const skills=skillList(edited);
    const isActive=window.confirm("Nhấn OK nếu công nhân đang active; Cancel để đặt inactive.");
    await mutation("/api/operators/"+person.id,"PUT",{
      displayName:person.displayName,isActive,skills
    });
    state.workstationPreview=null;
    notify("Đã cập nhật kỹ năng; các plan trước đó phải được duyệt lại.");return;
  }
  if(action==="add-machine-row"){
    document.querySelector("#machineTable tbody").insertAdjacentHTML("beforeend",machineRow());return;
  }
  if(action==="preview-plan"){
    const form=$("linePlanForm");
    const target=Number(form.elements.namedItem("targetDailyQuantity").value);
    const line=state.lines.find(x=>x.id===state.selectedLineId) || state.lines[0];
    if(!line)throw new Error("Chưa cấu hình chuyền may.");
    const result=await api("/api/styles/"+id+"/line-plans/preview",{
      method:"POST",body:JSON.stringify({productionLineId:line.id,targetDailyQuantity:target})
    });
    state.planTargetQty=target;
    state.linePreview={lineId:line.id,result};
    renderDetail();notify("Đã tính xong đề xuất phân bổ IE.");return;
  }
  if(action==="approve-plan"){
    await mutation("/api/line-plans/"+el.dataset.id+"/approve","POST",{approvedBy:actor()});
    notify("Đã phê duyệt kế hoạch chuyền.");return;
  }
  if(action==="expand-plan"){
    state.expandedPlan=state.expandedPlan===el.dataset.id?null:el.dataset.id;
    renderDetail();return;
  }
  if(action==="add-bom-row") {document.querySelector("#bomTable tbody").insertAdjacentHTML("beforeend",bomRow());return;}
  if(action==="add-op-row"){
    const body=document.querySelector("#operationTable tbody");
    body.insertAdjacentHTML("beforeend",opRow({},body.rows.length+1));return;
  }
  if(action==="remove-row"){el.closest("tr").remove();return;}
  if(action==="approve-sample"){await mutation("/api/samples/"+el.dataset.id+"/approve","POST",{approvedBy:actor()});notify("Đã phê duyệt mẫu.");return;}
  if(action==="approve-bom"){await mutation("/api/styles/"+id+"/bom/approve","POST",{approvedBy:actor()});notify("BOM revision đã được phê duyệt.");return;}
  if(action==="approve-operations"){await mutation("/api/styles/"+id+"/operations/approve","POST",{approvedBy:actor()});notify("IE routing/SMV chính thức đã được duyệt.");return;}
  if(action==="approve-cost"){await mutation("/api/costs/"+el.dataset.id+"/approve","POST",{approvedBy:actor()});notify("Đã phê duyệt giá thành.");return;}
  if(action==="accept-quote"){
    if(!window.confirm("Xác nhận khách hàng đã chấp nhận báo giá này?"))return;
    await mutation("/api/quotes/"+el.dataset.id+"/accept");notify("Đã cập nhật báo giá được khách hàng chấp nhận.");return;
  }
  if(action==="parse-estimate"){
    let json; try{json=JSON.parse($("gstJson").value);}catch{throw new Error("JSON không hợp lệ.");}
    const p=json.prediction || json, form=$("estimateForm");
    const interval=p.smv_interval_80 || p.smvInterval80 || [];
    const mapping={
      modelVersion:json.model_version||p.model_version||"",
      predictedOperationCount:p.operation_count??p.predictedOperationCount,
      predictedSmvMinutes:p.smv_minutes??p.predictedSmvMinutes,
      smvLower80:interval[0]??p.smvLower80,
      smvUpper80:interval[1]??p.smvUpper80,
      viewConsistencyPercent:p.view_consistency_percent??p.viewConsistencyPercent??"",
      confidence:p.confidence||""
    };
    Object.entries(mapping).forEach(([key,value])=>{
      if(value!==undefined&&value!==null) form.elements.namedItem(key).value=String(value);
    });
    notify("Đã đọc kết quả AI. Chỉ là estimate, chưa phải IE approval.");return;
  }
  if(action==="verify-check"){
    const code=el.dataset.code;
    const check=d.checks.find(x=>x.code===code);
    const verified=!check?.verified;
    const notes=document.querySelector("[data-check-note='"+code+"']").value.trim();
    await mutation("/api/styles/"+id+"/readiness/"+code,"PUT",{
      verified,verifiedBy:verified?actor():"",notes
    });
    notify(verified?"Đã xác nhận điều kiện "+code+".":"Đã bỏ xác nhận "+code+".");return;
  }
}
async function handleSubmit(form) {
  const name=form.id,id=state.selectedId;
  const fields=Object.fromEntries(new FormData(form));
  if(name==="createStyleForm"){
    const style=await api("/api/styles",{method:"POST",body:JSON.stringify(fields)});
    $("styleDialog").close();state.selectedId=style.id;state.tab="overview";
    await reload();notify("Đã tạo mã hàng "+style.code+".");return;
  }
  if(!id)throw new Error("Chọn mã hàng trước khi thao tác.");
  if(name==="operatorForm"){
    const selected=state.lines.find(x=>x.id===state.selectedLineId)||state.lines[0];
    if(!selected)throw new Error("Chưa có chuyền.");
    await mutation("/api/lines/"+selected.id+"/operators","POST",{
      employeeCode:fields.employeeCode,displayName:fields.displayName,
      skills:skillList(fields.skillsText)
    });
    state.workstationPreview=null;
    notify("Đã thêm công nhân; cần tạo lại line/workstation plans.");return;
  }
  if(name==="workstationForm"){
    const selected=state.lines.find(x=>x.id===state.selectedLineId)||state.lines[0];
    if(!selected)throw new Error("Chưa có chuyền.");
    const targetDailyQuantity=Number(fields.targetDailyQuantity);
    const minimumSkillLevel=Number(fields.minimumSkillLevel);
    state.planTargetQty=targetDailyQuantity;state.wsSkillLevel=minimumSkillLevel;
    await mutation("/api/styles/"+id+"/workstation-plans","POST",{
      productionLineId:selected.id,targetDailyQuantity,minimumSkillLevel
    });
    state.workstationPreview=null;
    notify("Workstation Plan đã lưu, cần IE phê duyệt sau khi có line baseline tương ứng.");return;
  }
  if(name==="lineForm"){
    const line=await api("/api/lines",{method:"POST",body:JSON.stringify({
      code:fields.code,name:fields.name,factoryCode:fields.factoryCode,
      maximumOperators:Number(fields.maximumOperators),
      shiftMinutes:Number(fields.shiftMinutes),
      plannedEfficiencyPercent:Number(fields.plannedEfficiencyPercent)
    })});
    state.selectedLineId=line.id;state.linePreview=null;
    await reload();notify("Đã tạo chuyền "+line.code+". Khai báo máy trước khi mô phỏng.");return;
  }
  if(name==="machineForm"){
    const line=state.lines.find(x=>x.id===state.selectedLineId) || state.lines[0];
    if(!line)throw new Error("Chưa có chuyền.");
    const rows=[...form.querySelectorAll("tbody tr")].map(tr=>
      rowData(tr,["machineType","availableCount"],["availableCount"]));
    if(!window.confirm("Đổi cấu hình máy làm các line balance plan của chuyền hết hiệu lực. Tiếp tục?"))return;
    await mutation("/api/lines/"+line.id+"/machines","PUT",rows);
    state.linePreview=null;notify("Đã lưu cấu hình máy, cần duyệt lại kế hoạch chuyền.");return;
  }
  if(name==="linePlanForm"){
    const line=state.lines.find(x=>x.id===state.selectedLineId) || state.lines[0];
    if(!line)throw new Error("Chưa có chuyền.");
    const targetDailyQuantity=Number(fields.targetDailyQuantity);
    state.planTargetQty=targetDailyQuantity;
    await mutation("/api/styles/"+id+"/line-plans","POST",{
      productionLineId:line.id,targetDailyQuantity
    });
    state.linePreview=null;renderDetail();
    notify("Đã lưu kế hoạch chuyền mới; cần IE phê duyệt nếu đạt yêu cầu.");return;
  }
  if(name==="standardForm"){
    const input={
      code:fields.code, componentName:fields.componentName, operationName:fields.operationName,
      machineType:fields.machineType, methodCode:fields.methodCode,
      referenceSmvMinutes:Number(fields.referenceSmvMinutes)
    };
    await api("/api/operation-standards",{method:"POST",body:JSON.stringify(input)});
    await reload();
    notify("Đã thêm IE Operation Standard. Chưa phải IE approval của mã hàng.");return;
  }
  if(name==="importStandardsForm"){
    const chosen=[...form.querySelectorAll("input[name=standardId]:checked")].map(x=>x.value);
    if(!chosen.length)throw new Error("Chọn ít nhất một công đoạn trong thư viện.");
    if(!window.confirm("Import sẽ thay thế routing hiện tại, tạo revision mới và hủy trạng thái readiness trước đó. Tiếp tục?"))return;
    await mutation("/api/styles/"+id+"/operations/from-library","POST",
      chosen.map((operationStandardId,i)=>({sequenceNo:i+1,operationStandardId})));
    notify("Đã import thư viện vào routing. IE phải kiểm tra & phê duyệt lại.");return;
  }
  if(name==="sampleForm"){
    await mutation("/api/styles/"+id+"/samples","POST",fields);notify("Đã thêm mẫu.");return;
  }
  if(name==="bomForm"){
    const rows=[...form.querySelectorAll("tbody tr")].map(tr=>
      rowData(tr,["materialCode","category","unit","consumption","unitPrice","wastagePercent"],
        ["consumption","unitPrice","wastagePercent"]));
    if(!window.confirm("Lưu BOM mới sẽ hủy phê duyệt revision trước và đặt lại checklist. Tiếp tục?"))return;
    await mutation("/api/styles/"+id+"/bom","PUT",rows);notify("Đã lưu BOM. Cần duyệt lại revision và readiness.");return;
  }
  if(name==="operationsForm"){
    const rows=[...form.querySelectorAll("tbody tr")].map(tr=>
      rowData(tr,["sequenceNo","name","machineType","smvMinutes"],["sequenceNo","smvMinutes"]));
    if(!window.confirm("Lưu routing sẽ tạo revision mới và yêu cầu IE phê duyệt lại. Tiếp tục?"))return;
    await mutation("/api/styles/"+id+"/operations","PUT",rows);notify("Đã lưu IE Routing. Cần IE phê duyệt lại.");return;
  }
  if(name==="estimateForm"){
    const input={
      modelVersion:fields.modelVersion,source:fields.source,
      predictedOperationCount:Number(fields.predictedOperationCount),
      predictedSmvMinutes:Number(fields.predictedSmvMinutes),
      smvLower80:Number(fields.smvLower80),smvUpper80:Number(fields.smvUpper80),
      viewConsistencyPercent:fields.viewConsistencyPercent===""?null:Number(fields.viewConsistencyPercent),
      confidence:fields.confidence
    };
    await mutation("/api/styles/"+id+"/smv-estimates","POST",input);
    notify("Đã lưu AI SMV Estimate — không thay thế routing IE.");return;
  }
  if(name==="costForm"){
    await mutation("/api/styles/"+id+"/costs","POST",{
      minuteRate:Number(fields.minuteRate),overheadPerPiece:Number(fields.overheadPerPiece),
      targetMarginPercent:Number(fields.targetMarginPercent)
    });notify("Cost sheet đã được tính và lưu, chờ phê duyệt.");return;
  }
  if(name==="quoteForm"){
    await mutation("/api/styles/"+id+"/quotes","POST",{
      costSheetId:fields.costSheetId,unitPrice:Number(fields.unitPrice)
    });notify("Đã tạo báo giá.");return;
  }
  if(name==="releaseForm"){
    if(!window.confirm("Phát hành mã hàng này cho chuyền "+fields.lineCode+"? Không thể sửa sau khi phát hành."))return;
    await mutation("/api/styles/"+id+"/release","POST",{
      lineCode:fields.lineCode,plannedStartDate:fields.plannedStartDate,approvedBy:actor()
    });notify("Production Release thành công.");return;
  }
}
document.addEventListener("click", async(event)=>{
  const el=event.target.closest("[data-action]");if(!el)return;
  try{await handleAction(el);}catch(error){notify(error.message,true);console.error(error);}
});
document.addEventListener("submit",async(event)=>{
  const form=event.target;if(!(form instanceof HTMLFormElement))return;
  event.preventDefault();
  const submit=form.querySelector("button[type=submit],button:not([type])");
  if(submit)submit.disabled=true;
  try{await handleSubmit(form);}catch(error){notify(error.message,true);console.error(error);}
  finally{if(submit && submit.isConnected)submit.disabled=false;}
});
$("styleSearch").addEventListener("input",event=>{state.query=event.target.value;renderStyles();});
document.addEventListener("change",event=>{
  if(event.target?.id==="lineSelector" || event.target?.id==="wsLineSelector"){
    state.selectedLineId=event.target.value;
    state.linePreview=null;state.workstationPreview=null;
    const lineId=state.selectedLineId;
    api("/api/lines/"+lineId+"/operators").then(people=>{
      if(state.selectedLineId===lineId){state.workers=people;renderDetail();}
    }).catch(error=>notify(error.message,true));
  }
});
$("actor").value=sessionStorage.getItem("gsp_actor") || "";
$("actor").addEventListener("input",event=>sessionStorage.setItem("gsp_actor",event.target.value));
reload().catch(error=>notify(error.message,true));
