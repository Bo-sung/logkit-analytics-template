namespace LogServer;

/// <summary>
/// poc_ts 분석 페이지 HTML. GGGG 분석 서버의 /analytics와 같은 공용 스타일(PageCommon.Css)과 같은 구성을 쓴다.
/// 데이터는 /api/analytics/poc-ts에서 받으며, API 키는 GM 툴과 같은 로컬 저장소(logkit.key)를 공유한다.
/// 모든 값은 esc()로 이스케이프해서 넣는다.
/// </summary>
public static class DashboardPage
{
    public const string Html = """
<!doctype html>
<html lang="ko">
<head>
<meta charset="utf-8">
<meta name="viewport" content="width=device-width, initial-scale=1">
<title>poc_ts 분석</title>
<style>
"""
    + PageCommon.Css + """
.hb { display: grid; grid-template-columns: 150px 1fr 150px; gap: 8px; align-items: center; margin: 5px 0; font-size: 12.5px; }
.hb .hl { overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
.hb .hv { text-align: right; font-variant-numeric: tabular-nums; color: var(--muted); white-space: nowrap; }
.hbar { height: 10px; background: #0d1117; border: 1px solid var(--border); border-radius: 4px; overflow: hidden; }
.hbar i { display: block; height: 100%; background: var(--accent); }
.hbar i.warn { background: var(--warn); }
.hbar i.bad { background: var(--bad); }
.kpitable td:first-child { color: var(--muted); }
</style>
</head>
<body>
<header>
  <div class="brand">POC_TS<span>TRENCHSLOT ANALYTICS</span></div>
  <nav class="pages">
    <b>버전 통계</b>
    <a href="__GM_TOOL_URL__">GM 툴(세션별 로그)</a>
  </nav>
  <div id="status" class="status"></div>
  <span class="spacer"></span>
  <select id="ver"></select>
  <input id="key" type="password" placeholder="API 키" style="width:150px">
  <button id="load">불러오기</button>
</header>
<main>
  <div id="notice" class="card wide" hidden></div>

  <div class="section">버전 비교</div>
  <section class="card wide">
    <h3>버전별 누적 지표</h3>
    <p class="sub">세션 수가 버전마다 다르므로 판당·발사당 값을 함께 보세요. 기록이 없는 값은 0이 아니라 –로 표시합니다.</p>
    <div class="tablewrap"><div id="kpi"></div></div>
  </section>

  <div class="section">웨이브</div>
  <section class="card">
    <h3>웨이브 완료율</h3>
    <p class="sub">완료 = 정상 마감(completed) / 진입(웨이브 마감 기록 수) · 막대 옆은 완료/진입 · 사망 이탈은 아래 표</p>
    <div id="c-waves"></div>
  </section>
  <section class="card">
    <h3>웨이브별 기록</h3>
    <p class="sub">사망 이탈 = 그 웨이브에서 죽은 판 수 · 클리어 시간은 전멸까지 걸린 시간의 평균</p>
    <div class="tablewrap"><div id="t-waves"></div></div>
  </section>

  <div class="section">전투</div>
  <section class="card">
    <h3>무기별 발사·명중</h3>
    <p class="sub">명중률 = 적에게 맞은 발사 / 전체 발사 · 헤드샷·피해는 실제 적용 값</p>
    <div class="tablewrap"><div id="t-weapons"></div></div>
  </section>
  <section class="card">
    <h3>받은 피해 (적 종류별)</h3>
    <p class="sub">플레이어가 받은 실제 피해 합계 · 막대는 가장 큰 값 기준</p>
    <div id="c-damage"></div>
  </section>

  <div class="section">경제</div>
  <section class="card">
    <h3>슬롯 회전</h3>
    <p class="sub">적중률 = 미스가 아닌 회전 / 전체 회전 · 니어미스 = 미스 중 근접한 회전</p>
    <div id="t-slot"></div>
  </section>
  <section class="card">
    <h3>상점 구매</h3>
    <p class="sub">성공 기준 · 지출은 실제 차감액(actualSpent) 합계</p>
    <div id="t-shop"></div>
  </section>

  <div class="section">위치</div>
  <section class="card wide">
    <h3>이동 히트맵</h3>
    <p class="sub">플레이어 위치를 0.5초 간격으로 모아 칸별 머문 횟수를 색으로 나타냅니다(진할수록 많이 머묾). 맵마다 좌표계가 달라서 맵을 골라서 보세요. 위쪽이 Z가 큰 쪽입니다.</p>
    <div class="row"><select id="hmap" style="min-width:200px"></select></div>
    <div id="hm-info" class="sub"></div>
    <div style="margin-top:8px"><canvas id="hm-canvas" width="560" height="560" style="max-width:100%;background:#0b0f16;border-radius:8px"></canvas></div>
  </section>

  <div class="section">판</div>
  <section class="card wide">
    <h3>판 목록</h3>
    <p class="sub" id="sessionSub"></p>
    <div class="tablewrap"><div id="t-sessions"></div></div>
  </section>

  <section class="card wide">
    <details>
      <summary>집계 기준 — 게임이 남기는 분석 이벤트</summary>
<pre>버전은 session_start.buildVersion입니다. 같은 버전의 모든 판을 합산합니다.

session_end          reason(death / application_quit / unknown), playSeconds, finalReachedWave, totalKills
wave_summary         summaryId별 최고 revision 하나만 · status(completed / interrupted), reason, plan.waveNumber, kills, clearDurationSeconds
slot_spin_resolved   spinId별 하나 · outcome.isMiss / isNearMiss, actualMoneyDelta
shop_purchase_result attemptId별 하나 · success, failureCode, actualSpent, product.itemType
player_shot          shotId · weaponTypeId (발사 수의 기준)
projectile_impact    shotId로 묶음 · hitEnemy(명중), killed(처치), headshot, actualDamage
player_damage        enemyTypeId, actualDamage

이 페이지는 로컬 Telemetry 폴더의 JSONL을 읽습니다. 로그킷 미러는 콘솔 로그만 가져오므로 여기 통계에 쓰지 않습니다.</pre>
    </details>
  </section>
</main>
<div id="tip"></div>
<script>
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
</script>
</body>
</html>
""";
}
