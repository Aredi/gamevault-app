// SanctuaryVault — install a game on my PC, or publish a game from a browser. Same file name rules as the client's "Publish a Game"
// (GameVault.Core/Publishing), same matching of the games already on the server, same chunked upload.
"use strict";

const CHUNK = 8 * 1024 * 1024;
const $ = id => document.getElementById(id);
const show = (id, visible = true) => { $(id).hidden = !visible; };

// ---- Session: the GameVault tokens, kept for the tab or on this device ----
const store = () => (localStorage.getItem("sv.remember") === "1" ? localStorage : sessionStorage);
const tokens = {
  get access() { return store().getItem("sv.access"); },
  get refresh() { return store().getItem("sv.refresh"); },
  set(pair, remember) {
    if (remember !== undefined) {
      localStorage.setItem("sv.remember", remember ? "1" : "0");
    }
    store().setItem("sv.access", pair.access_token);
    store().setItem("sv.refresh", pair.refresh_token);
  },
  clear() {
    for (const s of [localStorage, sessionStorage]) { s.removeItem("sv.access"); s.removeItem("sv.refresh"); }
  },
};

// Under the GameVault site (gamevault.example.com/sanctuary/), its web page keeps its sign-in here: the same one is used
const SITE_REFRESH = "app_refresh_token";

async function refreshTokens(refresh = tokens.refresh) {
  if (!refresh) return false;
  const response = await fetch("auth/refresh", { method: "POST", headers: { Authorization: "Bearer " + refresh } });
  if (!response.ok) return false;
  const pair = await response.json();
  // Renewed for both pages: the GameVault one must still find a valid sign-in
  if (localStorage.getItem(SITE_REFRESH) === refresh && pair.refresh_token) localStorage.setItem(SITE_REFRESH, pair.refresh_token);
  tokens.set(pair);
  return true;
}

/** A request with the GameVault sign-in; the session is renewed once when it expired. */
async function api(path, options = {}, retried = false) {
  const headers = Object.assign({}, options.headers, tokens.access ? { Authorization: "Bearer " + tokens.access } : {});
  const response = await fetch(path, Object.assign({}, options, { headers }));
  if (response.status === 401 && !retried && await refreshTokens()) return api(path, options, true);
  if (response.status === 401) { signOut(); throw new Error("La session a expiré : reconnectez-vous."); }
  return response;
}

async function errorOf(response) {
  try { const body = await response.json(); return body.error || body.message || response.statusText; }
  catch { return response.statusText || String(response.status); }
}

// ---- File names, as the server reads them ----
const ARCHIVE = /\.(zip|7z|rar|iso|tar|gz|tgz|xz|txz|bz2|tbz2|zst)$/i;

function archiveExtension(file) {
  const match = /(\.tar\.(gz|xz|bz2)|\.[A-Za-z0-9]+)$/.exec(file);
  return match ? match[0].toLowerCase() : ".zip";
}

function guessTitle(fileName) {
  let name = fileName;
  name = name.replace(/\.(zip|7z|rar|iso|exe|msi|tar|gz|xz|bz2|tar\.gz|tar\.xz)$/i, "");
  name = name.replace(/\.tar$/i, "");
  name = name.replace(/[([].*?[)\]]/g, " ");
  name = name.replace(/[\s._-]+v?\d+([._]\d+)+.*$/i, "");
  name = name.replace(/[._]+/g, " ").trim();
  name = name.replace(/[-\s]+(GOG|Steam|Portable|Setup|Installer|Win(64|32)?|x64|x86)$/i, "");
  return name.replace(/\s+/g, " ").replace(/^[ -]+|[ -]+$/g, "");
}

