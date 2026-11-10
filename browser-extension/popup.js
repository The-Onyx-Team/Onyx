const api = globalThis.browser ?? globalThis.chrome;

function show(text, cls) {
  const el = document.getElementById("status");
  el.textContent = text;
  el.className = cls;
}

api.runtime.sendMessage({ type: "status" }).then((state) => {
  if (state.collector) {
    show("Connected. Websites are recorded.", "ok");
  } else if (state.host) {
    show("The Onyx app is not running. Start it (or onyx-agent) to record websites.", "warn");
  } else {
    show("Website recording is off on this computer. Turn it on in the Onyx app under Devices, or run 'onyx-agent websites on'.", "warn");
  }
  document.getElementById("domain").textContent = state.domain ? `Current website: ${state.domain}` : "";
});
