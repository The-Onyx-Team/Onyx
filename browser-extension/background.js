// Onyx Websites: tells the Onyx collector on this computer which website the focused browser window shows.
//
// Only the host name of the active tab of a focused window leaves the browser, and only to the native
// messaging host "com.onyx_team.onyx.browser", which the Onyx app or onyx-agent registers when website recording
// is switched on. The collector decides whether to record it (it knows whether the browser is in front and
// whether the user is idle) and the server stores the domain only. Private windows are never reported.

const api = globalThis.browser ?? globalThis.chrome;
const HOST = "com.onyx_team.onyx.browser";

let port = null;
// What the popup shows.
const state = { host: false, collector: false, domain: null, error: null };

function connect() {
  try {
    port = api.runtime.connectNative(HOST);
  } catch (e) {
    port = null;
    state.host = false;
    state.error = String(e?.message ?? e);
    return;
  }

  state.host = true;
  state.error = null;
  port.onMessage.addListener((message) => {
    if (typeof message?.collector === "boolean") state.collector = message.collector;
  });
  port.onDisconnect.addListener((p) => {
    // Not installed, or recording was switched off (the host's registration was removed).
    const error = p?.error ?? api.runtime.lastError;
    state.host = false;
    state.collector = false;
    state.error = error?.message ?? null;
    port = null;
  });
}

function domainOf(url) {
  try {
    const u = new URL(url);
    return u.protocol === "http:" || u.protocol === "https:" ? u.hostname : null;
  } catch {
    return null;
  }
}

async function currentState() {
  const win = await api.windows.getLastFocused({ populate: false }).catch(() => null);
  if (!win || !win.focused || win.incognito) return { focused: Boolean(win?.focused), domain: null };

  const [tab] = await api.tabs.query({ active: true, windowId: win.id });
  return { focused: true, domain: tab && !tab.incognito ? domainOf(tab.url ?? "") : null };
}

async function report() {
  const message = await currentState();
  state.domain = message.domain;

  if (!port) connect();
  if (!port) return;
  try {
    port.postMessage(message);
  } catch {
    port = null;
  }
}

api.tabs.onActivated.addListener(report);
api.tabs.onUpdated.addListener((_tabId, change, tab) => {
  if (tab.active && (change.url || change.status === "complete")) report();
});
api.windows.onFocusChanged.addListener(report);

// Repeat the state every minute, so the collector gets it again after a restart.
api.alarms.create("onyx-heartbeat", { periodInMinutes: 1 });
api.alarms.onAlarm.addListener((alarm) => {
  if (alarm.name === "onyx-heartbeat") report();
});

api.runtime.onStartup.addListener(report);
api.runtime.onInstalled.addListener(report);

api.runtime.onMessage.addListener((message, _sender, sendResponse) => {
  if (message?.type === "status") {
    report().finally(() => sendResponse({ ...state }));
    return true;
  }
  return false;
});

report();