function cleanTitle(title) {
  return title.replace(/\(/g, "[").replace(/\)/g, "]").replace(/[<>:"/\\|?*\x00-\x1f]/g, "").replace(/\s+/g, " ").trim();
}

function buildFileName(title, version, type, year, earlyAccess, extension) {
  const clean = cleanTitle(title);
  if (!clean) return "";
  let name = clean;
  if (version && version.trim()) name += ` (v${version.trim().replace(/^[vV]+/, "").replace(/\(/g, "[").replace(/\)/g, "]")})`;
  if (earlyAccess) name += " (EA)";
  name += ` (${type})`;
  const y = parseInt(year, 10);
  if (y >= 1950 && y <= 2100) name += ` (${y})`;
  return name + extension;
}

const versionTag = version => (version && version.trim() ? "v" + version.trim().replace(/^[vV]+/, "").replace(/\(/g, "[").replace(/\)/g, "]") : null);

function normalize(name) {
  if (!name || !name.trim()) return "";
  name = name.toLowerCase().replace(/_/g, " ");
  name = name.replace(/^(a|an|the)\b/, "");
  name = name.replace(/,\s(a|an|the)\b(?=\s*[^\p{L}\p{N}_\s]|$)/u, "");
  name = name.replace(/[^\p{L}\p{N}_\s]/gu, " ").replace(/\s+/g, " ");
  return name.normalize("NFD").replace(/\p{Mn}/gu, "").normalize("NFC").trim();
}

function jaroWinkler(a, b) {
  if (a === b) return 1;
  if (!a.length || !b.length) return 0;
  const range = Math.max(0, Math.floor(Math.max(a.length, b.length) / 2) - 1);
  const am = new Array(a.length).fill(false), bm = new Array(b.length).fill(false);
  let matches = 0;
  for (let i = 0; i < a.length; i++)
    for (let j = Math.max(0, i - range); j < Math.min(b.length, i + range + 1); j++) {
      if (bm[j] || a[i] !== b[j]) continue;
      am[i] = bm[j] = true; matches++; break;
    }
  if (!matches) return 0;
  let t = 0;
  for (let i = 0, k = 0; i < a.length; i++) {
    if (!am[i]) continue;
    while (!bm[k]) k++;
    if (a[i] !== b[k]) t++;
    k++;
  }
  const jaro = (matches / a.length + matches / b.length + (matches - t / 2) / matches) / 3;
  let prefix = 0;
  while (prefix < Math.min(4, a.length, b.length) && a[prefix] === b[prefix]) prefix++;
  return jaro + prefix * 0.1 * (1 - jaro);
}

const isCandidate = (title, game) => {
  const a = normalize(title), b = normalize(game.title);
  return a.length >= 2 && b.length > 0 && (a === b || b.startsWith(a + " ") || jaroWinkler(a, b) >= 0.9);
};
const mergesWith = (title, year, game) => cleanTitle(title) === game.title && (!year || year === game.year);
const spelledDifferently = (title, game) => cleanTitle(title) !== game.title && normalize(title) === normalize(game.title);

// ---- Form ----
let file = null;
let candidates = [];
let searchTimer = null;
let searchGeneration = 0;
let uploading = null;

function currentFileName() {
  if (!file) return "";
  return buildFileName($("title").value, $("version").value, $("type").value, $("year").value, $("earlyAccess").checked, archiveExtension(file.name));
}

function refresh() {
  const name = currentFileName();
  $("fileName").textContent = name || "—";
  $("publishButton").disabled = !name || !!uploading;
  evaluateExisting();
}

function chooseFile(chosen) {
  if (!chosen) return;
  if (!ARCHIVE.test(chosen.name)) {
    status(`« ${chosen.name} » n’est pas une archive : envoyez un .zip, .7z, .rar, .iso, .tar.gz…`, "error");
    return;
  }
  file = chosen;
  $("drop").classList.add("has");
  $("dropText").textContent = `${file.name} · ${formatSize(file.size)}`;
  $("title").value = guessTitle(file.name);
  const version = /v?(\d+(\.\d+)+)/i.exec(file.name);
  $("version").value = version ? version[1] : "";
  const year = /[([ ._-]((19|20)\d{2})[)\] ._-]/.exec(file.name);
  $("year").value = year ? year[1] : "";
  $("type").value = /setup|installer/i.test(file.name) ? "W_S" : "W_P";
  show("details");
  status("");
  findExisting();
  refresh();
}

function findExisting() {
  clearTimeout(searchTimer);
  searchTimer = setTimeout(async () => {
    const generation = ++searchGeneration;
    const title = $("title").value.trim();
    if (title.length < 2) { candidates = []; evaluateExisting(); return; }
    try {
      const query = title.split(/\s+/).slice(0, 3).join(" ");
      const response = await api("games?search=" + encodeURIComponent(query));
      const games = response.ok ? await response.json() : [];
      if (generation !== searchGeneration) return;
      candidates = games.filter(g => g.title && isCandidate(title, g))
        .sort((a, b) => jaroWinkler(normalize(title), normalize(b.title)) - jaroWinkler(normalize(title), normalize(a.title)))
        .slice(0, 4);
    } catch { candidates = []; }
    evaluateExisting();
  }, 400);
}

function evaluateExisting() {
  const title = $("title").value.trim();
  const year = parseInt($("year").value, 10) || null;
  const merged = candidates.find(g => mergesWith(title, year, g));
  const lookalike = merged ? null : candidates.find(g => spelledDifferently(title, g));
  const list = $("matchList");
  list.replaceChildren(...candidates.map(game => {
    const button = document.createElement("button");
    button.type = "button";
    button.className = "match" + (game === merged ? " selected" : "");
    button.innerHTML = `<span></span><small></small>`;
    button.firstChild.textContent = game.title;
    button.lastChild.textContent = game.version ? `Sur le serveur : ${game.version}` : "Sur le serveur, sans version";
    button.onclick = () => {
      $("title").value = game.title;
      $("year").value = game.year || "";
      refresh();
    };
    return button;
  }));
  show("matches", candidates.length > 0);
  hint("existingHint", merged ? `Sera ajouté comme nouvelle version de « ${merged.title} » : la mise à jour sera proposée aux joueurs qui l’ont installé.` : "");
  hint("titleWarning", lookalike ? `« ${lookalike.title} » est déjà sur le serveur, écrit différemment : en l’état, un second jeu serait créé. Choisissez-le ci-dessus pour publier une nouvelle version.` : "");
  const tag = versionTag($("version").value);
  hint("versionWarning", !merged ? "" : !tag ? "Indiquez la version de ce fichier (ex. 2.0) : sans elle, il ne remplace pas la version actuelle."
    : merged.version && merged.version.toLowerCase() === tag.toLowerCase() ? `La version ${tag} est déjà sur le serveur.` : "");
}

function hint(id, text) { $(id).textContent = text; show(id, !!text); }

function status(text, kind = "") {
  $("status").textContent = text;
  $("status").className = "status " + kind;
  show("status", !!text);
}

function formatSize(bytes) {
  const units = ["o", "Ko", "Mo", "Go", "To"];
  let value = bytes, unit = 0;
  while (value >= 1024 && unit < units.length - 1) { value /= 1024; unit++; }
  return `${value.toLocaleString("fr-FR", { maximumFractionDigits: unit < 2 ? 0 : 1 })} ${units[unit]}`;
}

function formatDuration(seconds) {
  if (!isFinite(seconds)) return "…";
  if (seconds < 60) return `${Math.ceil(seconds)} s`;
  if (seconds < 3600) return `${Math.ceil(seconds / 60)} min`;
  return `${Math.floor(seconds / 3600)} h ${Math.ceil((seconds % 3600) / 60)} min`;
}

// ---- Upload: chunks, continued after an interruption ----
const uploadUrl = (name, suffix = "") => "uploads/" + encodeURIComponent(name) + suffix;

async function publish() {
  const name = currentFileName();
  if (!file || !name) return;
  const total = file.size;
  uploading = { aborted: false, controller: null };
  $("publishButton").disabled = true;
  show("cancelButton");
  show("progress");
  status("Préparation de l’envoi…");
  try {
    let response = await api(uploadUrl(name));
    if (!response.ok) throw new Error(await errorOf(response));
    const state = await response.json();
    let overwrite = false;
    if (state.exists) {
      if (!confirm(`${name} existe déjà sur le serveur. Le remplacer ?`)) { status(""); return; }
      overwrite = true;
    }
    if (state.freeSpace >= 0 && state.freeSpace < total - state.received) throw new Error(`Pas assez de place sur le serveur (${formatSize(state.freeSpace)} libres).`);
    // Continuing an earlier upload is only safe with the very same file
    const key = "sv.upload." + name;
    const fingerprint = `${file.size}|${file.lastModified}|${file.name}`;
    let offset = state.received;
    if (offset > 0 && (localStorage.getItem(key) !== fingerprint || offset > total)) {
      await api(uploadUrl(name), { method: "DELETE" });
      offset = 0;
    }
    localStorage.setItem(key, fingerprint);
    if (offset > 0) status(`Reprise de l’envoi à ${formatSize(offset)}…`);

    const started = performance.now(), startOffset = offset;
    let failures = 0;
    while (offset < total) {
      if (uploading.aborted) throw new Error("aborted");
      const end = Math.min(offset + CHUNK, total);
      uploading.controller = new AbortController();
      try {
        response = await api(uploadUrl(name, `?offset=${offset}&total=${total}`), { method: "PUT", body: file.slice(offset, end), signal: uploading.controller.signal });
      } catch (error) {
        if (uploading.aborted) throw new Error("aborted");
        if (++failures > 5) throw new Error("La connexion au serveur est perdue. Relancez « Publier » : l’envoi reprendra.");
        status(`Connexion perdue, nouvel essai (${failures}/5)…`);
        await new Promise(resolve => setTimeout(resolve, 2000 * failures));
        continue;
      }
      if (response.status === 409) { offset = (await response.json()).received; continue; }
      if (!response.ok) throw new Error(await errorOf(response));
      failures = 0;
      offset = (await response.json()).received;
      const seconds = (performance.now() - started) / 1000;
      const speed = (offset - startOffset) / Math.max(seconds, 0.001);
      $("bar").style.width = `${(offset / total * 100).toFixed(1)}%`;
      $("progressText").textContent = `${formatSize(offset)} sur ${formatSize(total)} · ${formatSize(speed)}/s · reste ${formatDuration((total - offset) / speed)}`;
      status("Envoi en cours… Gardez cette page ouverte.");
    }
    status("Finalisation…");
    response = await api(uploadUrl(name, `/complete?size=${total}${overwrite ? "&overwrite=true" : ""}`), { method: "POST" });
    if (!response.ok) throw new Error(await errorOf(response));
    localStorage.removeItem(key);
    $("bar").style.width = "100%";
    status(`« ${name} » est publié. Le serveur l’ajoute à la bibliothèque dans quelques instants.`, "ok");
  } catch (error) {
    if (error.message === "aborted") status("Envoi interrompu. Relancez « Publier » avec le même fichier : il reprendra où il s’est arrêté.");
    else status(error.message || String(error), "error");
  } finally {
    uploading = null;
    show("cancelButton", false);
    refresh();
  }
}

// ---- Install on my PC ----
function remoteStatus(text, kind = "") {
  $("remoteStatus").textContent = text;
  $("remoteStatus").className = "status " + kind;
  show("remoteStatus", !!text);
}

function ago(date) {
  const minutes = Math.round((Date.now() - new Date(date)) / 60000);
  if (minutes < 60) return `il y a ${Math.max(minutes, 1)} min`;
  if (minutes < 48 * 60) return `il y a ${Math.round(minutes / 60)} h`;
  return `il y a ${Math.round(minutes / 1440)} jours`;
}

function remoteOutcome(entry) {
  const pc = entry.deviceName;
  if (entry.status === "started") return `Téléchargement lancé sur ${pc}`;
  if (entry.status === "failed") return `Pas installé sur ${pc} : ${entry.message || "erreur inconnue"}`;
  const device = remoteDevices.find(d => d.id === entry.deviceId);
  return device && !device.online ? `En attente : ${pc} est fermé, ça partira à son ouverture` : `En attente de ${pc}…`;
}

let remoteDevices = [];
async function loadRemote() {
  const [devices, installs] = await Promise.all([api("remote/devices"), api("remote/installs")]);
  if (!devices.ok || !installs.ok) { show("remote", false); return; }
  remoteDevices = await devices.json();
  const select = $("remoteDevice");
  const chosen = select.value || localStorage.getItem("sv.device");
  select.replaceChildren(...remoteDevices.map(d => new Option(d.online ? `${d.name} — ouvert` : `${d.name} — fermé (vu ${ago(d.lastSeen)})`, d.id)));
  if (remoteDevices.some(d => d.id === chosen)) select.value = chosen;
  show("remoteDeviceField", remoteDevices.length > 0);
  show("remoteNoDevice", remoteDevices.length === 0);

  const entries = await installs.json();
  const list = $("remoteList");
  list.replaceChildren();
  for (const entry of entries) {
    const button = document.createElement("button");
    button.className = "match";
    button.title = "Retirer de la liste";
    button.textContent = entry.title || "Jeu " + entry.gameId;
    const small = document.createElement("small");
    small.textContent = `${remoteOutcome(entry)} · demandé ${ago(entry.requestedAt)} · ✕ retirer`;
    button.append(small);
    button.onclick = async () => { await api("remote/installs/" + entry.id, { method: "DELETE" }); await loadRemote(); };
    list.append(button);
  }
  show("remoteEmpty", entries.length === 0);
  show("remote");
}

/** Buttons for games to install: from a search, or the game opened on the GameVault site. */
function showRemoteChoices(games) {
  const results = $("remoteResults");
  results.replaceChildren();
  for (const game of games) {
    const button = document.createElement("button");
    button.className = "match";
    button.textContent = game.title;
    const small = document.createElement("small");
    small.textContent = [game.version, game.year].filter(Boolean).join(" · ") || "Installer sur ce PC";
    button.append(small);
    button.onclick = () => askRemote(game);
    results.append(button);
  }
}

async function askRemote(game) {
  const deviceId = $("remoteDevice").value;
  const device = remoteDevices.find(d => d.id === deviceId);
  if (!device) { remoteStatus("Choisis d’abord un de tes PC.", "error"); return; }
  const added = await api("remote/installs", { method: "POST", headers: { "Content-Type": "application/json" }, body: JSON.stringify({ gameId: game.id, title: game.title, deviceId }) });
  if (!added.ok) { remoteStatus(await errorOf(added), "error"); return; }
  localStorage.setItem("sv.device", deviceId);
  remoteStatus(device.online
    ? `« ${game.title} » part sur ${device.name} : le téléchargement démarre dans la minute.`
    : `« ${game.title} » se téléchargera sur ${device.name} dès que SanctuaryVault y sera ouvert.`, "ok");
  $("remoteSearch").value = "";
  $("remoteResults").replaceChildren();
  await loadRemote();
}

let remoteSearchGeneration = 0;
async function searchRemote() {
  const generation = ++remoteSearchGeneration;
  const text = $("remoteSearch").value.trim();
  if (text.length < 2) { $("remoteResults").replaceChildren(); return; }
  const response = await api("games?search=" + encodeURIComponent(text));
  if (!response.ok || generation !== remoteSearchGeneration) return;
  showRemoteChoices(await response.json());
}

/** Opened from a game's page of the GameVault site (its "Install on my PC" button): that game is offered. */
async function offerAskedGame() {
  const id = Number(new URLSearchParams(location.search).get("install"));
  if (!id) return;
  const response = await api("games/" + id);
  if (!response.ok) return;
  showRemoteChoices([await response.json()]);
  remoteStatus("Choisis le PC puis clique sur le jeu.");
  $("remote").scrollIntoView();
}

// ---- Screens ----
function signOut() {
  tokens.clear();
  show("publish", false); show("remote", false); show("who", false);
  show("login");
}

async function start() {
  if (!tokens.access && !await refreshTokens(localStorage.getItem(SITE_REFRESH))) { signOut(); return; }
  try {
    const response = await api("me");
    if (!response.ok) { signOut(); return; }
    const me = await response.json();
    $("whoName").textContent = me.username;
    show("who"); show("login", false);
    show("publish", me.admin);
    await loadRemote();
    await offerAskedGame();
  } catch { signOut(); }
}

// The requests' outcomes and the open PCs change: shown again while the page is in view
setInterval(() => { if (!document.hidden && !$("remote").hidden && !uploading) loadRemote().catch(() => {}); }, 15000);

$("loginForm").addEventListener("submit", async event => {
  event.preventDefault();
  show("loginError", false);
  $("loginButton").disabled = true;
  try {
    const response = await fetch("auth/login", {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({ username: $("username").value, password: $("password").value }),
    });
    if (!response.ok) {
      const message = response.status === 401 ? "Nom d’utilisateur ou mot de passe incorrect." : await errorOf(response);
      throw new Error(message);
    }
    tokens.clear();
    tokens.set(await response.json(), $("remember").checked);
    $("password").value = "";
    await start();
  } catch (error) {
    $("loginError").textContent = error.message;
    show("loginError");
  } finally {
    $("loginButton").disabled = false;
  }
});

$("logout").onclick = signOut;
$("file").onchange = () => chooseFile($("file").files[0]);
const drop = $("drop");
drop.addEventListener("dragover", e => { e.preventDefault(); drop.classList.add("over"); });
drop.addEventListener("dragleave", () => drop.classList.remove("over"));
drop.addEventListener("drop", e => { e.preventDefault(); drop.classList.remove("over"); chooseFile(e.dataTransfer.files[0]); });
for (const id of ["title", "version", "year", "type", "earlyAccess"]) $(id).addEventListener("input", refresh);
$("type").addEventListener("change", refresh);
$("title").addEventListener("input", findExisting);
$("publishButton").onclick = publish;
let remoteTimer;
$("remoteSearch").addEventListener("input", () => { clearTimeout(remoteTimer); remoteTimer = setTimeout(searchRemote, 300); });
$("cancelButton").onclick = () => { if (uploading) { uploading.aborted = true; uploading.controller?.abort(); } };
window.addEventListener("beforeunload", e => { if (uploading) { e.preventDefault(); e.returnValue = ""; } });

start();
