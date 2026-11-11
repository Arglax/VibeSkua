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
#mvs-script .bt.moon { font-size: 14px; }
#mvs-script.botonly .bt.moon { background: #8A6A2E; color: #1A140E; }
#mvs-botonly { position: fixed; inset: 0; z-index: 25; background: #0B0907; display: flex; align-items: center;
  justify-content: center; color: #EDE3CC; font: 14px/1.4 system-ui, sans-serif; user-select: none; -webkit-user-select: none; }
#mvs-botonly .card { width: min(420px, 86vw); padding: 22px 22px 18px; border-radius: 14px; text-align: center;
  background: linear-gradient(#1E1711, #15100B); border: 1px solid #8A6A2E; box-shadow: 0 6px 24px rgba(0,0,0,.6); }
#mvs-botonly .ic { font-size: 34px; color: #E8C15A; line-height: 1; }
#mvs-botonly h1 { margin: 8px 0 2px; font: 700 20px system-ui, sans-serif; color: #E8C15A; }
#mvs-botonly .sub { color: #9A8F7A; font-size: 13px; }
#mvs-botonly .st { margin: 14px 0 4px; font-weight: 600; }
#mvs-botonly .st .dot { display: inline-block; width: 9px; height: 9px; border-radius: 50%; background: #7CFC7C; margin-right: 6px; }
#mvs-botonly .st.off .dot { background: #9A8F7A; }
#mvs-botonly .st.paused .dot { background: #FFB347; }
#mvs-botonly .wh { color: #C8B89A; font-size: 13px; min-height: 18px; }
#mvs-botonly .tk { color: #EDE3CC; font-size: 13px; margin-top: 6px; min-height: 36px; display: -webkit-box; -webkit-line-clamp: 2;
  -webkit-box-orient: vertical; overflow: hidden; word-break: break-word; }
