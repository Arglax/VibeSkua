// Aura overlay: the auras on you and on your target, with their icons, stacks, time left and what
// they do.
//
// Data comes from the game's own aura records (dataLeaf.hudAuras: stacks, icon and when each runs
// out, which its aura icons use; dataLeaf.auras: who cast it and when). Icons are drawn by skua.swf
// ("mvsAuraIcon") from the game's art. What an aura does and how many stacks it can reach come from
// auradata.json, built from the AQW Wiki's class pages (your class's skill texts that mention the
// aura); otherwise from the aura's description when the server sends one. Without a known limit the
// count is shown as "xN" (the most seen is in its details).
//
// Effects that matter most are marked: Decay and other heal-blocking effects in red; taunts and
// enrages in orange (a Scroll of Enrage puts Focus and Reckless on a monster). An aura at its limit glows.
//
// Tap an aura for its details. Drag a window's header to move it; tap the header (or "–") to collapse
// it to one line, "×" hides it behind a small button, "⇆" splits You and Target into two windows
// (or joins them back). Everything is remembered.
(() => {
    "use strict";
    if (window.__mvsAuraOverlay) return;
    window.__mvsAuraOverlay = true;

    const POLL_MS = 250, DRAW_MS = 100, DESC_MS = 3000, MAX_ROWS = 7, ICON = 40;
    const store = {
        get(k, d) { try { const v = localStorage.getItem("mvs." + k); return v === null ? d : JSON.parse(v); } catch (e) { return d; } },
        set(k, v) { try { localStorage.setItem("mvs." + k, JSON.stringify(v)); } catch (e) { } }
    };
    const SPECIAL = [
        { re: /decay|heal(ing)?\s*block|no\s*heal|anti-?heal|cauteri[sz]e/i, kind: "noheal", tag: "NO HEAL",
          note: "Healing is blocked or turned into damage while this lasts. Hold healing skills and potions until it ends." },
        // Scroll of Enrage (AQW Wiki): Focus for 6 s and Reckless for 10 s on the target.
        { re: /^focus$|taunt/i, kind: "enrage", tag: "TAUNT",
          note: "Forced to attack whoever taunted it (Scroll of Enrage: 6 seconds). Taunts don't work on players." },
        { re: /^reckless$/i, kind: "enrage", tag: "RECKLESS",
          note: "Attacks recklessly: all its outgoing damage is 15% lower (Scroll of Enrage: 10 seconds)." },
        { re: /enrage/i, kind: "enrage", tag: "ENRAGE",
          note: "Enraged: expect bigger hits until it ends." }
    ];

    let seen = store.get("auraSeen", {});
    let descs = {}, lastDesc = 0;
    let clockOffset = null;
    // Recent clock-offset samples, one per aura cast (name + cast time), so the median can be taken.
    const offSamples = new Map();
    const open = new Set();
    const icons = {};               // icon key -> data URL ("" when it can't be drawn)
    let iconBudget = 0;
    let wiki = null, wikiLoading = false;
    const wikiCache = {};           // class|aura -> { text, max, nocap }
    let state = { mine: [], theirs: [], target: null, me: "", cls: "" };

    function call(name, ...args) {
        try {
            const p = document.querySelector("ruffle-player");
            return p && p.ruffle ? p.ruffle().callExternalInterface(name, ...args) : null;
        } catch (e) {
            return null;
        }
    }

    function read(path) {
        const v = call("getGameObject", path);
        if (v == null) return null;
        try { return JSON.parse(v); } catch (e) { return null; }
    }

    function esc(t) { return String(t == null ? "" : t).replace(/[&<>"]/g, c => ({ "&": "&amp;", "<": "&lt;", ">": "&gt;", '"': "&quot;" }[c])); }

    // ---- what an aura does (wiki dataset) ----

    function loadWiki() {
        if (!wiki && window.__mvsAuraData) wiki = window.__mvsAuraData;   // handed in for testing
        if (wiki || wikiLoading) return;
        wikiLoading = true;
        fetch("auradata.json").then(r => r.ok ? r.json() : null).then(j => { wiki = j || { classes: {} }; })
            .catch(() => { wiki = { classes: {} }; });
    }

    function normClass(n) { return String(n || "").toLowerCase().replace(/\([^)]*\)/g, "").replace(/\s+/g, " ").trim(); }

    function limitIn(text) {
        const m = text.match(/up\s+to\s+(\d+)\s*(?:times|stacks)/i) || text.match(/max(?:imum)?(?:\s+of)?\s+(\d+)\s+stacks?/i)
            || text.match(/(\d+)\s+stacks?\s+max/i) || text.match(/stacks?\s+up\s+to\s+(\d+)/i);
        return m && +m[1] > 1 ? +m[1] : 0;
    }

    function wikiInfo(aura, cls) {
        const key = cls + "|" + aura;
        if (key in wikiCache) return wikiCache[key];
        if (!wiki) return null;
        const skills = wiki.classes[normClass(cls)] || [];
        let base = aura.replace(/^[IVXLC0-9]+\s*-\s*/, "");
        const reversed = /\(reversed\)/i.test(base);
        const core = base.replace(/\s*\(reversed\)\s*/i, "").replace(/^the\s+/i, "").trim();
        const coreRe = new RegExp("^(?:the\\s+)?" + core.replace(/[.*+?^${}()|[\]\\]/g, "\\$&") + "\\s*:\\s*(.+)$", "i");
        let text = "", from = "";
        // A table line in a passive ("Magician: 22% INT"), from the Reversed table for reversed auras.
        for (const s of skills) {
            if (/reversed/i.test(s.n) !== reversed) continue;
            for (const line of String(s.d).split("\n")) {
                const m = line.trim().match(coreRe);
                if (m) { text = m[1]; from = s.n; break; }
            }
            if (text) break;
        }
        // Otherwise the sentences of the class's skills that name the aura.
        let all = "";
        if (core.length > 2) {
            const nameRe = new RegExp("(?<![\\w])" + base.replace(/[.*+?^${}()|[\]\\]/g, "\\$&") + "(?![\\w])", "i");
            const hits = [];
            for (const s of skills) {
                for (const sent of String(s.d).replace(/\n/g, " ").split(/(?<=[.!?])\s+/)) {
                    if (nameRe.test(sent)) hits.push({ s: s.n, t: sent.trim() });
                }
            }
            all = hits.map(h => h.t).join(" ");
            // The sentence that applies the aura says what it does; others only use it.
            const says = h => (/\b(appl|grant|summon|gain|increas|reduc|decreas|restor|heal)/i.test(h.t) ? 0 : 1);
            hits.sort((a, b) => says(a) - says(b));
            if (!text && hits.length) { text = hits.slice(0, 2).map(h => h.t).join(" "); from = hits[0].s; }
        }
        const max = limitIn(all || text);
        const nocap = !max && /\b(each|per)\s+stack\b/i.test(all || text);
        const info = text ? { text, from, max, nocap } : null;
        wikiCache[key] = info;
        return info;
    }

    // Forge enhancement procs (Ravenous, Consumed, Valiance, Arcana's Concerto, Decay from Dauntless...):
    // { t: effects, max: stack limit, dur, on: "you"/"target", src: enhancement }.
    let enhIndex = null;
    function enhInfo(name, who) {
        if (!wiki || !wiki.auras) return null;
        if (!enhIndex) {
            enhIndex = {};
            for (const k of Object.keys(wiki.auras)) enhIndex[k.toLowerCase().replace(/[‘’]/g, "'")] = wiki.auras[k];
        }
        const e = enhIndex[String(name).toLowerCase().replace(/[‘’]/g, "'")];
        if (!e) return null;
        if (!Array.isArray(e)) return e;
        const on = who === "t" ? "target" : "you";
        return e.find(x => x.on === on) || e[0];
    }

    function limitFromDesc(desc) {
        if (!desc) return 0;
        return limitIn(String(desc).replace(/<[^>]*>/g, " "));
    }

    function special(name) {
        for (const s of SPECIAL) if (s.re.test(name)) return s;
        return null;
    }

    // ---- icons (drawn by skua.swf) ----

    function iconUrl(key) {
        if (!key) return "";
        if (key in icons) return icons[key];
        if (iconBudget <= 0) return "";
        iconBudget--;
        const r = call("mvsAuraIcon", key, ICON);
        let url = "";
        try {
            const v = typeof r === "string" ? r : "";
            const i = v.indexOf(":");
            if (i > 0) {
                const size = +v.slice(0, i);
                const bin = atob(v.slice(i + 1));
                const c = document.createElement("canvas");
                c.width = c.height = size;
                const ctx = c.getContext("2d");
                const img = ctx.createImageData(size, size);
                for (let p = 0, q = 0; p + 3 < bin.length; p += 4, q += 4) {
                    img.data[q] = bin.charCodeAt(p + 1);       // ARGB -> RGBA
                    img.data[q + 1] = bin.charCodeAt(p + 2);
                    img.data[q + 2] = bin.charCodeAt(p + 3);
                    img.data[q + 3] = bin.charCodeAt(p);
                }
                ctx.putImageData(img, 0, 0);
                url = c.toDataURL();
            }
        } catch (e) {
            url = "";
        }
        icons[key] = url;
        return url;
    }

    // ---- windows ----

    function css() {
        if (document.getElementById("mvs-aura-style")) return;
        const s = document.createElement("style");
        s.id = "mvs-aura-style";
        s.textContent = `
.mvs-aw { position: fixed; z-index: 31; width: 17em; font: calc(11px * var(--s, .72))/1.25 system-ui, sans-serif; color: #EDE3CC;
  background: rgba(22, 17, 12, 0.84); border: 1px solid #8A6A2E; border-radius: .8em; box-shadow: 0 2px 10px rgba(0,0,0,.5);
  user-select: none; -webkit-user-select: none; touch-action: none; max-height: 94vh; overflow: hidden; }
.mvs-aw .hd { display: flex; align-items: center; gap: .3em; padding: .25em .25em .25em .3em; }
.mvs-aw .grip { flex: none; width: 1.4em; text-align: center; color: #C9A447; font-size: 1.35em; line-height: 1; cursor: grab;
  opacity: .85; }
.mvs-aw .tt { flex: 1; min-width: 0; font-weight: 700; color: #E8C15A; font-size: 1.05em; white-space: nowrap; overflow: hidden; text-overflow: ellipsis; }
.mvs-aw .bt { flex: none; width: 2.1em; height: 1.9em; border: 0; border-radius: .45em; background: rgba(255,255,255,.08);
  color: #E8C15A; font: 700 1.1em system-ui, sans-serif; padding: 0; }
.mvs-aw .bd { padding: 0 .45em .45em; overflow-y: auto; max-height: calc(94vh - 3em); touch-action: pan-y; overscroll-behavior: contain; }
.mvs-aw.min .bd { display: none; }
.mvs-aw .sh { color: #C8B89A; font-weight: 700; font-size: .9em; letter-spacing: .04em; text-transform: uppercase; margin: .2em .2em .25em;
  white-space: nowrap; overflow: hidden; text-overflow: ellipsis; }
.mvs-aw .row { position: relative; margin-bottom: .25em; padding: .25em .4em .35em .25em; border-radius: .5em; background: rgba(0,0,0,.38);
  border: 1px solid rgba(138,106,46,.5); overflow: hidden; }
.mvs-aw .top { display: flex; align-items: center; gap: .4em; }
.mvs-aw .ic { flex: none; width: 2em; height: 2em; border-radius: .35em; background: #2A2016 center/contain no-repeat;
  display: flex; align-items: center; justify-content: center; color: #E8DCC0; font: 700 1em system-ui, sans-serif; }
.mvs-aw .nm { flex: 1; min-width: 0; white-space: nowrap; overflow: hidden; text-overflow: ellipsis; color: #E6DCC6; }
.mvs-aw .st { flex: none; font: 700 1.15em system-ui, sans-serif; color: #FFF; }
.mvs-aw .st small { font-size: .8em; color: #9A8F7A; font-weight: 600; }
.mvs-aw .tm { flex: none; min-width: 3em; text-align: right; font: 600 1em monospace; color: #C8B89A; }
.mvs-aw .tag { flex: none; padding: 0 .35em; border-radius: .35em; font-size: .8em; font-weight: 800; }
.mvs-aw .sum { color: #A89C84; font-size: .9em; margin: .15em 0 0 2.4em; white-space: nowrap; overflow: hidden; text-overflow: ellipsis; }
.mvs-aw .bar { position: absolute; left: 0; bottom: 0; height: .2em; background: #40A8C8; }
.mvs-aw .bar.low { background: #FF9A3C; }
.mvs-aw .row.noheal { border-color: #FF5A4E; background: rgba(70,10,8,.6); }
.mvs-aw .row.noheal .tag { background: #FF5A4E; color: #1A0604; }
.mvs-aw .row.enrage { border-color: #FF9A3C; background: rgba(70,32,6,.55); }
.mvs-aw .row.enrage .tag { background: #FF9A3C; color: #1A0E04; }
.mvs-aw .row.scroll { border-color: #C77DFF; background: rgba(48,14,70,.62); }
.mvs-aw .row.scroll .tag { background: #C77DFF; color: #1A0826; }
.mvs-aw .row.noheal { animation: mvsAuraRed 0.9s ease-in-out infinite alternate; }
.mvs-aw .row.enrage { animation: mvsAuraOrange 0.9s ease-in-out infinite alternate; }
@keyframes mvsAuraRed { from { box-shadow: 0 0 2px rgba(255,90,78,.5); } to { box-shadow: 0 0 12px rgba(255,90,78,1); } }
@keyframes mvsAuraOrange { from { box-shadow: 0 0 2px rgba(255,154,60,.5); } to { box-shadow: 0 0 12px rgba(255,154,60,1); } }
.mvs-aw .chip.scroll { border-color: #C77DFF; color: #DDB0FF; }
.mvs-aw .row.max { border-color: #40E0FF; animation: mvsAuraGlow 1.1s ease-in-out infinite alternate; }
.mvs-aw .row.max .st { color: #40E0FF; }
.mvs-aw .info { margin: .35em 0 0 .2em; padding-top: .25em; border-top: 1px solid rgba(138,106,46,.4); color: #C8B89A; font-size: .95em; white-space: normal; }
.mvs-aw .info b { color: #EDE3CC; font-weight: 600; }
.mvs-aw .info .wd { color: #E6DCC6; }
.mvs-aw .more, .mvs-aw .none { color: #8A806C; font-size: .9em; margin: 0 .2em .25em; }
.mvs-aw .chips { display: none; gap: .25em; flex-wrap: wrap; padding: 0 .45em .35em; }
.mvs-aw.min .chips { display: flex; }
.mvs-aw .chip { display: flex; align-items: center; gap: .25em; padding: 0 .45em 0 .15em; border-radius: .7em; background: rgba(0,0,0,.4);
  border: 1px solid rgba(138,106,46,.6); font-weight: 700; }
.mvs-aw .chip i { width: 1.3em; height: 1.3em; border-radius: .25em; background: center/contain no-repeat; }
.mvs-aw .chip.max { border-color: #40E0FF; color: #40E0FF; box-shadow: 0 0 6px rgba(64,224,255,.7); }
.mvs-aw .chip.noheal { border-color: #FF5A4E; color: #FF8A80; }
.mvs-aw .chip.enrage { border-color: #FF9A3C; color: #FFB870; }
@keyframes mvsAuraGlow { from { box-shadow: 0 0 3px rgba(64,224,255,.45); } to { box-shadow: 0 0 12px rgba(64,224,255,.95); } }
.mvs-ap { position: fixed; z-index: 31; width: 30px; height: 30px; border-radius: 50%; border: 1px solid #8A6A2E;
  background: rgba(22, 17, 12, 0.86); color: #E8C15A; font: 700 13px system-ui, sans-serif; padding: 0; box-shadow: 0 2px 8px rgba(0,0,0,.5); }
.mvs-ap.max { border-color: #40E0FF; color: #40E0FF; animation: mvsAuraGlow 1.1s ease-in-out infinite alternate; }
.mvs-ap.noheal { border-color: #FF5A4E; color: #FF5A4E; }`;
        document.head.appendChild(s);
    }

    function clampTo(node, x, y) {
        node.style.left = Math.min(Math.max(0, x), Math.max(0, window.innerWidth - node.offsetWidth)) + "px";
        node.style.top = Math.min(Math.max(0, y), Math.max(0, window.innerHeight - node.offsetHeight)) + "px";
    }

    // One window: key "all", "me" or "t" (its own position, collapsed and hidden state).
    function makeWin(key, title, defX, defY, pillY, pillGlyph, side) {
        css();
        const w = { key, el: document.createElement("div"), pill: document.createElement("button"), sig: "", rows: {} };
        const el = w.el;
        el.className = "mvs-aw";
        el.innerHTML = `<div class="hd"><span class="grip" title="Drag to move">⠿</span><span class="tt">${esc(title)}</span><button class="bt sz-b" title="Size">A</button><button class="bt sp-b" title="Split / join You and Target">⇆</button>` +
            `<button class="bt min-b" title="Collapse">–</button><button class="bt hide-b" title="Hide">×</button></div>` +
            `<div class="chips"></div><div class="bd"></div>`;
        if (store.get("auraMin." + key, false)) el.classList.add("min");
        el.querySelector(".sz-b").addEventListener("click", e => { e.stopPropagation(); cycleSize(); });
        el.querySelector(".sp-b").addEventListener("click", e => { e.stopPropagation(); setSplit(!store.get("auraSplit", true)); });
        el.querySelector(".min-b").addEventListener("click", e => { e.stopPropagation(); toggleMin(w); });
        el.querySelector(".hide-b").addEventListener("click", e => { e.stopPropagation(); store.set("auraHidden." + key, true); draw(); });
        el.querySelector(".bd").addEventListener("click", e => {
            const row = e.target.closest(".row");
            if (!row) return;
            const k = row.dataset.k;
            if (open.has(k)) open.delete(k); else open.add(k);
            w.sig = "";
            draw();
        });
        drag(w);
        document.body.appendChild(el);
        const pos = store.get("auraPos." + key, null);
        clampTo(el, pos ? pos.x : defX, pos ? pos.y : defY);
        const pill = w.pill;
        pill.className = "mvs-ap";
        pill.textContent = pillGlyph;
        pill.title = title;
        pill.addEventListener("click", () => { store.set("auraHidden." + key, false); w.sig = ""; draw(); });
        pill.style[side === "left" ? "left" : "right"] = "6px";
        pill.style.top = pillY + "px";
        document.body.appendChild(pill);
        return w;
    }

    const SIZES = [0.6, 0.72, 0.85, 1];
    function applySize() {
        const k = store.get("auraSize", 0.72);
        for (const w of Object.values(wins || {})) {
            w.el.style.setProperty("--s", k);
            clampTo(w.el, w.el.offsetLeft, w.el.offsetTop);
        }
    }

    function cycleSize() {
        const k = store.get("auraSize", 0.72);
        store.set("auraSize", SIZES[(SIZES.indexOf(k) + 1) % SIZES.length]);
        applySize();
    }

    function toggleMin(w) {
        w.el.classList.toggle("min");
        store.set("auraMin." + w.key, w.el.classList.contains("min"));
    }

    function drag(w) {
        const handle = w.el.querySelector(".hd");
        let start = null;
        handle.addEventListener("pointerdown", e => {
            if (e.target.closest(".bt")) return;
            start = { x: e.clientX, y: e.clientY, l: w.el.offsetLeft, t: w.el.offsetTop, moved: false };
            handle.setPointerCapture(e.pointerId);
        });
        handle.addEventListener("pointermove", e => {
            if (!start) return;
            const dx = e.clientX - start.x, dy = e.clientY - start.y;
            if (!start.moved && Math.abs(dx) + Math.abs(dy) < 6) return;
            start.moved = true;
            clampTo(w.el, start.l + dx, start.t + dy);
        });
        handle.addEventListener("pointerup", () => {
            if (!start) return;
            if (start.moved) store.set("auraPos." + w.key, { x: w.el.offsetLeft, y: w.el.offsetTop });
            else toggleMin(w);
            start = null;
        });
        handle.addEventListener("pointercancel", () => { start = null; });
    }

    let wins = null;
    function windows() {
        if (wins) return wins;
        const W = window.innerWidth, H = window.innerHeight;
        // Layout 2 (2026-11-08): split by default, your auras on the left under your portrait, the
        // target's in the top right corner. Earlier saved positions are dropped once.
        if (!store.get("auraLayout2", false)) {
            store.set("auraLayout2", true);
            store.set("auraSplit", true);
            for (const k of ["all", "me", "t"]) { try { localStorage.removeItem("mvs.auraPos." + k); } catch (e) { } }
        }
        wins = {
            all: makeWin("all", "Auras", 4, Math.round(H * 0.35), Math.round(H * 0.35), "✦", "left"),
            me: makeWin("me", "You", 4, Math.round(H * 0.35), Math.round(H * 0.35), "✦", "left"),
            t: makeWin("t", "Target", W - 150, Math.round(H * 0.1), Math.round(H * 0.1), "◎", "right")
        };
        applySize();
        return wins;
    }

    function setSplit(on) {
        store.set("auraSplit", on);
        for (const w of Object.values(windows())) w.sig = "";
        draw();
    }

    // ---- reading the game ----

    function auraList(leafPath, who) {
        const hud = read(leafPath + ".hudAuras");
        if (!hud || typeof hud !== "object") return null;   // not readable right now: keep what we had
        const raw = read(leafPath + ".auras");
        const byName = {};
        if (Array.isArray(raw)) {
            for (const a of raw) {
                if (!a || !a.nam) continue;
                const b = byName[a.nam];
                if (!b || Number(a.ts) > Number(b.ts)) byName[a.nam] = a;
            }
        }
        // The hud records' end times are in the game's own clock (getTimer); the aura records' cast
        // times are wall clock. Where both were written for the same cast, end minus duration against
        // the cast time gives the offset between the clocks. Some pairs are off: a stack lost moves only
        // the hud end, and an aura re-cast on a monster gets a new cast time while its hud end stays put
        // (seen on Doom Overlord: Overthrown 3.4 s off). So the median of recent samples is used.
        const nowMs = Date.now();
        for (const name of Object.keys(hud)) {
            const h = hud[name], a = byName[name];
            if (a && a.ts && h && h.end > 0 && h.dur > 0 && Math.abs(Number(a.dur) - h.dur) < 0.01) {
                offSamples.set(who + ":" + name + ":" + a.ts, { off: Number(a.ts) - (h.end - h.dur * 1000), at: nowMs });
            }
        }
        for (const [k, s] of offSamples) if (nowMs - s.at > 120000) offSamples.delete(k);
        while (offSamples.size > 25) offSamples.delete(offSamples.keys().next().value);
        if (offSamples.size) {
            const offs = [...offSamples.values()].map(s => s.off).sort((x, y) => x - y);
            clockOffset = offs[Math.floor((offs.length - 1) / 2)];
        }
        const out = [];
        for (const name of Object.keys(hud)) {
            const h = hud[name] || {}, a = byName[name] || {};
            // The later of the two: the latest cast plus its duration, or the hud end (stacks that
            // fall off one at a time push only the hud end on).
            const fromCast = a.ts && a.dur > 0 ? Number(a.ts) + a.dur * 1000 : null;
            const fromHud = h.end > 0 && clockOffset !== null ? h.end + clockOffset : null;
            const end = fromCast !== null && fromHud !== null ? Math.max(fromCast, fromHud) : (fromCast ?? fromHud);
            out.push({ key: who + ":" + name, who, name, n: Math.max(1, Number(h.n) || Number(a.val) || 1),
                dur: Number(h.dur) || Number(a.dur) || 0, end, icon: h.icon || a.icon || "",
                casterType: a.casterType, casterId: a.casterId, seq: Number(h.seq) || 0 });
        }
        return out;
    }

    function poll() {
        const me = read("world.myAvatar.objData.strUsername") || "";
        state.me = me;
        if (!me) { state.mine = []; state.theirs = []; state.target = null; return; }
        const now = Date.now();
        if (now - lastDesc > DESC_MS) {
            lastDesc = now;
            const d = read("world.auraDescs");
            if (d && typeof d === "object") descs = d;
            state.cls = read("world.myAvatar.objData.strClassName") || "";
            // The game's own touch controls (skill buttons down the right side, the drops bar) are up.
            state.mobile = call("mvsMobileActive") === true;
            loadWiki();
        }
        state.myId = read("world.myAvatar.dataLeaf.entID");
        const mine = auraList("world.myAvatar.dataLeaf", "me");
        if (mine) state.mine = mine;
        if (read("world.myAvatar.target.dataLeaf.intHP") != null) {
            state.target = {
                name: read("world.myAvatar.target.objData.strMonName") || read("world.myAvatar.target.objData.strUsername") || "Target",
                mapId: read("world.myAvatar.target.dataLeaf.MonMapID")
            };
            const theirs = auraList("world.myAvatar.target.dataLeaf", "t");
            if (theirs) state.theirs = theirs;
        } else {
            state.target = null;
            state.theirs = [];
        }
        let changed = false;
        for (const a of state.mine.concat(state.theirs)) {
            if (a.n > (seen[a.name] || 0)) { seen[a.name] = a.n; changed = true; }
        }
        if (changed) store.set("auraSeen", seen);
    }

    // ---- drawing ----

    function sourceOf(a) {
        if (a.casterType === "m") {
            if (state.target && state.target.mapId != null && a.casterId === state.target.mapId) return state.target.name + " (your target)";
            return "A monster";
        }
        if (a.casterType === "p") return a.casterId === state.myId ? "You (" + state.me + ")" : "Another player";
        return "Unknown";
    }

    function decorate(list, now) {
        for (const a of list) {
            a.left = a.end != null ? (a.end - now) / 1000 : null;
            // Auras cast by you are described by your class; others by the game's description, if any.
            const mine = a.casterType === "p" && a.casterId === state.myId;
            a.wiki = mine ? wikiInfo(a.name, state.cls) : null;
            // Forge procs are yours; a monster's "Decay" is not Dauntless's.
            a.enh = mine || a.casterType !== "m" ? enhInfo(a.name, a.who) : null;
            a.max = limitFromDesc(descs[a.name]) || (a.wiki ? a.wiki.max : 0) || (a.enh ? a.enh.max : 0);
            a.nocap = !a.max && a.wiki && a.wiki.nocap;
            a.most = seen[a.name] || a.n;
            a.stacks = a.max > 1 || a.most > 1;
            a.full = a.max > 1 && a.n >= a.max;
            a.sp = special(a.name);
            // A class's own aura with the same name (King's Echo has a "Focus") isn't a scroll's taunt.
            if (a.sp && a.wiki && a.sp.kind !== "noheal") a.sp = null;
            // Any other scroll's effect (AQW Wiki scroll pages) gets the scroll highlight.
            if (!a.sp && !a.wiki && a.enh && /^Scroll of/i.test(a.enh.src || "")) {
                a.sp = { kind: "scroll", tag: "SCROLL", note: a.enh.src + ": " + a.enh.t };
            }
        }
        const live = list.filter(a => a.left === null || a.left > -1);
        const rank = a => (a.sp ? 0 : a.full ? 1 : a.stacks ? 2 : 3);
        live.sort((x, y) => rank(x) - rank(y) || x.seq - y.seq);
        return live;
    }

    function fmtTime(s) {
        if (s == null) return "∞";
        if (s < 0) s = 0;
        return s >= 60 ? Math.floor(s / 60) + "m" + String(Math.floor(s % 60)).padStart(2, "0") : s.toFixed(1) + "s";
    }

    function stackText(a) {
        if (!a.stacks || (a.n <= 1 && !(a.max > 1))) return "";
        if (a.max > 1) return `${a.n}<small>/${a.max}</small>`;
        return `×${a.n}${a.nocap ? "<small> no cap</small>" : ""}`;
    }

    function summary(a) {
        const d = descs[a.name];
        if (d) return String(d).replace(/<[^>]*>/g, " ");
        if (a.wiki) return a.wiki.text;
        if (a.enh) return a.enh.t;
        return a.sp ? a.sp.note : "";
    }

    function rowHtml(a) {
        const cls = ["row", a.sp ? a.sp.kind : "", a.full ? "max" : ""].filter(Boolean).join(" ");
        const url = iconUrl(a.icon);
        const icon = url ? `<span class="ic" style="background-image:url(${url})"></span>`
            : `<span class="ic">${esc(a.name.replace(/^[IVXLC0-9]+\s*-\s*/, "").charAt(0).toUpperCase())}</span>`;
        const sum = summary(a);
        let info = "";
        if (open.has(a.key)) {
            const lines = [`<b>Source:</b> ${esc(sourceOf(a))}`];
            if (a.stacks) {
                lines.push(`<b>Stacks:</b> ${a.n}${a.max > 1 ? " of " + a.max + (a.full ? " (max)" : "")
                    : a.nocap ? " (no cap)" : " (limit unknown; most seen " + a.most + ")"}`);
            }
            lines.push(`<b>Duration:</b> ${a.dur > 0 ? a.dur + "s" : "until removed"}`);
            const from = a.wiki && a.wiki.from ? a.wiki.from : (!a.wiki && a.enh ? a.enh.src : "");
            if (sum) lines.push(`<span class="wd">${esc(sum)}</span>` + (from ? ` <i>(${esc(from)}, AQW Wiki)</i>` : ""));
            if (a.sp && sum !== a.sp.note) lines.push(esc(a.sp.note));
            info = `<div class="info">${lines.join("<br>")}</div>`;
        }
        return `<div class="${cls}" data-k="${esc(a.key)}"><div class="top">${icon}` +
            (a.sp ? `<span class="tag">${a.sp.tag}</span>` : "") +
            `<span class="nm">${esc(a.name)}</span><span class="st">${stackText(a)}</span><span class="tm"></span></div>` +
            (sum && !open.has(a.key) ? `<div class="sum">${esc(sum)}</div>` : "") +
            `<div class="bar"></div>${info}</div>`;
    }

    function section(title, list) {
        const rows = list.slice(0, MAX_ROWS).map(rowHtml).join("");
        const more = list.length > MAX_ROWS ? `<div class="more">+${list.length - MAX_ROWS} more</div>` : "";
        return `<div class="sh">${esc(title)}</div>${rows || '<div class="none">No auras</div>'}${more}`;
    }

    function chip(a) {
        const cls = ["chip", a.sp ? a.sp.kind : "", a.full ? "max" : ""].filter(Boolean).join(" ");
        const url = iconUrl(a.icon);
        const short = a.name.replace(/^[IVXLC0-9]+\s*-\s*/, "").split(/\s+/).map(w => w.charAt(0)).join("").slice(0, 3);
        return `<span class="${cls}" data-k="${esc(a.key)}">${url ? `<i style="background-image:url(${url})"></i>` : esc(short)}` +
            `${a.stacks ? " " + a.n : ""}<span class="tm"></span></span>`;
    }

    // Structure changes (auras added, removed, stacks, details opened) rebuild a window; the time
    // left and bars update in place every 0.1 s.
    // A window that just ran out of auras stays up a moment (between one aura ending and the next
    // starting, or a game read that missed), so it doesn't blink.
    const HOLD_MS = 1500;
    // A window grows with its auras but stops above the game's controls, and its list scrolls: on the
    // right, above the touch skill buttons (they start a little above mid-height); elsewhere, above the
    // bottom bar. Seen on Void Highlord vs Doom Overlord: 4 target auras covered skills 4 and 5.
    function fitHeight(w) {
        const H = window.innerHeight, el = w.el;
        const right = el.offsetLeft + el.offsetWidth / 2 > window.innerWidth / 2;
        const limit = (state.mobile && right ? 0.5 : 0.86) * H;
        const hd = el.querySelector(".hd"), bd = el.querySelector(".bd");
        const room = Math.max(48, Math.round(limit - el.offsetTop - (hd ? hd.offsetHeight : 0)));
        if (bd.style.maxHeight !== room + "px") bd.style.maxHeight = room + "px";
    }

    function paint(w, show, html, chips, list) {
        const now = Date.now();
        if (show && w.forceUntil && now < w.forceUntil) {
            // Opened from a portrait's menu: shown even before any aura is up.
            w.lastFull = now;
        } else if (show && list && list.length) w.lastFull = now;
        else if (show && w.lastFull && now - w.lastFull < HOLD_MS) { w.el.style.display = ""; return; }
        if (show && !(list && list.length) && !(w.forceUntil && now < w.forceUntil)) show = false;
        w.el.style.display = show ? "" : "none";
        if (!show) return;
        const sig = html + chips;
        if (sig !== w.sig) {
            w.sig = sig;
            w.el.querySelector(".bd").innerHTML = html;
            w.el.querySelector(".chips").innerHTML = chips;
        }
        fitHeight(w);
        const byKey = {};
        for (const a of list) byKey[a.key] = a;
        for (const node of w.el.querySelectorAll("[data-k]")) {
            const a = byKey[node.dataset.k];
            if (!a) continue;
            const tm = node.querySelector(".tm");
            if (tm) tm.textContent = node.classList.contains("chip") ? (a.left != null ? " " + Math.ceil(Math.max(0, a.left)) + "s" : "") : fmtTime(a.left);
            const bar = node.querySelector(".bar");
            if (bar) {
                const frac = a.left != null && a.dur > 0 ? Math.max(0, Math.min(1, a.left / a.dur)) : 0;
                bar.style.width = (frac * 100).toFixed(1) + "%";
                bar.classList.toggle("low", a.left != null && a.left < 3);
            }
        }
    }

    function draw() {
        const W = windows();
        iconBudget = 2;
        const now = Date.now();
        const mine = decorate(state.mine, now), theirs = decorate(state.theirs, now);
        const split = store.get("auraSplit", true);
        const hasMe = !!state.me;
        const tTitle = state.target ? "Target: " + state.target.name : "Target";
        const meHtml = section("You", mine), tHtml = section(tTitle, theirs);
        const meChips = mine.map(chip).join(""), tChips = theirs.map(a => chip(a)).join("");
        const anyMe = mine.length > 0, anyT = theirs.length > 0;
        const hid = k => store.get("auraHidden." + k, false);
        const pill = (w, show, list) => {
            w.pill.style.display = show ? "" : "none";
            w.pill.classList.toggle("max", list.some(a => a.full));
            w.pill.classList.toggle("noheal", list.some(a => a.sp && a.sp.kind === "noheal"));
        };
        if (!split) {
            const any = hasMe && (anyMe || anyT);
            paint(W.all, hasMe && !hid("all"), meHtml + (state.target ? tHtml : ""), meChips + (state.target ? tChips : ""), mine.concat(theirs));
            pill(W.all, any && hid("all"), mine.concat(theirs));
            paint(W.me, false); pill(W.me, false, []);
            paint(W.t, false); pill(W.t, false, []);
        } else {
            paint(W.all, false); pill(W.all, false, []);
            paint(W.me, hasMe && !hid("me"), meHtml, meChips, mine);
            pill(W.me, hasMe && anyMe && hid("me"), mine);
            paint(W.t, hasMe && !hid("t"), tHtml, tChips, theirs);
            pill(W.t, hasMe && !!state.target && anyT && hid("t"), theirs);
        }
    }

    // Portrait menus ("Show Auras"): which = "me" or "t". Un-hides and expands that window (the joined
    // window when You and Target aren't split) and keeps it up a few seconds even with no auras.
    window.MvsAuras = {
        show(which) {
            const W = windows();
            const w = store.get("auraSplit", true) ? (which === "t" ? W.t : W.me) : W.all;
            store.set("auraHidden." + w.key, false);
            if (w.el.classList.contains("min")) toggleMin(w);
            w.forceUntil = Date.now() + 5000;
            w.sig = "";
            poll();
            draw();
        }
    };

    setInterval(poll, POLL_MS);
    setInterval(draw, DRAW_MS);
})();
