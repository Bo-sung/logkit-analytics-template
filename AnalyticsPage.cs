namespace LogServer;

/// <summary>
/// 분석 서버 기본 페이지(템플릿). 미러 상태, 월별 세션 수, 선택한 월의 버전별 집계를 보여 준다.
/// 게임별 통계 화면은 프로젝트 저장소에서 이 파일을 복사해 확장한다. 데이터는 /api/* 에서 받고, 모든 값은 esc()로 넣는다.
/// __GM_TOOL_URL__은 서버가 GmToolUrl 설정으로 바꾼다.
/// </summary>
public static class AnalyticsPage
{
    public const string Html = """
<!doctype html>
<html lang="ko">
<head>
<meta charset="utf-8">
<meta name="viewport" content="width=device-width, initial-scale=1">
<title>분석 서버</title>
<style>
:root { --bg:#f5f7fb; --fg:#1c2330; --sub:#6b7485; --card:#ffffff; --line:#e3e8f0; --accent:#2f6fed; --err:#c62828; }
@media (prefers-color-scheme: dark) { :root:not([data-theme="light"]) { --bg:#0e1116; --fg:#e6eaf2; --sub:#8a93a6; --card:#161b23; --line:#2a313d; --accent:#6ea0ff; --err:#ff6b6b; } }
:root[data-theme="dark"] { --bg:#0e1116; --fg:#e6eaf2; --sub:#8a93a6; --card:#161b23; --line:#2a313d; --accent:#6ea0ff; --err:#ff6b6b; }
body { margin:0; background:var(--bg); color:var(--fg); font:14px/1.5 system-ui, "Malgun Gothic", sans-serif; }
header { display:flex; flex-wrap:wrap; gap:10px; align-items:center; padding:12px 16px; border-bottom:1px solid var(--line); background:var(--card); }
header .brand { font-weight:700; }
header .brand span { color:var(--sub); font-weight:400; margin-left:8px; }
header nav a { color:var(--accent); margin-right:12px; text-decoration:none; }
header .spacer { flex:1; }
main { padding:16px; max-width:1100px; margin:0 auto; }
.card { background:var(--card); border:1px solid var(--line); border-radius:10px; padding:14px 16px; margin-bottom:14px; }
.card h3 { margin:0 0 6px; font-size:15px; }
.sub { color:var(--sub); font-size:12px; }
.row { display:flex; flex-wrap:wrap; gap:8px; align-items:center; }
input, select, button { font:inherit; padding:5px 8px; border:1px solid var(--line); border-radius:6px; background:var(--card); color:var(--fg); }
button { cursor:pointer; color:var(--accent); }
table { width:100%; border-collapse:collapse; font-size:13px; }
th, td { text-align:left; padding:6px 8px; border-bottom:1px solid var(--line); }
th { color:var(--sub); font-weight:600; }
td.num { text-align:right; font-variant-numeric:tabular-nums; }
.status { color:var(--sub); }
.status.err { color:var(--err); }
.empty { color:var(--sub); padding:12px 0; }
</style>
</head>
<body>
<header>
  <div class="brand">ANALYTICS<span>게임 분석 서버</span></div>
  <nav><a href="__GM_TOOL_URL__">GM 툴(세션별 로그)</a></nav>
  <span class="spacer"></span>
  <span id="status" class="status"></span>
  <input id="key" type="password" placeholder="API 키" style="width:160px">
  <button id="load">불러오기</button>
</header>
<main>
  <section class="card">
    <h3>미러 상태</h3>
    <div id="mirror" class="sub">API 키를 입력하고 불러오세요.</div>
  </section>

  <section class="card">
    <h3>월별 세션</h3>
    <div class="row"><b>월</b><select id="month"></select></div>
    <div id="months" style="margin-top:8px"></div>
  </section>

  <section class="card">
    <h3>버전별 집계</h3>
    <p class="sub">선택한 월의 세션을 앱 버전별로 묶었습니다. 정상 종료 비율은 정상 종료 세션 / 전체 세션입니다.</p>
    <div id="versions"></div>
  </section>
</main>
<script>
"use strict";
const KEY_STORE = "logkit.analytics.key";
const $ = (id) => document.getElementById(id);

function esc(s) {
  return String(s ?? "").replace(/[&<>"']/g, (c) => ({ "&": "&amp;", "<": "&lt;", ">": "&gt;", '"': "&quot;", "'": "&#39;" }[c]));
}
function num(v, d) { return Number(v || 0).toLocaleString("ko-KR", { maximumFractionDigits: d == null ? 0 : d }); }
function getKey() { try { return localStorage.getItem(KEY_STORE) || ""; } catch (_) { return ""; } }
function setKey(k) { try { localStorage.setItem(KEY_STORE, k); } catch (_) {} }
function setStatus(text, isErr) { const el = $("status"); el.textContent = text; el.className = isErr ? "status err" : "status"; }

async function api(path) {
  const res = await fetch(path, { headers: { "X-Api-Key": getKey() } });
  if (res.status === 401) throw new Error("인증 실패 — API 키를 확인하세요");
  if (res.status === 403) throw new Error("이 키로는 열람할 수 없습니다");
  if (!res.ok) throw new Error("HTTP " + res.status);
  return res.json();
}

function renderMirror(m) {
  $("mirror").innerHTML = m.ok
    ? "동기화 정상 · 세션 <b>" + num(m.sessions) + "</b> · 마지막 동기화 " + esc((m.lastSyncUtc || "").replace("T", " ").slice(0, 19)) + " UTC"
    : '<span style="color:var(--err)">동기화 실패: ' + esc(m.lastError || "알 수 없음") + "</span>";
}

function renderMonths(data, selected) {
  const sel = $("month");
  sel.innerHTML = data.months.length
    ? data.months.map((m) => '<option value="' + esc(m.month) + '">' + esc(m.month) + " (" + num(m.sessions) + "세션)</option>").join("")
    : '<option value="">데이터 없음</option>';
  if (selected && data.months.some((m) => m.month === selected)) sel.value = selected;
  $("months").innerHTML = data.months.length
    ? "<table><thead><tr><th>월</th><th class='num'>세션</th><th class='num'>로그</th><th class='num'>경고</th><th class='num'>오류</th><th class='num'>예외</th></tr></thead><tbody>"
      + data.months.map((m) => "<tr><td>" + esc(m.month) + "</td><td class='num'>" + num(m.sessions) + "</td><td class='num'>" + num(m.total)
      + "</td><td class='num'>" + num(m.warnings) + "</td><td class='num'>" + num(m.errors) + "</td><td class='num'>" + num(m.exceptions) + "</td></tr>").join("")
      + "</tbody></table>"
    : '<div class="empty">표시할 월이 없습니다. 게임이 로그를 올리면 여기에 집계됩니다.</div>';
}

function renderVersions(data) {
  const rows = data.versions || [];
  $("versions").innerHTML = rows.length
    ? "<table><thead><tr><th>앱 버전</th><th class='num'>세션</th><th class='num'>로그</th><th class='num'>오류·예외</th><th class='num'>정상 종료</th><th class='num'>평균 플레이(초)</th></tr></thead><tbody>"
      + rows.map((v) => "<tr><td>v" + esc(v.appVersion) + "</td><td class='num'>" + num(v.sessions) + "</td><td class='num'>" + num(v.logs)
      + "</td><td class='num'>" + num(v.errors) + "</td><td class='num'>" + num(v.cleanEndRate * 100, 0) + "%</td><td class='num'>" + num(v.averageDurationSec, 1) + "</td></tr>").join("")
      + "</tbody></table>"
    : '<div class="empty">이 월에 세션이 없습니다.</div>';
}

async function loadVersions() {
  const month = $("month").value;
  const q = month ? "?month=" + encodeURIComponent(month) : "";
  renderVersions(await api("/api/analytics/versions" + q));
}

async function load() {
  const typed = $("key").value.trim();
  if (typed) setKey(typed);
  if (!getKey()) { setStatus("API 키를 입력하세요", true); return; }
  setStatus("불러오는 중…");
  try {
    const [mirror, months] = await Promise.all([api("/api/mirror/status"), api("/api/months")]);
    renderMirror(mirror);
    renderMonths(months, $("month").value);
    await loadVersions();
    setStatus("연결됨");
  } catch (e) {
    setStatus(e.message, true);
  }
}

$("key").value = getKey();
$("load").addEventListener("click", load);
$("key").addEventListener("keydown", (e) => { if (e.key === "Enter") load(); });
$("month").addEventListener("change", () => loadVersions().catch((e) => setStatus(e.message, true)));
if (getKey()) load(); else setStatus("API 키를 입력하세요");
</script>
</body>
</html>
""";
}
