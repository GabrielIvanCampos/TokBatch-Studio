const $ = (id) => document.getElementById(id);

const urlEl = $("url");
const destEl = $("dest");
const logEl = $("log");
const statusEl = $("status");
const statusText = $("status-text");
const btnBaixar = $("btn-baixar");
const btnParar = $("btn-parar");
let running = false;
const history = [];

function setStatus(text, kind) {
  statusText.textContent = text;
  statusEl.classList.remove("busy", "err");
  if (kind) statusEl.classList.add(kind);
}

function appendLog(line) {
  const cur = logEl.textContent.trim();
  logEl.textContent = cur && cur !== "Pronto para baixar." ? cur + "\n" + line : line;
  logEl.scrollTop = logEl.scrollHeight;
}

function remember(line) {
  history.push(line);
  const hist = $("hist");
  hist.textContent = history.join("\n");
}

window.appendLog = appendLog;
window.setStatus = setStatus;
window.onDownloadDone = function (ok, message) {
  running = false;
  btnBaixar.disabled = false;
  btnParar.disabled = true;
  setStatus(ok ? "Pronto." : "Falhou.", ok ? "" : "err");
  if (message) appendLog(message);
};

let httpHostPromise = null;
function useHttp() {
  if (location.protocol === "file:") return Promise.resolve(false);
  if (!httpHostPromise) {
    httpHostPromise = fetch("/api/info").then((r) => r.ok).catch(() => false);
  }
  return httpHostPromise;
}

async function host(name, ...args) {
  const fn = window[name];
  if (typeof fn === "function") return await fn(...args);
  if (!(await useHttp())) return null;
  const res = await fetch("/api/" + name, {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify({ a: args[0] == null ? "" : args[0] })
  });
  if (!res.ok) return null;
  const data = await res.json();
  return data.value;
}

if (location.protocol !== "file:") {
  setInterval(async () => {
    if (!(await useHttp())) return;
    try {
      const res = await fetch("/api/poll");
      const data = await res.json();
      (data.lines || []).forEach(appendLog);
      if (data.finished) window.onDownloadDone(!!data.ok, data.message || "");
    } catch (e) {}
  }, 500);
}

document.querySelectorAll(".nav-btn").forEach((btn) => {
  btn.addEventListener("click", () => {
    document.querySelectorAll(".nav-btn").forEach((b) => b.classList.remove("active"));
    btn.classList.add("active");
    const view = btn.dataset.view;
    document.querySelectorAll(".card").forEach((card) => {
      card.hidden = card.id !== "view-" + view;
    });
    if (view === "config") loadConfig();
  });
});

$("btn-colar").addEventListener("click", async () => {
  let text = await host("pasteClipboard");
  if (!text && navigator.clipboard) {
    try { text = await navigator.clipboard.readText(); } catch (e) { text = ""; }
  }
  text = (text || "").trim();
  if (!text) {
    appendLog("A área de transferência está vazia.");
    return;
  }
  urlEl.value = text;
  appendLog("Link colado.");
});

$("btn-escolher").addEventListener("click", async () => {
  const picked = await host("pickFolder", destEl.value);
  if (picked) destEl.value = picked;
});

$("btn-abrir").addEventListener("click", async () => {
  const dest = destEl.value.trim();
  const opened = await host("openFolder", dest);
  if (opened === false) appendLog("Não foi possível abrir a pasta.");
});

$("btn-parar").addEventListener("click", async () => {
  if (!running) return;
  await host("stopDownload");
  appendLog("Parando…");
});

$("btn-baixar").addEventListener("click", async () => {
  const url = urlEl.value.trim();
  const dest = destEl.value.trim();
  if (!url || url.includes("@seu_usuario")) {
    setStatus("Informe o link.", "err");
    appendLog("Cole o link de um perfil do TikTok.");
    return;
  }
  if (!dest) {
    setStatus("Informe a pasta.", "err");
    appendLog("Escolha a pasta de destino.");
    return;
  }
  running = true;
  btnBaixar.disabled = true;
  btnParar.disabled = false;
  setStatus("Baixando…", "busy");
  appendLog("Iniciando download…");
  const payload = {
    url,
    dest,
    cookies: $("cookies").value,
    skip: $("opt-skip").checked,
    json: $("opt-json").checked,
    thumb: $("opt-thumb").checked,
    update: $("opt-update").checked,
  };
  remember(url);
  const started = await host("startDownload", JSON.stringify(payload));
  if (started == null || started === "missing") {
    appendLog(started === "missing"
      ? "yt-dlp.exe não foi encontrado ao lado do programa."
      : "Abra o TokBatch Studio para baixar com o yt-dlp.");
    window.onDownloadDone(false, "");
    if (started == null) setStatus("Pronto.", "");
  }
});

async function loadConfig() {
  const info = await host("appInfo");
  let data = null;
  if (info) {
    try { data = JSON.parse(info); } catch (e) { data = null; }
  }
  $("cfg-ytdlp").textContent = data ? data.ytdlp : "Disponível no aplicativo";
  $("cfg-dest").textContent = destEl.value;
}

destEl.addEventListener("change", () => {
  $("cfg-dest").textContent = destEl.value;
});