#mvs-botonly button { margin-top: 14px; width: 100%; height: 48px; border-radius: 10px; border: 1px solid #C9A447;
  background: linear-gradient(#E8C15A, #B8902F); color: #1A140E; font: 700 16px system-ui, sans-serif; }
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
#mvs-menu button.mvs-add { border-color: #C9A447; }
.mvs-tag { display: inline-block; margin-left: 5px; padding: 1px 4px 0; border-radius: 4px; vertical-align: super;
  font: 800 9px/1.1 system-ui, sans-serif; letter-spacing: .4px; color: #1A140E;
  background: linear-gradient(#F3D27A, #C9963A); border: 1px solid #FFE9A8; box-shadow: 0 0 4px rgba(232,193,90,.6); }
#mvs-heat { position: fixed; z-index: 35; top: 8px; left: 50%; transform: translateX(-50%); max-width: min(560px, 86vw);
  display: flex; gap: 9px; align-items: flex-start; padding: 8px 12px; border-radius: 10px; color: #F2E8D2;
  font: 13px/1.35 system-ui, sans-serif; box-shadow: 0 3px 12px rgba(0,0,0,.55); user-select: none; -webkit-user-select: none;
  background: rgba(60, 38, 8, 0.94); border: 1px solid #D89A2B; transition: opacity .4s; }
#mvs-heat.hot { background: rgba(70, 14, 10, 0.95); border-color: #FF6B4A; }
#mvs-heat.ok { background: rgba(14, 44, 22, 0.94); border-color: #5FBF6A; }
#mvs-heat .ic { font-size: 18px; line-height: 1; flex: none; margin-top: 1px; }
#mvs-heat b { color: #FFD27A; }
#mvs-heat.hot b { color: #FF9C80; }
#mvs-heat.ok b { color: #9BE59F; }
#mvs-heat .x { flex: none; margin-left: 4px; color: #C8B89A; font-weight: 700; }
#mvs-drops { position: fixed; z-index: 38; left: 50%; top: 50%; transform: translate(-50%, -50%); width: min(760px, 96vw);
  height: min(520px, 92vh); display: flex; flex-direction: column; background: rgba(22,17,12,.96); border: 1px solid #8A6A2E;
  border-radius: 14px; box-shadow: 0 4px 18px rgba(0,0,0,.6); color: #F2E8D2; font: 14px system-ui, sans-serif;
  user-select: none; -webkit-user-select: none; }
#mvs-drops .hd, #mvs-drops .ft { display: flex; align-items: center; gap: 8px; padding: 8px 10px 8px 14px; flex-wrap: wrap; }
#mvs-drops .hd { border-bottom: 1px solid rgba(138,106,46,.5); }
#mvs-drops .ft { border-top: 1px solid rgba(138,106,46,.5); }
#mvs-drops .tt { font: 700 18px system-ui; color: #E8C15A; flex: 1; min-width: 120px; }
#mvs-drops .tt small { font: 500 12px system-ui; color: #C8B89A; margin-left: 6px; }
#mvs-drops .grow { flex: 1; color: #C8B89A; font-size: 13px; }
#mvs-drops button { min-height: 42px; padding: 0 12px; border-radius: 9px; border: 1px solid #8A6A2E;
  background: linear-gradient(#3A2D1E, #2A2016); color: #F2E8D2; font: 600 14px system-ui; }
#mvs-drops button:active { filter: brightness(1.3); }
#mvs-drops button:disabled { opacity: .38; }
#mvs-drops button.ok { border-color: #4E9A4A; background: linear-gradient(#2F5A2A, #22421F); }
#mvs-drops button.no { border-color: #A0432E; background: linear-gradient(#5E2418, #43190F); }
#mvs-drops button.on { background: #5A4528; }
#mvs-drops button.sq { width: 42px; padding: 0; font-size: 18px; }
#mvs-drops .list { flex: 1; overflow-y: auto; padding: 8px 10px; display: grid; gap: 8px; align-content: start;
  grid-template-columns: 1fr; -webkit-overflow-scrolling: touch; }
#mvs-drops .row { min-width: 0; display: flex; align-items: center; gap: 9px; padding: 7px 7px 7px 9px; border-radius: 10px;
  background: rgba(255,255,255,.04); border: 1px solid rgba(138,106,46,.35); }
#mvs-drops .row.picked { border-color: #E8C15A; background: rgba(232,193,90,.12); }
#mvs-drops .ck { width: 26px; height: 26px; border-radius: 7px; border: 2px solid #8A6A2E; flex: none; display: flex;
  align-items: center; justify-content: center; color: #2A2016; font: 900 16px system-ui; }
#mvs-drops .picked .ck { background: #E8C15A; border-color: #E8C15A; }
#mvs-drops .ic { width: 40px; height: 40px; border-radius: 8px; flex: none; display: flex; align-items: center;
  justify-content: center; font-size: 20px; background: #2A2016; border: 2px solid #8A6A2E; }
#mvs-drops .ic.ac { border-color: #E8C15A; } #mvs-drops .ic.mem { border-color: #8E6BE0; }
#mvs-drops .nm { flex: 1; min-width: 0; }
#mvs-drops .nm .l1 { display: flex; align-items: center; min-width: 0; }
#mvs-drops .nm b { font: 600 15px system-ui; white-space: nowrap; overflow: hidden; text-overflow: ellipsis; min-width: 0; }
#mvs-drops .tag { flex: none; }
#mvs-drops .nm span { font-size: 12px; color: #C8B89A; }
#mvs-drops .nm .q { font: 700 13px monospace; color: #E8C15A; }
#mvs-drops .tag { font: 700 10px system-ui; padding: 1px 5px; border-radius: 4px; margin-left: 5px; vertical-align: 2px; }
#mvs-drops .tag.ac { background: #E8C15A; color: #2A2016; } #mvs-drops .tag.mem { background: #6B4BB0; color: #fff; }
#mvs-drops .ic.art { background: #2A2016 center/86% no-repeat; border: 2px solid #C9A447; }
#mvs-drops .acmark { font: italic 800 13px Georgia, serif; color: #E8C15A; margin-right: 5px; flex: none; }
#mvs-drops button.aqw { min-height: 38px; padding: 0 14px; border-radius: 7px; border: 2px solid #E8B04A; color: #fff;
  font: 700 14px system-ui; background: linear-gradient(#D8392A, #8E1A10); box-shadow: inset 0 1px 0 rgba(255,255,255,.25); }
#mvs-drops button.aqw.arm { background: linear-gradient(#FF7A3A, #B8400E); }
#mvs-drops button.blk { width: 40px; min-height: 38px; padding: 0; border-radius: 7px; border: 2px solid #E8B04A;
  background: linear-gradient(#D8392A, #8E1A10); color: #fff; font: 700 20px system-ui; }
#mvs-drops button.blk.arm { background: linear-gradient(#FF7A3A, #B8400E); font-size: 12px; }
#mvs-drops .warn { margin: 8px 10px 0; padding: 7px 10px; border-radius: 8px; background: rgba(70,38,8,.92);
  border: 1px solid #D89A2B; color: #FFD27A; font: 600 13px system-ui; }
#mvs-drops .empty { grid-column: 1 / -1; text-align: center; color: #9A8F7A; padding: 40px 10px; font-size: 15px; }
#mvs-drops .sw { display: inline-flex; align-items: center; gap: 8px; font: 600 13px system-ui; }
#mvs-drops .sw i { width: 38px; height: 22px; border-radius: 11px; background: #554836; position: relative; display: inline-block; }
#mvs-drops .sw i::after { content: ""; position: absolute; left: 3px; top: 3px; width: 16px; height: 16px; border-radius: 50%; background: #C8B89A; }
#mvs-drops .sw.yes i { background: #4E9A4A; } #mvs-drops .sw.yes i::after { left: auto; right: 3px; background: #fff; }
#mvs-drops .toast { position: absolute; left: 50%; bottom: 70px; transform: translateX(-50%); background: #5E2418;
  border: 1px solid #A0432E; padding: 8px 14px; border-radius: 9px; font-weight: 600; }
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
<button class="bt moon" title="Bot only: hide the game, draw nothing">\u263E</button><button class="bt min-b" title="Collapse">–</button><button class="bt hide-b" title="Hide until the next script">×</button></div>
<div class="bd"><div class="ln st"></div><div class="task"></div></div>`;
        const pos = store.get("scriptPos", null);
        el.style.left = (pos ? pos.x : 8) + "px";
        el.style.top = (pos ? pos.y : Math.round(window.innerHeight * 0.3)) + "px";
        if (store.get("scriptMin", false)) el.classList.add("min");
        el.querySelector(".moon").addEventListener("click", e => {
            e.stopPropagation();
            botOnly(!botOnlyOn, true);
        });
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
        // The task line only: the caller is the script's method name (CoreBots' "(Caller)"), not for players.
        scriptEl.querySelector(".task").innerHTML = m.task
            ? esc(m.task)
            : "No log lines yet";
        // Keep it on screen when the game area shrinks.
        const maxX = Math.max(0, window.innerWidth - scriptEl.offsetWidth), maxY = Math.max(0, window.innerHeight - scriptEl.offsetHeight);
        if (scriptEl.offsetLeft > maxX) scriptEl.style.left = maxX + "px";
        if (scriptEl.offsetTop > maxY) scriptEl.style.top = maxY + "px";
    }

    // ---------------- Bot only ----------------
    // The game keeps running at full speed but Ruffle draws nothing (window.__mvsNoDraw) and the game
    // canvas is hidden, so the GPU and the screen compositor have nothing to do. A plain static card
    // shows what the bot is doing; it changes at most once a second.
    let botOnlyOn = false, botEl = null;

    function botOnly(on, tellApp) {
        on = !!on;
        botOnlyOn = on;
        window.__mvsBotOnly = on;
        if (window.__mvsUpdateNoDraw) window.__mvsUpdateNoDraw(); else window.__mvsNoDraw = on;
        const p = getPlayer && getPlayer();
        if (p) p.style.visibility = on ? "hidden" : "visible";
        if (on && !botEl) {
            css();
            botEl = document.createElement("div");
            botEl.id = "mvs-botonly";
            botEl.innerHTML = `<div class="card"><div class="ic">\u263E</div><h1>Bot only</h1>
<div class="sub">The game is hidden and nothing is drawn, to save power and heat. The bot keeps running.</div>
<div class="st"><span class="dot"></span><span class="tx"></span></div><div class="wh"></div><div class="tk"></div>
<button>Show game</button></div>`;
            botEl.querySelector("button").addEventListener("click", () => botOnly(false, true));
            document.body.appendChild(botEl);
        }
        if (botEl) botEl.style.display = on ? "flex" : "none";
        // The card shows the same as the script window (and has Show game), so the window steps aside.
        if (scriptEl) { scriptEl.classList.toggle("botonly", on); scriptEl.style.visibility = on ? "hidden" : ""; }
        // Text, not a boolean: the app's event reader keeps only text and numbers.
        if (tellApp && window.__skuaExt) { try { window.__skuaExt("mvsBotOnly", [on ? "on" : "off"]); } catch (e) { } }
        renderBotOnly();
    }

    function renderBotOnly() {
        if (!botOnlyOn || !botEl) return;
        const m = last;
        const st = botEl.querySelector(".st");
        const running = m && m.run;
        st.classList.toggle("off", !running);
        st.classList.toggle("paused", !!(running && m.status !== "Running"));
        const secs = running ? m.secs + (Date.now() - lastAt) / 1000 : (m ? m.secs : 0);
        botEl.querySelector(".tx").textContent = m
            ? (m.name || "Script") + " \u00B7 " + (running ? m.status + " " + fmt(secs) : "Finished")
            : "No script running";
        botEl.querySelector(".wh").textContent = m && m.where ? m.where : "";
        botEl.querySelector(".tk").textContent = m && m.task ? m.task : "";
    }

    function scriptStatus(msg) {
        last = msg;
        lastAt = Date.now();
        render();
        renderBotOnly();
        if (!clock) clock = setInterval(() => { if (last && last.run) { render(); renderBotOnly(); } }, 1000);
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
            : labels.includes("View Drops") ? "Monster"
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
        // MVS additions, tagged so they read as MVS's rather than the game's: the aura overlay for
        // whoever this menu is about (you, or your target).
        if (!given && (title === "You" || title === "Monster" || title === "Player") && window.MvsAuras &&
            !(window.__mvsOpts && window.__mvsOpts.mvsAuras === false)) {
            const b = document.createElement("button");
            b.className = "mvs-add";
            b.innerHTML = `Show Auras<sup class="mvs-tag">MVS</sup>`;
            b.addEventListener("click", e => {
                e.stopPropagation();
                closeMenu(true);
                window.MvsAuras.show(title === "You" ? "me" : "t");
            });
            const close = grid.querySelector("button.close");
            grid.insertBefore(b, close || null);
        }
        pn.appendChild(grid);
        pn.addEventListener("click", e => e.stopPropagation());
        menuEl.appendChild(pn);
        // A tap beside the panel closes the game's menu too.
        menuEl.addEventListener("click", () => closeMenu(true));
        document.body.appendChild(menuEl);
    }

    // ---------------- Heat warning ----------------
    // From bridge.js setThermal (ThermalMonitor.cs): warns once when the phone gets warm and again if it
    // gets hot, so a lower frame rate is expected rather than a surprise. A tap hides it.
    let heatEl = null, heatShown = "ok", heatDismissed = "ok", heatTimer = 0;
    const heatRank = { ok: 0, warm: 1, hot: 2 };

    function hideHeat() {
        clearTimeout(heatTimer);
        if (heatEl) { heatEl.remove(); heatEl = null; }
    }

    function showHeat(kind, html, autoHideMs) {
        css();
        hideHeat();
        heatEl = document.createElement("div");
        heatEl.id = "mvs-heat";
        heatEl.className = kind;
        heatEl.innerHTML = `<span class="ic">${kind === "ok" ? "\u2714" : "\u{1F321}"}</span><span>${html}</span><span class="x">\u2715</span>`;
        heatEl.addEventListener("click", () => { heatDismissed = heatShown; hideHeat(); });
        document.body.appendChild(heatEl);
        if (autoHideMs) heatTimer = setTimeout(hideHeat, autoHideMs);
    }

    function heat(m) {
        const kind = m.heat || "ok";
        const temp = Number(m.battery) > 0 ? ` (${Number(m.battery).toFixed(1)}\u00B0C)` : "";
        if (kind === "ok") {
            if (heatShown !== "ok" && m.battery !== undefined)
                showHeat("ok", "<b>Phone cooled down.</b> Graphics are back to your settings.", 5000);
            else if (m.battery === undefined) hideHeat();
            heatShown = "ok"; heatDismissed = "ok";
            return;
        }
        // Warm by Android's forecast while the battery is still cool: say it is heating up.
        const warmLabel = Number(m.battery) >= 40.5 ? `Phone is warm${temp}.` : "Phone is heating up.";
        if (kind === heatShown && heatEl) {
            heatEl.querySelector("b").textContent = kind === "hot" ? `Phone is hot${temp}.` : warmLabel;
            return;
        }
        const worse = heatRank[kind] > heatRank[heatShown];
        heatShown = kind;
        if (!worse && heatRank[heatDismissed] >= heatRank[kind]) return;
        if (kind === "hot")
            showHeat("hot", `<b>Phone is hot${temp}.</b> ` + (m.lowered ? "Cooler mode is on: lighter graphics and steadier clocks until it is back under 45\u00B0C. " : "") +
                "Expect a lower frame rate. A short break, unplugging the charger or taking off the case helps.");
        else
            showHeat("warm", `<b>${warmLabel}</b> Still full speed; at 47\u00B0C MVS switches to a cooler mode and the frame rate may drop.`, 12000);
    }

    // ---------------- Drops ----------------
    // skua.swf (DropsUI) keeps drops that need accepting in the game's own drop store instead of popping
    // them up, and sends the waiting list ("mvsDrops") whenever it changes. This panel lists them with
    // big Keep / Decline buttons, a select mode for doing several at once, Keep all / Decline all, an
    // auto-decline list (names declined as they drop) and "always keep AC items". Keep and Decline run
    // the game's own Yes / No (mvsDropAccept / mvsDropDecline), so bag-space checks stay the game's.
    let drops = [], dropsEl = null, dropsSelect = false, dropsPicked = new Set(), dropsView = "list", declineArm = 0;
    const blocked = new Set(store.get("dropsBlocked", []));
    let keepAC = store.get("dropsKeepAC", false);
    let askDecline = store.get("dropsAskDecline", false);
    let armed = "";                  // "decline:<id>" / "block:<id>" waiting for the second tap
    let armTimer = 0;
    const dropArt = {};              // icon art name -> data URL ("" when the game can't draw it)

    // The item's own art, drawn by skua.swf (the aura overlay's icon call draws any game art).
    function artUrl(name) {
        if (!name) return "";
        if (name in dropArt) return dropArt[name];
        let url = "";
        try {
            const v = String(ruffle().callExternalInterface("mvsAuraIcon", name, 64) || "");
            const i = v.indexOf(":");
            if (i > 0) {
                const size = +v.slice(0, i), bin = atob(v.slice(i + 1));
                const c = document.createElement("canvas");
                c.width = c.height = size;
                const ctx = c.getContext("2d"), img = ctx.createImageData(size, size);
                for (let p = 0; p + 3 < bin.length; p += 4) {
                    img.data[p] = bin.charCodeAt(p + 1); img.data[p + 1] = bin.charCodeAt(p + 2);
                    img.data[p + 2] = bin.charCodeAt(p + 3); img.data[p + 3] = bin.charCodeAt(p);
                }
                ctx.putImageData(img, 0, 0);
                url = c.toDataURL();
            }
        } catch (e) { url = ""; }
        dropArt[name] = url;
        return url;
    }

    function arm(key) {
        clearTimeout(armTimer);
        armed = key;
        armTimer = setTimeout(() => { armed = ""; if (dropsEl) drawDrops(); }, 3000);
    }
    const typeGlyph = t => /sword|axe|dagger|mace|polearm|staff|wand|gun|bow|whip|gauntlet/i.test(t) ? "\u2694"
        : /armor|class/i.test(t) ? "\u{1F6E1}" : /helm/i.test(t) ? "\u{1FA96}" : /cape/i.test(t) ? "\u{1F9E3}"
        : /pet/i.test(t) ? "\u{1F43E}" : /house|floor|wall/i.test(t) ? "\u{1F3E0}" : "\u25C6";

    function dropsCall(fn, id) {
        try { return ruffle().callExternalInterface(fn, id); } catch (e) { return ""; }
    }

    // The game's own drop settings, one key at a time (nothing is dumped).
    function gameGet(path) {
        try {
            const v = ruffle().callExternalInterface("getGameObject", path);
            return v == null || v === "null" ? null : JSON.parse(v);
        } catch (e) { return null; }
    }
    function gameSet(path, v) {
        try { ruffle().callExternalInterface("setGameObject", path, v); return true; } catch (e) { return false; }
    }
    function gameFlush() {
        try { ruffle().callExternalInterface("callGameFunction", "litePreference.flush"); } catch (e) { }
    }
    // The game's "Item Drops Block List" (litePreference.data.blackList: [{label, value: ItemID}]):
    // the game throws those drops away as the drop packet arrives (Game.as), before anything is drawn
    // or sent here. Blocking adds to it; older name-only entries here are moved over as they drop.
    function nativeBlockList() {
        const l = gameGet("litePreference.data.blackList");
        return Array.isArray(l) ? l : [];
    }
    function blockNative(d) {
        const list = nativeBlockList();
        if (list.some(b => Number(b && b.value) === d.id)) return;
        list.push({ label: String(d.n).toUpperCase(), value: d.id });
        if (gameSet("litePreference.data.blackList", list)) gameFlush();
    }
    function unblockNative(name) {
        const up = String(name).toUpperCase(), list = nativeBlockList();
        const left = list.filter(b => String(b && b.label).toUpperCase() !== up);
        if (left.length !== list.length && gameSet("litePreference.data.blackList", left)) gameFlush();
    }
    function blockedNames() {
        const names = new Set(blocked);
        for (const b of nativeBlockList()) if (b && b.label) names.add(String(b.label).toLowerCase());
        return [...names];
    }
    // "Ask before declining" is the game's own "Warn When Declining" (dOptions.warnDecline).
    function readWarnDecline() {
        const w = gameGet("litePreference.data.dOptions.warnDecline");
        if (w === true || w === false) askDecline = w;
    }
    function setWarnDecline(on) {
        askDecline = on;
        if (gameSet("litePreference.data.dOptions.warnDecline", on)) gameFlush();
        saveRules();
    }
    // Decline all: the game's own clear (customDrops.onUpdate empties its drop list in one go).
    function declineAll() {
        try {
            if (ruffle().callExternalInterface("callGameFunction", "cDropsUI.onUpdate") !== "null") return;
        } catch (e) { }
        decline(drops.map(d => d.id));
    }

    function keep(ids) {
        let full = false;
        for (const id of ids) if (dropsCall("mvsDropAccept", id) === "full") full = true;
        // After the redraw that follows a tap, or it would be wiped with the old contents.
        if (full) setTimeout(() => dropsToast("Your bag is full: make room, then tap Keep again."), 0);
    }

    function decline(ids) {
        for (const id of ids) dropsCall("mvsDropDecline", id);
    }

    function saveRules() {
        store.set("dropsBlocked", [...blocked]);
        store.set("dropsKeepAC", keepAC);
        store.set("dropsAskDecline", askDecline);
    }

    // New list from the game: apply the auto rules, then redraw.
    function dropsList(args) {
        let v = args;
        while (Array.isArray(v)) v = v[0];
        try { drops = JSON.parse(String(v || "[]")); } catch (e) { drops = []; }
        const auto = [], autoKeep = [];
        for (const d of drops) {
            if (blocked.has(d.n.toLowerCase())) { auto.push(d.id); blockNative(d); }
            else if (keepAC && d.ac) autoKeep.push(d.id);
        }
        if (auto.length) decline(auto);
        if (autoKeep.length) keep(autoKeep);
        const ids = new Set(drops.map(d => d.id));
        for (const id of [...dropsPicked]) if (!ids.has(id)) dropsPicked.delete(id);
        if (dropsEl) drawDrops();
    }

    function dropsToast(text) {
        if (!dropsEl) return;
        const old = dropsEl.querySelector(".toast");
        if (old) old.remove();
        const t = document.createElement("div");
        t.className = "toast";
        t.textContent = text;
        dropsEl.appendChild(t);
        setTimeout(() => t.remove(), 3500);
    }

    function closeDrops() {
        if (dropsEl) { dropsEl.remove(); dropsEl = null; }
        dropsSelect = false; dropsPicked.clear(); dropsView = "list";
    }

    function toggleDrops() {
        if (dropsEl) { closeDrops(); return; }
        css();
        if (menuEl) closeMenu(true);
        dropsEl = document.createElement("div");
        dropsEl.id = "mvs-drops";
        dropsEl.addEventListener("click", onDropsClick);
        readWarnDecline();
        document.body.appendChild(dropsEl);
        drawDrops();
    }

    function drawDrops() {
        const n = drops.length, picked = dropsPicked.size;
        if (dropsView === "blocked") {
            const names = blockedNames().sort();
            dropsEl.innerHTML = `<div class="hd"><div class="tt">Auto-decline list<small>${names.length} name${names.length === 1 ? "" : "s"}, declined as soon as they drop</small></div>
                <button data-a="back">\u2190 Back</button><button class="sq" data-a="close">\u2715</button></div>
                <div class="list">${names.length ? names.map(nm => `<div class="row"><div class="nm"><b>${esc(nm)}</b></div>
                <button class="no" data-a="unblock" data-n="${esc(nm)}">Remove</button></div>`).join("")
                : `<div class="empty">Nothing here yet. In Select mode, pick drops and tap \u201CAlways decline\u201D.</div>`}</div>`;
            return;
        }
        // Like the game's own drop card: the item's art, name and count, Accept / Decline / Preview and Block.
        const rows = drops.map(d => {
            const art = artUrl(d.i);
            const icon = art ? `<div class="ic art" style="background-image:url(${art})"></div>`
                : `<div class="ic${d.ac ? " ac" : d.mem ? " mem" : ""}">${typeGlyph(d.t)}</div>`;
            const dArm = armed === "decline:" + d.id, bArm = armed === "block:" + d.id;
            return `<div class="row${dropsPicked.has(d.id) ? " picked" : ""}" data-id="${d.id}">
            ${dropsSelect ? `<div class="ck">${dropsPicked.has(d.id) ? "\u2713" : ""}</div>` : ""}
            ${icon}
            <div class="nm"><div class="l1">${d.ac ? '<span class="acmark">AC</span>' : ""}<b>${esc(d.n)}</b>${d.mem ? '<span class="tag mem">MEMBER</span>' : ""}</div>
            <span>${esc(d.t)} \u00B7 <span class="q">\u00D7${d.q}</span></span></div>
            ${dropsSelect ? "" : `<button class="aqw" data-a="keep" data-id="${d.id}">Accept</button>
            <button class="aqw${dArm ? " arm" : ""}" data-a="decline" data-id="${d.id}">${dArm ? "Sure?" : "Decline"}</button>
            <button class="aqw" data-a="preview" data-id="${d.id}">Preview</button>
            <button class="blk${bArm ? " arm" : ""}" data-a="block" data-id="${d.id}" title="Always decline this item">${bArm ? "Sure?" : "\u2298"}</button>`}
            </div>`;
        }).join("");
        const over = drops.filter(d => d.q > 100).length;
        const warn = over ? `<div class="warn">\u26A0 ${over} drop${over === 1 ? " is" : "s are"} over the game\u2019s 100\u00D7 limit: keep or decline ${over === 1 ? "it" : "them"} before the game bugs out.</div>` : "";
        dropsEl.innerHTML = `<div class="hd"><div class="tt">Drops<small>${n} waiting \u00B7 kept here instead of popping up</small></div>
            <button class="${dropsSelect ? "on" : ""}" data-a="select" ${n ? "" : "disabled"}>\u2611 Select</button>
            <button class="ok" data-a="keepall" ${n ? "" : "disabled"}>\u2714 Keep all</button>
            <button class="no" data-a="declineall" ${n ? "" : "disabled"}>${declineArm ? "Tap again to decline " + n : "\u2716 Decline all"}</button>
            <button class="sq" data-a="close">\u2715</button></div>
            ${warn}<div class="list">${rows || '<div class="empty">No drops waiting. New ones land here instead of covering the game.</div>'}</div>
            <div class="ft"><span class="grow">${dropsSelect ? picked + " selected" : "Tip: Select lets you keep or decline several at once."}</span>
            ${dropsSelect ? "" : `<span class="sw${askDecline ? " yes" : ""}" data-a="askdecline"><i></i>Ask before declining</span>
            <span class="sw${keepAC ? " yes" : ""}" data-a="keepac"><i></i>Always keep AC items</span>
            <button data-a="blocked">\u26D4 Auto-decline list (${blockedNames().length})</button>`}
            ${dropsSelect ? `<button data-a="blocksel" ${picked ? "" : "disabled"}>\u26D4 Always decline</button>
            <button class="ok" data-a="keepsel" ${picked ? "" : "disabled"}>\u2714 Keep ${picked}</button>
            <button class="no" data-a="declinesel" ${picked ? "" : "disabled"}>\u2716 Decline ${picked}</button>` : ""}</div>`;
    }

    function onDropsClick(e) {
        e.stopPropagation();
        const el = e.target.closest("[data-a], .row[data-id]");
        if (!el) return;
        const a = el.dataset.a, id = Number(el.dataset.id);
        if (a !== "declineall") declineArm = 0;
        switch (a) {
            case "close": closeDrops(); return;
            case "keep": keep([id]); break;
            case "decline":
                if (askDecline && armed !== "decline:" + id) { arm("decline:" + id); break; }
                armed = ""; decline([id]);
                break;
            case "preview":
                // The game's own preview window; the panel would cover it.
                dropsCall("mvsDropPreview", id);
                closeDrops();
                return;
            case "block": {
                if (askDecline && armed !== "block:" + id) { arm("block:" + id); break; }
                armed = "";
                const d = drops.find(x => x.id === id);
                if (d) { blocked.add(d.n.toLowerCase()); saveRules(); blockNative(d); }
                decline([id]);
                break;
            }
            case "askdecline": setWarnDecline(!askDecline); break;
            case "select": dropsSelect = !dropsSelect; dropsPicked.clear(); break;
            case "keepall": keep(drops.map(d => d.id)); break;
            case "declineall":
                // Two taps, so one stray tap cannot throw everything away.
                if (!declineArm) { declineArm = setTimeout(() => { declineArm = 0; if (dropsEl) drawDrops(); }, 3000); break; }
                clearTimeout(declineArm); declineArm = 0;
                declineAll();
                break;
            case "keepsel": keep([...dropsPicked]); dropsPicked.clear(); break;
            case "declinesel": decline([...dropsPicked]); dropsPicked.clear(); break;
            case "blocksel":
                for (const d of drops) if (dropsPicked.has(d.id)) { blocked.add(d.n.toLowerCase()); blockNative(d); }
                saveRules();
                decline([...dropsPicked]); dropsPicked.clear();
                break;
            case "keepac":
                keepAC = !keepAC; saveRules();
                if (keepAC) keep(drops.filter(d => d.ac).map(d => d.id));
                break;
            case "blocked": dropsView = "blocked"; break;
            case "back": dropsView = "list"; break;
            case "unblock": blocked.delete(el.dataset.n); saveRules(); unblockNative(el.dataset.n); break;
            default:
                // A row tap in select mode picks it.
                if (dropsSelect && el.classList.contains("row")) {
                    if (dropsPicked.has(id)) dropsPicked.delete(id); else dropsPicked.add(id);
                } else return;
        }
        if (dropsEl) drawDrops();
    }

    window.MvsOverlays = {
        init(playerGetter) { getPlayer = playerGetter; },
        scriptStatus,
        menu,
        heat,
        dropsList,
        toggleDrops,
        botOnly,
        isBotOnly: () => botOnlyOn
    };
})();
