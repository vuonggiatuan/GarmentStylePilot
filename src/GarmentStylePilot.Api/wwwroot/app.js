// GarmentStylePilot operational MVP (browser-only UI; permissions are enforced server-side).
const state = { styles: [], stats: null, detail: null, readiness: null, codes: [], selectedId: null, tab: "overview", query: "" };
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
  const [stats, styles, codes] = await Promise.all([
    api("/api/dashboard"), api("/api/styles"), api("/api/readiness-codes")
  ]);
  state.stats = stats; state.styles = styles; state.codes = codes;
  renderSummary(); renderStyles();
  if (state.selectedId) await openStyle(state.selectedId, false);
  $("lastUpdated").textContent = "Đồng bộ lúc " + new Date().toLocaleTimeString("vi-VN");
}
async function openStyle(id, updateList = true) {
  state.selectedId = id;
  try {
    const [detail, readiness] = await Promise.all([
      api("/api/styles/" + id), api("/api/styles/" + id + "/readiness")
    ]);
    state.detail = detail; state.readiness = readiness;
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
    ["ie", "IE · GST/SMV"], ["cost", "Costing & Quote"], ["readiness", "Line Readiness"]
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
    case "cost": return costTab();
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
  const steps = [
    ["01", "Sample", d.samples.some(x=>x.status==="Approved"), "Fit / PP Sample approval"],
    ["02", "BOM", s.bomRevision>0 && s.bomRevision===s.approvedBomRevision, "Material consumption revision " + s.bomRevision],
    ["03", "IE Routing", s.routingRevision>0 && s.routingRevision===s.approvedRoutingRevision, "Official SMV " + fmt(official) + " min"],
    ["04", "Costing", !!currentCost, "Approved cost sheet"],
    ["05", "Quotation", !!accepted, "Customer-accepted quote"],
    ["06", "Line Readiness", state.readiness.ready || s.status==="Released", "8 production checkpoints"]
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
  const checkMap=new Map(d.checks.map(x=>[x.code,x]));
  return "<h4 class='work-title'>Production Readiness Gate</h4><p class='work-desc'>Kiểm tra 8 điều kiện tiền sản xuất trước khi phát hành mã hàng sang chuyền.</p>" +
  (released ?
    "<div class='alert success'><strong>Đã phát hành!</strong> Chuyền " + h(d.release?.lineCode) +
    " · Planned start: " + h(d.release?.plannedStartDate) + " · Người duyệt: " + h(d.release?.approvedBy) + "</div>" :
    "<div class='alert "+(ready?"success":"warning")+"'><strong>"+(ready?"Đủ điều kiện phát hành":"Chưa đủ điều kiện phát hành")+"</strong>"+
    (ready?" — Đã hoàn thành Production Gate.":" — Hãy hoàn thiện những điều kiện bên dưới.")+"</div>")+
  (!ready?"<div class='section-card'><h4>Release Blockers ("+state.readiness.blockers.length+")</h4><ul class='data-muted'>"+
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
  "<form id='releaseForm' class='form-row'><label>Chuyền may<input name='lineCode' placeholder='A01' required></label>"+
  "<label>Ngày dự kiến lên chuyền<input type='date' name='plannedStartDate' required></label>"+
  "<button class='button primary' "+(!ready||released?"disabled":"")+">✓ Release to Production</button></form>"+
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
$("actor").value=sessionStorage.getItem("gsp_actor") || "";
$("actor").addEventListener("input",event=>sessionStorage.setItem("gsp_actor",event.target.value));
reload().catch(error=>notify(error.message,true));
