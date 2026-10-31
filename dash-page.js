
"use strict";
const ENDPOINT = "/api/analytics/poc-ts";
const KEY_STORE = "logkit.key";
const ALL = "__total__";
let DATA = null;

const $ = (id) => document.getElementById(id);
const num = (n, d = 1) => n == null || !isFinite(n) ? "–" : Number(n).toLocaleString(undefined, { maximumFractionDigits: d });
const pct = (r, d = 1) => r == null || !isFinite(r) ? "–" : (r * 100).toFixed(d) + "%";
function esc(s) {
  return String(s ?? "").replace(/[&<>"']/g, (c) =>
    ({ "&": "&amp;", "<": "&lt;", ">": "&gt;", '"': "&quot;", "'": "&#39;" }[c]));
}
function dur(sec) {
  if (sec == null || !isFinite(sec)) return "–";
  if (sec < 60) return sec.toFixed(1) + "초";
  if (sec < 3600) return Math.floor(sec / 60) + "분 " + Math.round(sec % 60) + "초";
  return Math.floor(sec / 3600) + "시간 " + Math.round(sec % 3600 / 60) + "분";
}
function getKey() { try { return localStorage.getItem(KEY_STORE) || ""; } catch (_) { return ""; } }
function setKey(k) { try { localStorage.setItem(KEY_STORE, k); } catch (_) {} }
function setStatus(text, isErr) { const el = $("status"); el.textContent = text; el.className = isErr ? "status err" : "status"; }
function emptyBox(text) { return '<div class="empty">' + esc(text) + "</div>"; }

async function api(path) {
  const res = await fetch(path, { headers: { "X-Api-Key": getKey() } });
  if (res.status === 401) throw new Error("인증 실패 — API 키를 확인하세요");
  if (res.status === 403) throw new Error("이 키로는 열람할 수 없습니다");
  if (!res.ok) throw new Error("HTTP " + res.status);
  return res.json();
}

// 가로 막대 목록. rows: { label, value(0~1 또는 값), text, cls }, max: 막대 기준 값
function bars(rows, max) {
  if (rows.length === 0) return emptyBox("기록된 데이터가 없습니다.");
  return rows.map((r) => {
    const w = max > 0 && r.value != null ? Math.max(0, Math.min(100, r.value / max * 100)) : 0;
    return '<div class="hb"><span class="hl" title="' + esc(r.label) + '">' + esc(r.label) + '</span>'
      + '<span class="hbar"><i class="' + (r.cls || "") + '" style="width:' + w.toFixed(1) + '%"></i></span>'
      + '<span class="hv">' + esc(r.text) + "</span></div>";
  }).join("");
}

// 표 한 개. columns: 헤더 이름 배열, rows: 문자열 셀 배열의 배열
function table(columns, rows) {
  const head = "<tr>" + columns.map((c) => "<th>" + esc(c) + "</th>").join("") + "</tr>";
  const body = rows.map((row) => "<tr>" + row.map((c) => "<td>" + esc(c) + "</td>").join("") + "</tr>").join("");
  return "<table><thead>" + head + "</thead><tbody>" + body + "</tbody></table>";
}

function viewOf(key) {
  if (key === ALL) return DATA.total;
  return DATA.versions.find((v) => v.buildVersion === key) || null;
}

function renderKpi() {
  const columns = DATA.versions.concat(DATA.total ? [DATA.total] : []);
  const header = ["지표"].concat(columns.map((v) => v.buildVersion === "전체" ? "전체" : "v" + v.buildVersion));
  const groups = [
    ["판"],
    ["판 수", (v) => num(v.sessions, 0)],
    ["평균 플레이 시간", (v) => dur(v.avgPlaySeconds)],
    ["판당 처치", (v) => num(v.avgKillsPerSession)],
    ["사망 종료 비율", (v) => pct(v.deathRate)],
    ["평균 도달 웨이브", (v) => num(v.avgReachedWave)],
    ["전투"],
    ["발사 수", (v) => num(v.shots, 0)],
    ["발사 명중률", (v) => pct(v.shotHitRate)],
    ["받은 피해 합계", (v) => num(v.totalDamageTaken, 0)],
    ["슬롯"],
    ["판당 회전", (v) => num(v.slot.spinsPerSession)],
    ["회전 적중률", (v) => pct(v.slot.hitRate)],
    ["니어미스 비율", (v) => pct(v.slot.nearMissRate)],
    ["회전당 금전 변화 평균", (v) => num(v.slot.avgMoneyDelta)],
    ["상점"],
    ["판당 구매 시도", (v) => num(v.shop.attemptsPerSession)],
    ["구매 성공률", (v) => pct(v.shop.successRate)],
    ["총 지출", (v) => num(v.shop.totalSpent, 0)],
  ];
  const rows = [];
  for (const [label, pick] of groups) {
    if (!pick) {
      rows.push('<tr class="group"><td colspan="' + header.length + '">' + esc(label) + "</td></tr>");
      continue;
    }
    rows.push("<tr><td>" + esc(label) + "</td>" + columns.map((v) => "<td>" + esc(pick(v)) + "</td>").join("") + "</tr>");
  }
  const head = "<tr>" + header.map((c) => "<th>" + esc(c) + "</th>").join("") + "</tr>";
  $("kpi").innerHTML = '<table class="kpitable"><thead>' + head + "</thead><tbody>" + rows.join("") + "</tbody></table>";
}

function renderWaves(v) {
  if (!v.waves.length) {
    $("c-waves").innerHTML = emptyBox("웨이브 기록이 없습니다.");
    $("t-waves").innerHTML = emptyBox("웨이브 기록이 없습니다.");
    return;
  }
  $("c-waves").innerHTML = bars(v.waves.map((w) => ({
    label: "웨이브 " + w.wave,
    value: w.completionRate,
    text: w.completed + "/" + w.entered + " (" + pct(w.completionRate, 0) + ")",
    cls: w.completionRate != null && w.completionRate < 0.5 ? "warn" : "",
  })), 1);
  $("t-waves").innerHTML = table(
    ["웨이브", "진입", "완료", "사망 이탈", "완료율", "평균 처치", "평균 클리어(초)"],
    v.waves.map((w) => ["웨이브 " + w.wave, num(w.entered, 0), num(w.completed, 0), num(w.diedHere, 0),
      pct(w.completionRate), num(w.avgKills), num(w.avgClearSeconds)]));
}

function renderWeapons(v) {
  $("t-weapons").innerHTML = v.weapons.length
    ? table(["무기", "발사", "명중", "명중률", "처치", "헤드샷", "피해"],
      v.weapons.map((w) => [w.weapon, num(w.shots, 0), num(w.hits, 0), pct(w.hitRate), num(w.kills, 0), num(w.headshots, 0), num(w.damage, 0)]))
    : emptyBox("발사 기록이 없습니다.");
}

function renderDamage(v) {
  const max = v.damageTaken.length ? Math.max(...v.damageTaken.map((d) => d.damage)) : 0;
  $("c-damage").innerHTML = bars(v.damageTaken.map((d) => ({
    label: d.enemyType,
    value: d.damage,
    text: num(d.damage, 0) + " · " + num(d.hits, 0) + "회",
  })), max);
}

function renderSlot(v) {
  const s = v.slot;
  $("t-slot").innerHTML = s.spins
    ? table(["항목", "값"], [
      ["회전 수", num(s.spins, 0)],
      ["판당 회전", num(s.spinsPerSession)],
      ["적중률", pct(s.hitRate)],
      ["니어미스 비율", pct(s.nearMissRate)],
      ["회전당 금전 변화 평균", num(s.avgMoneyDelta)],
      ["금전 변화 합계", num(s.totalMoneyDelta, 0)],
    ])
    : emptyBox("슬롯 회전 기록이 없습니다.");
}

function renderShop(v) {
  const s = v.shop;
  if (!s.attempts) {
    $("t-shop").innerHTML = emptyBox("상점 구매 기록이 없습니다.");
    return;
  }
  const failures = Object.entries(s.failureCodes).map(([code, count]) => code + " " + count + "건").join(", ");
  $("t-shop").innerHTML = table(["품목", "시도", "성공", "지출"],
    s.byItemType.map((r) => [r.itemType, num(r.attempts, 0), num(r.successes, 0), num(r.spent, 0)]))
    + '<p class="sub" style="margin-top:8px">성공률 ' + pct(s.successRate) + " · 총 지출 " + num(s.totalSpent, 0)
    + (failures ? " · 실패 사유: " + esc(failures) : "") + "</p>";
}

function renderSessions(v) {
  const list = DATA.sessions.filter((s) => v.buildVersion === "전체" || s.buildVersion === v.buildVersion);
  const shown = list.slice(0, 200);
  $("sessionSub").textContent = "표시 " + shown.length + "판 / 해당 " + list.length + "판 (전체 " + DATA.sessionCount + "판)";
  $("t-sessions").innerHTML = shown.length
    ? table(["시작(UTC)", "버전", "맵", "플레이", "종료", "도달 웨이브", "처치", "회전", "구매", "발사", "ID"],
      shown.map((s) => [
        s.startUtc ? s.startUtc.replace("T", " ").slice(0, 19) : "–",
        s.buildVersion, s.mapId, dur(s.playSeconds), s.endReason,
        s.finalReachedWave == null ? "–" : num(s.finalReachedWave, 0),
        num(s.totalKills, 0), num(s.spins, 0), num(s.purchases, 0), num(s.shots, 0),
        s.sessionId.slice(0, 8)]))
    : emptyBox("표시할 판이 없습니다.");
}

function renderVersionSelect() {
  const select = $("ver");
  const previous = select.value;
  const options = [{ value: ALL, text: "전체" }].concat(DATA.versions.map((v) => ({ value: v.buildVersion, text: "v" + v.buildVersion + " (" + v.sessions + "판)" })));
  select.innerHTML = options.map((o) => '<option value="' + esc(o.value) + '">' + esc(o.text) + "</option>").join("");
  const keep = options.some((o) => o.value === previous) ? previous : (DATA.versions[0] ? DATA.versions[0].buildVersion : ALL);
  select.value = keep;
}

// 이동 히트맵: 버전 선택과 맵 선택으로 서버에서 칸별 횟수를 받아 캔버스에 그린다.
const HEATMAP_GRID = 64;
async function loadHeatmap() {
  if (!DATA || !DATA.configured) return;
  const version = $("ver").value === ALL ? "" : $("ver").value;
  const p = new URLSearchParams({ grid: String(HEATMAP_GRID) });
  if (version) p.set("version", version);
  if ($("hmap").value) p.set("map", $("hmap").value);
  const data = await api(ENDPOINT + "/heatmap?" + p);
  const sel = $("hmap");
  const previous = sel.value;
  sel.innerHTML = '<option value="">전체 맵 (좌표계가 섞임)</option>' + data.maps.map((m) => '<option value="' + esc(m) + '">' + esc(m) + "</option>").join("");
  sel.value = data.maps.includes(previous) ? previous : "";
  drawHeatmap(data);
}

function drawHeatmap(d) {
  const c = $("hm-canvas");
  const g = c.getContext("2d");
  const W = c.width, H = c.height;
  g.clearRect(0, 0, W, H);
  if (!d.pointCount) {
    $("hm-info").textContent = "표시할 위치 데이터가 없습니다. 선택한 버전·맵의 판에 위치 기록이 있는지 확인하세요.";
    return;
  }
  const mixed = !$("hmap").value && d.maps.length > 1 ? " · 맵이 여러 개라 좌표가 섞였습니다" : "";
  $("hm-info").textContent = "판 " + num(d.sessionCount, 0) + "개 · 위치 " + num(d.pointCount, 0) + "개 · 최대 " + num(d.maxCount, 0) + "회/칸" + mixed;
  const n = d.grid, cell = W / n, max = d.maxCount;
  for (let row = 0; row < n; row++) {
    for (let col = 0; col < n; col++) {
      const k = d.counts[row * n + col];
      if (!k) continue;
      const t = Math.sqrt(k / max);
      g.fillStyle = "rgba(255," + Math.round(200 * (1 - t)) + "," + Math.round(60 * (1 - t)) + "," + (0.15 + 0.85 * t).toFixed(3) + ")";
      g.fillRect(col * cell, (n - 1 - row) * cell, cell, cell);
    }
  }
}

function render() {
  if (!DATA.configured) {
    $("notice").hidden = false;
    $("notice").innerHTML = '<h3>분석 폴더가 설정되지 않았거나 없습니다</h3><p class="sub">appsettings.json의 TelemetryPath를 확인하세요. 현재 값: '
      + esc(DATA.telemetryPath || "(비어 있음)") + "</p>";
    return;
  }
  $("notice").hidden = true;
  renderVersionSelect();
  const v = viewOf($("ver").value);
  if (!v) return;
  setStatus("판 " + DATA.sessionCount + "개 · 버전 " + DATA.versions.length + "개");
  renderKpi();
  renderWaves(v);
  renderDamage(v);
  renderWeapons(v);
  renderSlot(v);
  renderShop(v);
  renderSessions(v);
  loadHeatmap().catch((e) => { $("hm-info").textContent = e.message; });
}

async function load() {
  const typed = $("key").value.trim();
  if (typed) setKey(typed);
  if (!getKey()) { setStatus("API 키를 입력하세요", true); return; }
  setStatus("불러오는 중…");
  try {
    DATA = await api(ENDPOINT);
    render();
  } catch (e) {
    setStatus(e.message, true);
  }
}

$("key").value = getKey();
$("load").addEventListener("click", load);
$("ver").addEventListener("change", () => { if (DATA) render(); });
$("hmap").addEventListener("change", () => { loadHeatmap().catch((e) => { $("hm-info").textContent = e.message; }); });
$("key").addEventListener("keydown", (e) => { if (e.key === "Enter") load(); });
if (getKey()) load(); else setStatus("API 키를 입력하세요");
