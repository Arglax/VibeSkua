// MVS overlays drawn by the page over the game (app controls cannot cover the WebView):
//  - the script window: which script runs, for how long, status, place and latest log line
//    (bridge "scriptStatus" messages from ScriptStatusOverlay.cs);
//  - big menus: when the game opens one of its small click menus (your portrait, another player),
//    the same entries as large buttons (skua.swf reports it with "mvsMenu" and replays the tap on
//    the game's own entry with mvsMenuClick, so the game does exactly what its menu does).
// Everything here is local to this screen.
(function () {
    "use strict";

    let getPlayer = () => null;
    const store = {
        get(k, d) { try { const v = localStorage.getItem("mvs." + k); return v === null ? d : JSON.parse(v); } catch (e) { return d; } },
        set(k, v) { try { localStorage.setItem("mvs." + k, JSON.stringify(v)); } catch (e) { } }
    };

    function css() {
        if (document.getElementById("mvs-style")) return;
        const s = document.createElement("style");
        s.id = "mvs-style";
        s.textContent = `
#mvs-script { position: fixed; z-index: 30; min-width: 150px; max-width: 46vw; color: #EDE3CC;
  background: rgba(22, 17, 12, 0.88); border: 1px solid #8A6A2E; border-radius: 8px;
  font: 12px/1.35 system-ui, sans-serif; box-shadow: 0 2px 10px rgba(0,0,0,.5); user-select: none;
  -webkit-user-select: none; touch-action: none; }
#mvs-script .hd { display: flex; align-items: center; gap: 6px; padding: 5px 6px 5px 8px; }
#mvs-script .dot { width: 8px; height: 8px; border-radius: 50%; background: #7CFC7C; flex: none; }
#mvs-script.off .dot { background: #9A8F7A; }
#mvs-script.stopping .dot { background: #FFB347; }
#mvs-script .nm { font-weight: 700; color: #E8C15A; white-space: nowrap; overflow: hidden; text-overflow: ellipsis; flex: 1; min-width: 0; }
#mvs-script .tm { font: 700 12px monospace; color: #EDE3CC; flex: none; }
#mvs-script .bt { flex: none; width: 24px; height: 22px; border: 0; border-radius: 5px; background: rgba(255,255,255,.08);
  color: #E8C15A; font: 700 13px system-ui, sans-serif; padding: 0; }
#mvs-script .bd { padding: 0 8px 7px; border-top: 1px solid rgba(138,106,46,.45); }
#mvs-script .ln { margin-top: 5px; color: #C8B89A; white-space: nowrap; overflow: hidden; text-overflow: ellipsis; }
#mvs-script .ln b { color: #EDE3CC; font-weight: 600; }
#mvs-script .task { margin-top: 3px; color: #EDE3CC; display: -webkit-box; -webkit-line-clamp: 2; -webkit-box-orient: vertical; overflow: hidden; word-break: break-word; }
#mvs-script .task i { color: #E8C15A; font-style: normal; }
#mvs-script.min .bd { display: none; }
#mvs-menu { position: fixed; inset: 0; z-index: 40; display: flex; align-items: center; justify-content: center;
  background: rgba(0,0,0,.35); }
#mvs-menu .pn { min-width: 230px; max-width: 80vw; max-height: 92vh; overflow-y: auto; padding: 10px;
  background: rgba(22, 17, 12, 0.96); border: 1px solid #8A6A2E; border-radius: 12px; box-shadow: 0 4px 18px rgba(0,0,0,.6); }
#mvs-menu .tt { color: #E8C15A; font: 700 14px system-ui, sans-serif; margin: 0 4px 8px; letter-spacing: .3px; text-align: center; }
#mvs-menu .gr { display: grid; grid-template-columns: 1fr; gap: 7px; }
#mvs-menu .gr.two { grid-template-columns: 1fr 1fr; }
#mvs-menu button { min-height: 44px; padding: 0 16px; border-radius: 9px; border: 1px solid #8A6A2E;
  background: linear-gradient(#3A2D1E, #2A2016); color: #F2E8D2; font: 600 16px system-ui, sans-serif; text-align: center; }
#mvs-menu button:active { background: #5A4528; }
#mvs-menu button:disabled { opacity: .38; }
#mvs-menu button.close { color: #C8B89A; font-weight: 500; }
`;
        document.head.appendChild(s);
    }

    // ---------------- Script window ----------------
    let scriptEl = null;
    let last = null;          // last message
    let lastAt = 0;           // when it arrived
    let clock = 0;
    let hiddenRun = store.get("scriptHiddenRun", -1);

    function fmt(secs) {
        secs = Math.max(0, Math.floor(secs));
        const h = Math.floor(secs / 3600), m = Math.floor(secs / 60) % 60, s = secs % 60;
        const p = n => String(n).padStart(2, "0");
        return (h > 0 ? h + ":" : "") + p(m) + ":" + p(s);
    }

    function esc(t) { return String(t || "").replace(/[&<>"]/g, c => ({ "&": "&amp;", "<": "&lt;", ">": "&gt;", '"': "&quot;" }[c])); }

    function makeScriptEl() {
        css();
        const el = document.createElement("div");
        el.id = "mvs-script";
        el.innerHTML = `<div class="hd"><span class="dot"></span><span class="nm"></span><span class="tm"></span>
<button class="bt min-b" title="Collapse">–</button><button class="bt hide-b" title="Hide until the next script">×</button></div>
<div class="bd"><div class="ln st"></div><div class="task"></div></div>`;
        const pos = store.get("scriptPos", null);
        el.style.left = (pos ? pos.x : 8) + "px";
        el.style.top = (pos ? pos.y : Math.round(window.innerHeight * 0.3)) + "px";
        if (store.get("scriptMin", false)) el.classList.add("min");
        el.querySelector(".min-b").addEventListener("click", e => {
            e.stopPropagation();
            el.classList.toggle("min");
            store.set("scriptMin", el.classList.contains("min"));
            render();
        });
        el.querySelector(".hide-b").addEventListener("click", e => {
            e.stopPropagation();
            hiddenRun = last ? last.id : -1;
            store.set("scriptHiddenRun", hiddenRun);
            render();
        });
        dragBy(el.querySelector(".hd"), el);
        document.body.appendChild(el);
        return el;
    }

    // Drag the window by its header; a short tap on the header toggles collapsed.
    function dragBy(handle, el) {
        let start = null;
        handle.addEventListener("pointerdown", e => {
            if (e.target.closest(".bt")) return;
            start = { x: e.clientX, y: e.clientY, l: el.offsetLeft, t: el.offsetTop, moved: false };
            handle.setPointerCapture(e.pointerId);
        });
        handle.addEventListener("pointermove", e => {
            if (!start) return;
            const dx = e.clientX - start.x, dy = e.clientY - start.y;
            if (!start.moved && Math.abs(dx) + Math.abs(dy) < 6) return;
            start.moved = true;
            const x = Math.min(Math.max(0, start.l + dx), window.innerWidth - el.offsetWidth);
            const y = Math.min(Math.max(0, start.t + dy), window.innerHeight - el.offsetHeight);
            el.style.left = x + "px";
            el.style.top = y + "px";
        });
        const end = () => {
            if (!start) return;
            if (start.moved) store.set("scriptPos", { x: el.offsetLeft, y: el.offsetTop });
            else { el.classList.toggle("min"); store.set("scriptMin", el.classList.contains("min")); render(); }
            start = null;
        };
        handle.addEventListener("pointerup", end);
        handle.addEventListener("pointercancel", () => { start = null; });
    }

    function render() {
        const m = last;
        const visible = m && m.show && m.id !== hiddenRun;
        if (!visible) {
            if (scriptEl) scriptEl.style.display = "none";
            return;
        }
        if (!scriptEl) scriptEl = makeScriptEl();
        scriptEl.style.display = "block";
        scriptEl.classList.toggle("off", !m.run);
        scriptEl.classList.toggle("stopping", m.run && m.status !== "Running");
        const secs = m.run ? m.secs + (Date.now() - lastAt) / 1000 : m.secs;
        scriptEl.querySelector(".nm").textContent = m.name || "Script";
        scriptEl.querySelector(".tm").textContent = fmt(secs);
        scriptEl.querySelector(".st").innerHTML = `<b>${esc(m.run ? m.status : "Finished, ran " + fmt(m.secs))}</b>${m.where ? " · " + esc(m.where) : ""}`;
        scriptEl.querySelector(".task").innerHTML = m.task || m.caller
            ? (m.caller ? `<i>${esc(m.caller)}</i> ` : "") + esc(m.task)
            : "No log lines yet";
        // Keep it on screen when the game area shrinks.
        const maxX = Math.max(0, window.innerWidth - scriptEl.offsetWidth), maxY = Math.max(0, window.innerHeight - scriptEl.offsetHeight);
        if (scriptEl.offsetLeft > maxX) scriptEl.style.left = maxX + "px";
        if (scriptEl.offsetTop > maxY) scriptEl.style.top = maxY + "px";
    }

    function scriptStatus(msg) {
        last = msg;
        lastAt = Date.now();
        render();
        if (!clock) clock = setInterval(() => { if (last && last.run) render(); }, 1000);
    }

    // ---------------- Big menus ----------------
    let menuEl = null;

    function ruffle() { const p = getPlayer(); return p && p.ruffle(); }

    function closeMenu(tellGame) {
        if (menuEl) { menuEl.remove(); menuEl = null; }
        if (tellGame) { try { ruffle().callExternalInterface("mvsMenuClose"); } catch (e) { } }
    }

    function menu(args) {
        let v = args;
        while (Array.isArray(v)) v = v[0];
        // A click menu comes as [{i, t, on}]; the bottom bar's Char / Quests list as {title, items}.
        let items = [], given = "";
        try {
            const parsed = v ? JSON.parse(String(v)) : [];
            if (Array.isArray(parsed)) items = parsed;
            else if (parsed && Array.isArray(parsed.items)) { items = parsed.items; given = String(parsed.title || ""); }
        } catch (e) { items = []; }
        if (!items.length) { closeMenu(false); return; }
        css();
        closeMenu(false);
        const labels = items.map(i => String(i.t));
        const title = given ? given
            : labels.includes("Char Page") && labels.includes("Reputation") ? "You"
            : labels.some(t => /whisper|friend|party|go ?to/i.test(t)) ? "Player" : "Menu";
        menuEl = document.createElement("div");
        menuEl.id = "mvs-menu";
        const pn = document.createElement("div");
        pn.className = "pn";
        pn.innerHTML = `<div class="tt">${esc(title)}</div>`;
        const grid = document.createElement("div");
        grid.className = "gr" + (items.length > 6 ? " two" : "");
        for (const it of items) {
            const b = document.createElement("button");
            b.textContent = it.t;
            b.disabled = !it.on;
            if (/^close$/i.test(it.t)) b.className = "close";
            b.addEventListener("click", e => {
                e.stopPropagation();
                try { ruffle().callExternalInterface("mvsMenuClick", it.i); } catch (err) { }
                closeMenu(false);
            });
            grid.appendChild(b);
        }
        pn.appendChild(grid);
        pn.addEventListener("click", e => e.stopPropagation());
        menuEl.appendChild(pn);
        // A tap beside the panel closes the game's menu too.
        menuEl.addEventListener("click", () => closeMenu(true));
        document.body.appendChild(menuEl);
    }

    window.MvsOverlays = {
        init(playerGetter) { getPlayer = playerGetter; },
        scriptStatus,
        menu
    };
})();
