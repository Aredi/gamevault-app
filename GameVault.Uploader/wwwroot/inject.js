// Added to the GameVault web page by the reverse proxy (see the README): on a game's page, an "Install on my PC"
// button that opens the SanctuaryVault page with that game. Only the address is read, not the page itself.
(() => {
  "use strict";
  const base = new URL(".", document.currentScript.src).pathname;
  const button = document.createElement("a");
  button.id = "sanctuary-install";
  button.textContent = "⬇ Installer sur mon PC";
  Object.assign(button.style, {
    position: "fixed", right: "24px", bottom: "24px", zIndex: "2147483000", padding: "12px 18px",
    borderRadius: "999px", background: "#7C5CFF", color: "#fff", textDecoration: "none",
    font: "600 14px Inter, system-ui, sans-serif", boxShadow: "0 6px 24px rgba(0, 0, 0, .35)",
  });
  const update = () => {
    const game = /^\/library\/(\d+)/.exec(location.pathname);
    if (!game) { button.remove(); return; }
    button.href = base + "?install=" + game[1];
    if (!button.isConnected) document.body.append(button);
  };
  // The page changes its address without loading: follow it
  for (const name of ["pushState", "replaceState"]) {
    const original = history[name];
    history[name] = function (...args) { const result = original.apply(this, args); update(); return result; };
  }
  addEventListener("popstate", update);
  if (document.readyState === "loading") document.addEventListener("DOMContentLoaded", update); else update();
})();
