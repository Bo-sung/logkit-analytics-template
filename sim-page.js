
"use strict";
const ENDPOINT = "/api/analytics/poc-ts";
const KEY_STORE = "logkit.key";
const ALL = "";
const PALETTE = ["#ff5a5a", "#ffb84d", "#5ad1ff", "#7dff7a", "#c48bff", "#ff7ac8", "#ffe65a", "#5affc6", "#ff9a5a", "#a3b8ff"];
const SIM = { sessions: [], maps: [], versions: [], maxT: 0, t: 0, playing: false, last: 0, visible: {} };
const TRAIL_OPTIONS = [["5", "꼬리 5초"], ["15", "꼬리 15초"], ["30", "꼬리 30초"], ["0", "전체 경로"]];
const $ = (id) => document.getElementById(id);

function esc(s) {
  return String(s ?? "").replace(/[&<>"']/g, (c) => ({ "&": "&amp;", "<": "&lt;", ">": "&gt;", '"': "&quot;", "'": "&#39;" }[c]));
}
function getKey() { try { return localStorage.getItem(KEY_STORE) || ""; } catch (_) { return ""; } }
function setKey(k) { try { localStorage.setItem(KEY_STORE, k); } catch (_) {} }
function setStatus(text, isErr) { const el = $("status"); el.textContent = text; el.className = isErr ? "status err" : "status"; }
function num(v, d) { return Number(v || 0).toLocaleString("ko-KR", { maximumFractionDigits: d == null ? 1 : d }); }

async function api(path) {
  const res = await fetch(path, { headers: { "X-Api-Key": getKey() } });
  if (res.status === 401) throw new Error("인증 실패 — API 키를 확인하세요");
  if (res.status === 403) throw new Error("이 키로는 열람할 수 없습니다");
  if (!res.ok) throw new Error("HTTP " + res.status);
  return res.json();
}

function simTime(sec) {
  const s = Math.max(0, sec || 0);
  const m = Math.floor(s / 60), r = s - m * 60;
  return m + ":" + (r < 10 ? "0" : "") + r.toFixed(1);
}

// 선택한 버전·맵을 서버에서 받아 판 목록과 선택 상자를 채운다.
async function loadPaths() {
  const p = new URLSearchParams();
  if ($("ver").value) p.set("version", $("ver").value);
  if ($("map").value) p.set("map", $("map").value);
  const data = await api(ENDPOINT + "/paths?" + p);
  if (!data.configured) {
    $("notice").hidden = false;
    $("notice").innerHTML = "<h3>분석 폴더가 설정되지 않았거나 없습니다</h3><p class=\"sub\">TelemetryPath를 확인하세요.</p>";
    return;
  }
  $("notice").hidden = true;
  SIM.versions = data.versions;
  SIM.maps = data.maps;
  fillSelect("ver", [["", "전체 버전"]].concat(data.versions.map((v) => [v, "v" + v])), data.version);
  fillSelect("map", [["", "전체 맵 (좌표계가 섞임)"]].concat(data.maps.map((m) => [m, m])), data.map);

  SIM.sessions = data.sessions.map((s, i) => ({ ...s, color: PALETTE[i % PALETTE.length] }));
  for (const s of SIM.sessions) if (!(s.sessionId in SIM.visible)) SIM.visible[s.sessionId] = true;
  SIM.maxT = SIM.sessions.reduce((m, s) => Math.max(m, s.points.length ? s.points[s.points.length - 1][0] : 0), 0);
  SIM.t = Math.min(SIM.t, SIM.maxT);
  $("sim-seek").max = String(SIM.maxT || 1);
  $("sim-seek").value = String(SIM.t);
  renderLegend();
  simInfo(data);
  simDraw();
}

function fillSelect(id, options, selected) {
  const sel = $(id);
  sel.innerHTML = options.map(([v, label]) => '<option value="' + esc(v) + '">' + esc(label) + "</option>").join("");
  sel.value = options.some(([v]) => v === selected) ? selected : options[0][0];
}

function simInfo(data) {
  const shown = SIM.sessions.filter((s) => SIM.visible[s.sessionId]).length;
  const mixed = !$("map").value && SIM.maps.length > 1 ? " · 맵이 여러 개라 좌표가 섞였습니다" : "";
  $("sim-info").textContent = SIM.sessions.length
    ? "판 " + num(SIM.sessions.length, 0) + "개(표시 " + num(shown, 0) + "개) · 최대 " + simTime(SIM.maxT) + mixed
    : "선택한 조건에 위치 기록이 있는 판이 없습니다.";
}

function renderLegend() {
  $("sim-legend").innerHTML = SIM.sessions.map((s) =>
    '<label style="display:flex;align-items:center;gap:6px;padding:3px 0;font-size:12px;cursor:pointer">'
    + '<input type="checkbox" data-id="' + esc(s.sessionId) + '"' + (SIM.visible[s.sessionId] ? " checked" : "") + ">"
    + '<span style="display:inline-block;width:12px;height:12px;border-radius:3px;background:' + s.color + '"></span>'
    + '<span>' + esc((s.startUtc || "").replace("T", " ").slice(5, 16)) + " · " + esc(s.buildVersion) + " · " + esc(s.endReason || "") + " · " + esc(simTime(s.points.length ? s.points[s.points.length - 1][0] : 0)) + "</span></label>"
  ).join("") || '<div class="sub">판이 없습니다.</div>';
  for (const box of $("sim-legend").querySelectorAll("input[type=checkbox]")) {
    box.onchange = () => { SIM.visible[box.dataset.id] = box.checked; simInfo(); simDraw(); };
  }
}

// 두 기록 사이는 선형으로 보간한다. 판이 끝난 뒤의 시간이면 null을 돌려준다(점을 숨긴다).
function posAt(points, t) {
  if (!points.length || t < points[0][0]) return null;
  if (t > points[points.length - 1][0]) return null;
  let lo = 0, hi = points.length - 1;
  while (hi - lo > 1) { const mid = (lo + hi) >> 1; if (points[mid][0] <= t) lo = mid; else hi = mid; }
  const a = points[lo], b = points[hi];
  if (t === a[0]) return a;
  const k = (t - a[0]) / ((b[0] - a[0]) || 1);
  return [t, a[1] + (b[1] - a[1]) * k, a[2] + (b[2] - a[2]) * k];
}

function simDraw() {
  const c = $("sim-canvas");
  const g = c.getContext("2d");
  const W = c.width, H = c.height;
  g.clearRect(0, 0, W, H);
  g.strokeStyle = "rgba(255,255,255,0.07)";
  g.lineWidth = 1;
  for (let k = 1; k < 4; k++) { const p = Math.round(W * k / 4) + 0.5; g.beginPath(); g.moveTo(p, 0); g.lineTo(p, H); g.moveTo(0, p); g.lineTo(W, p); g.stroke(); }
  $("sim-clock").textContent = simTime(SIM.t) + " / " + simTime(SIM.maxT);

  const shown = SIM.sessions.filter((s) => SIM.visible[s.sessionId] && s.points.length);
  if (!shown.length) return;

  // 보이는 판 전체 범위를 정사각형으로 맞춘다(위쪽이 Z가 큰 쪽).
  let minX = Infinity, maxX = -Infinity, minZ = Infinity, maxZ = -Infinity;
  for (const s of shown) for (const p of s.points) { minX = Math.min(minX, p[1]); maxX = Math.max(maxX, p[1]); minZ = Math.min(minZ, p[2]); maxZ = Math.max(maxZ, p[2]); }
  const half = Math.max(maxX - minX, maxZ - minZ, 1) / 2 * 1.08;
  const cx = (minX + maxX) / 2, cz = (minZ + maxZ) / 2;
  const toX = (x) => (x - (cx - half)) / (2 * half) * W;
  const toY = (z) => H - (z - (cz - half)) / (2 * half) * H;
  const trail = Number($("sim-trail").value);

  for (const s of shown) {
    const pts = s.points;
    // 전체 동선(흐리게)
    g.strokeStyle = s.color + "33";
    g.lineWidth = 1.5;
    g.beginPath();
    pts.forEach((p, i) => { if (i === 0) g.moveTo(toX(p[1]), toY(p[2])); else g.lineTo(toX(p[1]), toY(p[2])); });
    g.stroke();

    // 꼬리: 현재 시각 기준 최근 구간
    const from = trail > 0 ? SIM.t - trail : -Infinity;
    const now = posAt(pts, SIM.t);
    g.strokeStyle = s.color;
    g.lineWidth = 3;
    g.beginPath();
    let started = false;
    for (const p of pts) {
      if (p[0] < from) continue;
      if (p[0] > SIM.t) break;
      if (!started) { g.moveTo(toX(p[1]), toY(p[2])); started = true; } else g.lineTo(toX(p[1]), toY(p[2]));
    }
    if (started && now) g.lineTo(toX(now[1]), toY(now[2]));
    g.stroke();

    if (now) {
      g.fillStyle = s.color;
      g.beginPath();
      g.arc(toX(now[1]), toY(now[2]), 6, 0, Math.PI * 2);
      g.fill();
    }
  }
}

function simTick(now) {
  if (!SIM.playing) return;
  const dt = (now - SIM.last) / 1000;
  SIM.last = now;
  SIM.t = Math.min(SIM.maxT, SIM.t + dt * Number($("sim-speed").value));
  $("sim-seek").value = String(SIM.t);
  simDraw();
  if (SIM.t >= SIM.maxT) { simSetPlaying(false); return; }
  requestAnimationFrame(simTick);
}

function simSetPlaying(on) {
  SIM.playing = on;
  $("sim-play").textContent = on ? "❚❚ 정지" : "▶ 재생";
  if (on && SIM.maxT > 0) {
    if (SIM.t >= SIM.maxT) SIM.t = 0;
    SIM.last = performance.now();
    requestAnimationFrame(simTick);
  }
}

function setAllVisible(on) {
  for (const s of SIM.sessions) SIM.visible[s.sessionId] = on;
  renderLegend();
  simInfo();
  simDraw();
}

function init() {
  $("sim-speed").innerHTML = [1, 2, 4, 8, 16].map((n) => '<option value="' + n + '"' + (n === 4 ? " selected" : "") + ">" + n + "배속</option>").join("");
  $("sim-trail").innerHTML = TRAIL_OPTIONS.map(([v, label]) => '<option value="' + v + '">' + label + "</option>").join("");
  $("sim-trail").value = "15";
  $("sim-play").onclick = () => simSetPlaying(!SIM.playing);
  $("sim-seek").oninput = () => { SIM.t = Number($("sim-seek").value); simDraw(); };
  $("sim-trail").onchange = simDraw;
  $("ver").onchange = () => { SIM.t = 0; loadPaths().catch((e) => setStatus(e.message, true)); };
  $("map").onchange = () => { SIM.t = 0; loadPaths().catch((e) => setStatus(e.message, true)); };
  $("all-on").onclick = () => setAllVisible(true);
  $("all-off").onclick = () => setAllVisible(false);
}

async function load() {
  const typed = $("key").value.trim();
  if (typed) setKey(typed);
  if (!getKey()) { setStatus("API 키를 입력하세요", true); return; }
  setStatus("불러오는 중…");
  try {
    await loadPaths();
    setStatus("판 " + SIM.sessions.length + "개 표시");
  } catch (e) {
    setStatus(e.message, true);
  }
}

$("key").value = getKey();
$("load").addEventListener("click", load);
$("key").addEventListener("keydown", (e) => { if (e.key === "Enter") load(); });
init();
if (getKey()) load(); else setStatus("API 키를 입력하세요");
