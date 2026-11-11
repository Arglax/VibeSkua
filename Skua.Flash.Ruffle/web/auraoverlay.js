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

    // ---- display rules shared with desktop VibeSkua (aura-rules-spec.md, approved 2026-11-12) ----
    const PILL = { buff: "#E8B931", hot: "#4CC26A", dot: "#E5484D", debuff: "#8A6CFF", scroll: "#F08A24", neutral: "#8E8E8E" };
    const GROUPS = ["enrage", "scrolls", "potions", "debuffs", "other"];
    const GROUP_NAMES = { enrage: "Scroll of Enrage", scrolls: "Scrolls", potions: "Potions", debuffs: "Debuffs", other: "Everything else" };
    const FILTERS = [["all", "All"], ["buff", "Buffs"], ["debuff", "Debuffs"], ["sp", "Scrolls & potions"]];
    const POTION_RE = /tonic|elixir|potion|draught|brew|philter|felicitous|tincture|serum/i;

    function clauseKind(c) {
        // Healing turned into damage (Decay, Noxious Decay) is a debuff, not a DoT or HoT.
        if (/convert\w*\s+heal\w*\s+(in)?to\s+damage|\bheal\w*\b.*\bas\s+damage\b/i.test(c)) return "debuff";
        if (/\bdots?\b|damage over time|\bburn|\bbleed|\bpoison|\bdeals?\b.*\bevery\b/i.test(c)) return "dot";
        if (/\bhots?\b|\bregen|healing over time|\bheals?\b.*\b(every|over)\b/i.test(c)) return "hot";
        if (/\bstun|heal(ing)?[\s-]*block|\bdecay\b|\bcannot\b|can't|\bblocked\b|\bslow/i.test(c)) return "debuff";
        const m = c.match(/([+\-−])\s*\d/);
        if (!m) return "neutral";
        let good = m[1] === "+";
        // Lower is better for these stats, so their sign flips.
        if (/\bcosts?\b|cooldown|consumption|\btaken\b|incoming damage/i.test(c)) good = !good;
        return good ? "buff" : "debuff";
    }

    // Parsed once per distinct effect text, not on every draw.
    const clauseCache = new Map();
    function clauses(text) {
        const key = String(text || "");
        let v = clauseCache.get(key);
        if (!v) {
            v = key.split(/[,;]/).map(s => s.trim()).filter(Boolean).map(t => ({ t, k: clauseKind(t) }));
            if (clauseCache.size > 500) clauseCache.clear();
            clauseCache.set(key, v);
        }
        return v;
    }

    function norm(s) { return String(s || "").toLowerCase().replace(/[^a-z0-9 ]/g, "").replace(/\s+/g, " ").trim(); }

    function lev(a, b) {
        const d = [];
        for (let i = 0; i <= a.length; i++) d[i] = [i];
        for (let j = 1; j <= b.length; j++) d[0][j] = j;
        for (let i = 1; i <= a.length; i++) for (let j = 1; j <= b.length; j++) {
            d[i][j] = Math.min(d[i - 1][j] + 1, d[i][j - 1] + 1, d[i - 1][j - 1] + (a[i - 1] === b[j - 1] ? 0 : 1));
        }
        return d[a.length][b.length];
    }

    // Best-match score of typed text q against an aura name (0 = no match).
    function matchScore(q, name) {
        const n = norm(name);
        if (!q || !n) return 0;
        if (n === q) return 1000;
        if (n.startsWith(q)) return 800 - (n.length - q.length);
        const words = n.split(" ");
        if (words.some(w => w.startsWith(q))) return 700;
        if (n.includes(q)) return 600;
        let i = 0, first = -1, last = -1;
        for (let p = 0; p < n.length && i < q.length; p++) {
            if (n[p] === q[i]) { if (first < 0) first = p; last = p; i++; }
        }
        if (i === q.length) return 500 - ((last - first + 1) - q.length);
        const dist = Math.min(lev(q, n), ...words.map(w => lev(q, w)));
        if (dist <= Math.max(1, Math.floor(q.length / 3))) return 400 - 50 * dist;
        return 0;
    }

    function knownNames() {
        const out = new Map();
        const add = n => { const k = String(n).toLowerCase(); if (n && !out.has(k)) out.set(k, String(n)); };
        if (wiki && wiki.auras) Object.keys(wiki.auras).forEach(add);
        if (wiki && wiki.fx) for (const c of Object.values(wiki.fx)) Object.keys(c).forEach(add);
        Object.keys(seen).forEach(add);
        return [...out.values()];
    }

    function bestMatches(text, limit) {
        const q = norm(text);
        if (!q) return [];
        return knownNames().map(n => ({ n, s: matchScore(q, n) })).filter(x => x.s > 0)
            .sort((a, b) => b.s - a.s || a.n.length - b.n.length).slice(0, limit);
    }

    function getPins() { return String(store.get("auraPins", "")).split(";").map(s => s.trim()).filter(Boolean); }
    function setPins(p) { store.set("auraPins", p.join(";")); }
    function getOrder() {
        const o = String(store.get("auraOrder", GROUPS.join(","))).split(",").filter(k => GROUPS.includes(k));
        for (const g of GROUPS) if (!o.includes(g)) o.push(g);
        return o;
    }

    function pinIndex(a, pins, text) {
        const nn = norm(a.name), lname = a.name.toLowerCase(), lt = String(text || "").toLowerCase();
        for (let i = 0; i < pins.length; i++) if (norm(pins[i]) === nn) return i;
        for (let i = 0; i < pins.length; i++) {
            const p = pins[i].toLowerCase();
            if (lname.includes(p) || lt.includes(p)) return i;
        }
        return -1;
    }

    let seen = store.get("auraSeen", {});
    let descs = {}, lastDesc = 0, lastDescRead = 0;
    // Aura names the server had no description for at the last read (read again on the next new name).
    const descMiss = new Set();
    // Nothing to read or draw while the page is hidden, the app is in the background or headless.
    function paused() {
        return document.hidden || !!window.__mvsNoDraw || !!window.__mvsBackground;
    }
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
  user-select: none; -webkit-user-select: none; touch-action: none; max-height: 94vh; overflow: hidden;
  contain: layout paint; will-change: transform; }
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
.mvs-aw .sum .by { color: #E8C15A; font-weight: 700; }
.mvs-aw .row.noheal .sum .by, .mvs-aw .row.enrage .sum .by { color: #FFF; }
.mvs-aw .bar { position: absolute; left: 0; bottom: 0; width: 100%; height: .2em; background: #40A8C8;
  transform-origin: 0 50%; will-change: transform; }
.mvs-aw .bar.low { background: #FF9A3C; }
.mvs-aw .row.noheal { border-color: #FF5A4E; background: rgba(70,10,8,.6); }
.mvs-aw .row.noheal .tag { background: #FF5A4E; color: #1A0604; }
.mvs-aw .row.enrage { border-color: #FF9A3C; background: rgba(70,32,6,.55); }
.mvs-aw .row.enrage .tag { background: #FF9A3C; color: #1A0E04; }
.mvs-aw .row.scroll { border-color: #C77DFF; background: rgba(48,14,70,.62); }
.mvs-aw .row.scroll .tag { background: #C77DFF; color: #1A0826; }
/* Glows pulse by opacity on their own layer (compositor only): animating box-shadow repainted the
   row over the game canvas every frame. */
.mvs-aw .row.noheal::after, .mvs-aw .row.enrage::after, .mvs-aw .row.max::after { content: ""; position: absolute; inset: 0;
  border-radius: inherit; pointer-events: none; will-change: opacity; animation: mvsAuraPulse 1s ease-in-out infinite alternate; }
.mvs-aw .row.noheal::after { box-shadow: inset 0 0 .7em rgba(255,90,78,1); }
.mvs-aw .row.enrage::after { box-shadow: inset 0 0 .7em rgba(255,154,60,1); }
.mvs-aw .row.max::after { box-shadow: inset 0 0 .7em rgba(64,224,255,.95); }
@keyframes mvsAuraPulse { from { opacity: .25; } to { opacity: 1; } }
@keyframes mvsAuraRed { from { box-shadow: 0 0 2px rgba(255,90,78,.5); } to { box-shadow: 0 0 12px rgba(255,90,78,1); } }
@keyframes mvsAuraOrange { from { box-shadow: 0 0 2px rgba(255,154,60,.5); } to { box-shadow: 0 0 12px rgba(255,154,60,1); } }
.mvs-aw .chip.scroll { border-color: #C77DFF; color: #DDB0FF; }
.mvs-aw .row.max { border-color: #40E0FF; }
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
.mvs-ap.max { border-color: #40E0FF; color: #40E0FF; box-shadow: 0 0 8px rgba(64,224,255,.8); }
.mvs-ap.noheal { border-color: #FF5A4E; color: #FF5A4E; }
.mvs-aw { width: 22em; }
.mvs-aw .top .nm { min-width: 5.5em; }
.mvs-aw .top .by { flex: 0 1 auto; min-width: 1.5em; max-width: 5em; white-space: nowrap; overflow: hidden; text-overflow: ellipsis; color: #E8B931; font-weight: 700; font-size: .9em; }
.mvs-aw .top .by.you { color: #9A8F7A; }
.mvs-aw .pls { display: flex; flex-wrap: wrap; gap: .2em; margin: .2em 0 0 2.4em; }
.mvs-aw .pl { padding: 0 .4em; border-radius: .6em; font-size: .85em; font-weight: 600; color: var(--c);
  border: 1px solid var(--c); background: color-mix(in srgb, var(--c) 18%, transparent); white-space: nowrap; max-width: 100%; overflow: hidden; text-overflow: ellipsis; }
.mvs-aw .row.scroll { border-color: #F08A24; background: rgba(70,32,6,.55); }
.mvs-aw .row.scroll .tag { background: #F08A24; color: #1A0E04; }
.mvs-aw .flt { display: flex; flex-wrap: wrap; gap: .25em; margin: 0 .2em .3em; }
.mvs-aw .fc { padding: .1em .5em; border-radius: .7em; border: 1px solid rgba(138,106,46,.6); color: #C8B89A; font-size: .85em; font-weight: 700; }
.mvs-aw .fc.on { background: #E8C15A; color: #1A1206; border-color: #E8C15A; }
.mvs-aw .cfg { padding: .3em .5em .5em; border-bottom: 1px solid rgba(138,106,46,.5); max-height: 60vh; overflow-y: auto; touch-action: pan-y; }
.mvs-aw .cfg .ch { color: #E8C15A; font-weight: 700; margin: .3em 0 .2em; }
.mvs-aw .cfg .pins { display: flex; flex-wrap: wrap; gap: .25em; }
.mvs-aw .cfg .pin { padding: .1em .2em .1em .5em; border-radius: .7em; background: rgba(232,185,49,.18); border: 1px solid #E8B931; }
.mvs-aw .cfg .pin b, .mvs-aw .cfg .og b { display: inline-block; padding: 0 .5em; color: #E8C15A; }
.mvs-aw .cfg .hint { color: #8A806C; }
.mvs-aw .cfg .q { width: 100%; box-sizing: border-box; margin-top: .35em; padding: .3em .4em; font: inherit; color: #EDE3CC;
  background: rgba(0,0,0,.5); border: 1px solid #8A6A2E; border-radius: .4em; user-select: text; -webkit-user-select: text; }
.mvs-aw .cfg .sg { padding: .3em .4em; border-bottom: 1px solid rgba(138,106,46,.3); }
.mvs-aw .cfg .og { display: flex; align-items: center; padding: .15em 0; }
.mvs-aw .cfg .og span { flex: 1; }
.mvs-aw .cfg .rst { display: inline-block; margin-top: .3em; color: #C8B89A; text-decoration: underline; }`;
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
            `<button class="bt cfg-b" title="Pins and order">⚙</button>` +
            `<button class="bt min-b" title="Collapse">–</button><button class="bt hide-b" title="Hide">×</button></div>` +
            `<div class="cfg"></div><div class="chips"></div><div class="bd"></div>`;
        el.querySelector(".cfg-b").addEventListener("click", e => { e.stopPropagation(); toggleCfg(w); });
        cfgEvents(w);
        if (store.get("auraMin." + key, false)) el.classList.add("min");
        el.querySelector(".sz-b").addEventListener("click", e => { e.stopPropagation(); cycleSize(); });
        el.querySelector(".sp-b").addEventListener("click", e => { e.stopPropagation(); setSplit(!store.get("auraSplit", true)); });
        el.querySelector(".min-b").addEventListener("click", e => { e.stopPropagation(); toggleMin(w); });
        el.querySelector(".hide-b").addEventListener("click", e => { e.stopPropagation(); store.set("auraHidden." + key, true); draw(); });
        el.querySelector(".bd").addEventListener("click", e => {
            const fc = e.target.closest(".fc");
            if (fc) {
                const [side, f] = fc.dataset.f.split(":");
                store.set("auraFilter." + side, f);
                for (const x of Object.values(wins)) x.sig = "";
                draw();
                return;
            }
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

    // ---- settings panel (⚙): pins with best-match search, group order ----
    function toggleCfg(w) {
        w.cfgOpen = !w.cfgOpen;
        if (w.el.classList.contains("min") && w.cfgOpen) toggleMin(w);
        renderCfg(w);
    }

    function renderCfg(w) {
        const c = w.el.querySelector(".cfg");
        if (!w.cfgOpen) { c.innerHTML = ""; c.style.display = "none"; return; }
        c.style.display = "";
        const pins = getPins(), order = getOrder();
        const pinHtml = pins.length ? pins.map((p, i) => `<span class="pin">${esc(p)}<b data-unpin="${i}">×</b></span>`).join("")
            : `<span class="hint">Nothing pinned</span>`;
        const ordHtml = order.map((g, i) => `<div class="og"><span>${i + 1}. ${GROUP_NAMES[g]}</span>` +
            `<b data-mv="${i}:-1">▲</b><b data-mv="${i}:1">▼</b></div>`).join("");
        c.innerHTML = `<div class="ch">Pinned (always on top)</div><div class="pins">${pinHtml}</div>` +
            `<input class="q" type="text" placeholder="Search aura to pin…" autocomplete="off" spellcheck="false">` +
            `<div class="sug"></div><div class="ch">Order</div>${ordHtml}<span class="rst" data-reset="1">Reset order</span>`;
    }

    function renderSug(w) {
        const q = w.el.querySelector(".cfg .q"), s = w.el.querySelector(".cfg .sug");
        if (!q || !s) return;
        const m = bestMatches(q.value, 6);
        s.innerHTML = q.value.trim() ? (m.length ? m.map(x => `<div class="sg" data-add="${esc(x.n)}">${esc(x.n)}</div>`).join("")
            : `<div class="sg" data-add="${esc(q.value.trim())}">Pin keyword “${esc(q.value.trim())}”</div>`) : "";
    }

    function addPin(name) {
        const p = getPins();
        if (name && !p.some(x => x.toLowerCase() === name.toLowerCase())) p.push(name);
        setPins(p);
        refreshAll();
    }

    function refreshAll() {
        for (const x of Object.values(wins || {})) { x.sig = ""; renderCfg(x); }
        draw();
    }

    function cfgEvents(w) {
        const c = w.el.querySelector(".cfg");
        c.style.display = "none";
        c.addEventListener("input", () => renderSug(w));
        c.addEventListener("keydown", e => {
            if (e.key !== "Enter" || !e.target.classList.contains("q")) return;
            const v = e.target.value.trim();
            if (!v) return;
            const m = bestMatches(v, 1);
            addPin(m.length ? m[0].n : v);
        });
        c.addEventListener("click", e => {
            const t = e.target;
            if (t.dataset.add) addPin(t.dataset.add);
            else if (t.dataset.unpin != null) { const p = getPins(); p.splice(+t.dataset.unpin, 1); setPins(p); refreshAll(); }
            else if (t.dataset.mv) {
                const [i, d] = t.dataset.mv.split(":").map(Number), o = getOrder(), j = i + d;
                if (j >= 0 && j < o.length) { [o[i], o[j]] = [o[j], o[i]]; store.set("auraOrder", o.join(",")); refreshAll(); }
            } else if (t.dataset.reset) { store.set("auraOrder", GROUPS.join(",")); refreshAll(); }
        });
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
        // AQW keeps one record per aura name per caster (World.updateAuraData matches nam, casterType
        // and casterId), so every player's Decay or taunt on a boss is its own record. A re-cast by the
        // same caster only updates dur/val, not ts, so ts is when that caster first applied it.
        const casters = {};
        if (Array.isArray(raw)) {
            for (const a of raw) {
                if (!a || !a.nam) continue;
                const b = byName[a.nam];
                if (!b || Number(a.ts) > Number(b.ts)) byName[a.nam] = a;
                if (a.casterType === "m" || a.casterType === "p") {
                    const list = casters[a.nam] || (casters[a.nam] = []);
                    const k = a.casterType + ":" + a.casterId;
                    const at = list.find(c => c.k === k);
                    if (!at) list.push({ k, type: a.casterType, id: a.casterId, ts: Number(a.ts) || 0 });
                    else if (Number(a.ts) > at.ts) at.ts = Number(a.ts);
                }
            }
            for (const n of Object.keys(casters)) casters[n].sort((x, y) => y.ts - x.ts);
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
                casterType: a.casterType, casterId: a.casterId, casters: casters[name] || [], seq: Number(h.seq) || 0 });
        }
        return out;
    }

    // The game's Options row "Improved Auras" (MVS) turns the whole overlay off (bridge.js keeps the value).
    function enabled() {
        return !(window.__mvsOpts && window.__mvsOpts.mvsAuras === false);
    }

    // Reads are gated on the game's own change counter: every aura packet goes through
    // World.handleAuraEvent, which bumps dataLeaf.hudAurasV (World.hudAuraSync) before it updates
    // dataLeaf.auras. So each poll reads a few numbers, and the aura lists only when a counter moved,
    // the target changed, or FULL_MS passed (a safety net). Before: ~10 JSON reads every 250 ms.
    const FULL_MS = 2000;
    const gate = { me: { v: null, at: 0 }, t: { v: null, at: 0, key: "" }, user: "" };

    function sideChanged(g, v, now) {
        if (v !== g.v || now - g.at > FULL_MS) { g.v = v; g.at = now; return true; }
        return false;
    }

    function poll() {
        if (!enabled() || paused()) return;
        const now = Date.now();
        // hudAurasV is undefined until the leaf's first aura, so the avatar's id says "logged in".
        const myId = read("world.myAvatar.dataLeaf.entID");
        if (myId == null) {
            state.me = ""; state.mine = []; state.theirs = []; state.target = null;
            gate.me.v = gate.t.v = null; gate.t.key = ""; gate.user = "";
            return;
        }
        if (myId !== state.myId || !state.me) {
            state.myId = myId;
            state.me = read("world.myAvatar.objData.strUsername") || "";
        }
        if (now - lastDesc > DESC_MS) {
            lastDesc = now;
            state.cls = read("world.myAvatar.objData.strClassName") || "";
            // The game's own touch controls (skill buttons down the right side, the drops bar) are up.
            state.mobile = call("mvsMobileActive") === true;
            loadWiki();
        }
        if (sideChanged(gate.me, read("world.myAvatar.dataLeaf.hudAurasV"), now)) {
            const mine = auraList("world.myAvatar.dataLeaf", "me");
            if (mine) state.mine = mine;
        }
        // The target: a monster has MonMapID, a player only entID.
        const tMon = read("world.myAvatar.target.dataLeaf.MonMapID");
        const tEnt = tMon == null ? read("world.myAvatar.target.dataLeaf.entID") : null;
        if (tMon != null || tEnt != null) {
            const key = tMon != null ? "m:" + tMon : "p:" + tEnt;
            if (key !== gate.t.key) {
                gate.t.key = key; gate.t.v = null;
                state.target = {
                    name: read("world.myAvatar.target.objData.strMonName") || read("world.myAvatar.target.objData.strUsername") || "Target",
                    mapId: tMon
                };
            }
            if (sideChanged(gate.t, read("world.myAvatar.target.dataLeaf.hudAurasV"), now)) {
                const theirs = auraList("world.myAvatar.target.dataLeaf", "t");
                if (theirs) state.theirs = theirs;
            }
        } else {
            state.target = null;
            state.theirs = [];
            gate.t.key = ""; gate.t.v = null;
        }
        // The server's own aura descriptions (World.auraDescs) grow all session: read them only when an
        // aura shows up that has none yet, at most every DESC_MS.
        if (now - lastDescRead > DESC_MS && state.mine.concat(state.theirs).some(a => !(a.name in descs) && !descMiss.has(a.name))) {
            lastDescRead = now;
            const d = read("world.auraDescs");
            if (d && typeof d === "object") descs = d;
            for (const a of state.mine.concat(state.theirs)) if (!(a.name in descs)) descMiss.add(a.name);
        }
        resolveCasters(now);
        let changed = false;
        for (const a of state.mine.concat(state.theirs)) {
            if (a.n > (seen[a.name] || 0)) { seen[a.name] = a.n; changed = true; }
        }
        if (changed) store.set("auraSeen", seen);
    }

    // ---- who cast it ----
    // Players: World.avatars is keyed by the caster's entID (World.as: avatars[uid], pnm = uoName);
    // the display name is objData.strUsername. Monsters: skua.swf's availableMonsters lists the cell's
    // monsters with MonMapID and strMonName. Looked up only for casters on screen, at most every 15 s each.
    const casterInfo = {};          // "p:123" / "m:4" -> { name, cls, at }
    let monsAt = 0;

    function resolveCasters(now) {
        const want = new Set();
        for (const a of state.mine.concat(state.theirs)) for (const c of a.casters || []) want.add(c);
        let needMons = false;
        for (const c of want) {
            const info = casterInfo[c.k];
            if (info && now - info.at < 15000) continue;
            if (c.type === "p") {
                if (c.id === state.myId) { casterInfo[c.k] = { name: state.me, cls: state.cls, at: now, me: true }; continue; }
                const base = "world.avatars." + c.id;
                const name = read(base + ".objData.strUsername") || read(base + ".pnm");
                const cls = read(base + ".objData.strClassName");
                casterInfo[c.k] = { name: name || "", cls: cls || "", at: now };
            } else needMons = true;
        }
        if (needMons && now - monsAt > 3000) {
            monsAt = now;
            let mons = null;
            try { const r = call("availableMonsters"); mons = typeof r === "string" ? JSON.parse(r) : null; } catch (e) { mons = null; }
            if (Array.isArray(mons)) {
                for (const m of mons) {
                    if (m && m.MonMapID != null) casterInfo["m:" + m.MonMapID] = { name: m.strMonName || "", cls: "", at: now };
                }
            }
            for (const c of want) {
                if (c.type === "m" && !casterInfo[c.k] && state.target && c.id === state.target.mapId) {
                    casterInfo[c.k] = { name: state.target.name, cls: "", at: now };
                }
            }
        }
    }

    function casterName(c) {
        const info = casterInfo[c.k];
        if (c.type === "p" && c.id === state.myId) return "You";
        if (info && info.name) return info.name;
        return c.type === "m" ? "a monster" : "a player";
    }

    // ---- drawing ----

    function sourceOf(a) {
        if (!a.casters || !a.casters.length) return "Unknown (the game sent no caster)";
        return a.casters.map(casterName).join(", ");
    }

    // Short "by ..." for a row: the latest caster, plus how many others have it up too.
    function byLine(a) {
        const cs = a.casters || [];
        if (!cs.length) return "";
        return casterName(cs[0]) + (cs.length > 1 ? " +" + (cs.length - 1) : "");
    }

    // A caster's own class decides what its aura does: a party buff from someone else's Lord of Order
    // is read from Lord of Order, not from your class.
    function casterClass(a) {
        const c = (a.casters || [])[0];
        if (!c || c.type !== "p") return "";
        if (c.id === state.myId) return state.cls;
        const info = casterInfo[c.k];
        return info ? info.cls : "";
    }

    function fxInfo(name, cls) {
        if (!wiki || !wiki.fx || !cls) return null;
        const f = wiki.fx[normClass(cls)];
        if (!f) return null;
        const base = String(name).replace(/^[IVXLC0-9]+\s*-\s*/, "").replace(/\s*\(reversed\)\s*/i, "").replace(/^the\s+/i, "").trim();
        if (f[base]) return f[base];
        const lower = base.toLowerCase();
        for (const k of Object.keys(f)) if (k.toLowerCase() === lower) return f[k];
        return null;
    }

    function decorate(list, now) {
        for (const a of list) {
            a.left = a.end != null ? (a.end - now) / 1000 : null;
            // Auras cast by you are described by your class; others by the game's description, if any.
            const mine = a.casterType === "p" && a.casterId === state.myId;
            const ccls = casterClass(a);
            a.wiki = ccls ? wikiInfo(a.name, ccls) : null;
            a.fx = ccls ? fxInfo(a.name, ccls) : null;
            a.fxCls = ccls;
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
        const pins = getPins(), order = getOrder();
        for (const a of list) {
            const text = summary(a);
            a.cl = clauses(text);
            const src = a.enh ? String(a.enh.src || "") : "";
            const enrage = (a.sp && a.sp.kind === "enrage") || /^Scroll of Enrage/i.test(src);
            const scroll = enrage || (a.sp && a.sp.kind === "scroll") || /^Scroll of/i.test(src);
            const bad = a.cl.filter(c => c.k === "debuff" || c.k === "dot").length;
            if (scroll) a.cat = "scroll";
            else if (POTION_RE.test(a.name)) a.cat = "potion";
            else if ((a.sp && a.sp.kind === "noheal") || (a.cl.length && bad * 2 > a.cl.length)
                || (a.who === "me" && a.casterType === "m")) a.cat = "debuff";
            else a.cat = "buff";
            a.group = enrage ? "enrage" : scroll ? "scrolls" : a.cat === "potion" ? "potions" : a.cat === "debuff" ? "debuffs" : "other";
            a.pin = pinIndex(a, pins, text);
        }
        const live = list.filter(a => a.left === null || a.left > -1);
        // Heal-blocking auras (NO HEAL: Decay, Noxious Decay) lead their group.
        const rank = a => (a.sp && a.sp.kind === "noheal" ? -1 : a.sp ? 0 : a.full ? 1 : a.stacks ? 2 : 3);
        // Focus (Scroll of Enrage's taunt) is the built-in first pin: above user pins and every group.
        const tier = a => ((a.sp && a.sp.tag === "TAUNT") || (a.group === "enrage" && /^focus$/i.test(a.name)) ? -1 : a.pin >= 0 ? a.pin : pins.length + order.indexOf(a.group));
        live.sort((x, y) => tier(x) - tier(y) || rank(x) - rank(y) || y.seq - x.seq);
        return live;
    }

    function passes(a, f) {
        return f === "buff" ? a.cat === "buff" : f === "debuff" ? a.cat === "debuff" : f === "sp" ? (a.cat === "scroll" || a.cat === "potion") : true;
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

    // What the aura gives, shortest reliable source first: the effect line pulled from the caster's
    // class (tools/build_aura_effects.py), a forge/scroll proc's effects, the server's own description,
    // and only then the skill sentence that names it.
    function summary(a) {
        if (a.fx) return a.fx.e;
        if (a.enh) return a.enh.t.replace(/;\s*/g, ", ");
        const d = descs[a.name];
        if (d) return String(d).replace(/<[^>]*>/g, " ").replace(/\s+/g, " ").trim();
        if (a.wiki) return a.wiki.text;
        return a.sp ? a.sp.note : "";
    }

    function rowHtml(a) {
        const cls = ["row", a.sp ? a.sp.kind : "", a.full ? "max" : ""].filter(Boolean).join(" ");
        const url = iconUrl(a.icon);
        const icon = url ? `<span class="ic" style="background-image:url(${url})"></span>`
            : `<span class="ic">${esc(a.name.replace(/^[IVXLC0-9]+\s*-\s*/, "").charAt(0).toUpperCase())}</span>`;
        const sum = summary(a);
        const by = byLine(a);
        let info = "";
        if (open.has(a.key)) {
            const cs = a.casters || [];
            const lines = [`<b>${cs.length > 1 ? "Applied by (latest first)" : "Applied by"}:</b> ${esc(sourceOf(a))}`];
            if (a.stacks) {
                lines.push(`<b>Stacks:</b> ${a.n}${a.max > 1 ? " of " + a.max + (a.full ? " (max)" : "")
                    : a.nocap ? " (no cap)" : " (limit unknown; most seen " + a.most + ")"}`);
            }
            lines.push(`<b>Duration:</b> ${a.dur > 0 ? a.dur + "s" : "until removed"}`);
            if (sum) lines.push(`<b>Gives:</b> <span class="wd">${esc(sum)}</span>`);
            const from = a.fx ? a.fx.from + " (" + a.fxCls + ")" : a.wiki && a.wiki.from ? a.wiki.from + " (" + a.fxCls + ")" : a.enh ? a.enh.src : "";
            if (from) lines.push(`<i>From ${esc(from)}, AQW Wiki</i>`);
            if (a.sp && sum !== a.sp.note) lines.push(esc(a.sp.note));
            info = `<div class="info">${lines.join("<br>")}</div>`;
        }
        const scroll = a.cat === "scroll";
        const pills = (scroll ? [`<span class="pl" style="--c:${PILL.scroll}">Scroll</span>`] : [])
            .concat(a.cl.slice(0, 6).map(c => `<span class="pl" style="--c:${scroll ? PILL.scroll : PILL[c.k]}">${esc(c.t)}</span>`)).join("");
        const byHtml = by ? `<span class="by${by === "You" ? " you" : ""}">${esc(by)}</span>` : "";
        return `<div class="${cls}" data-k="${esc(a.key)}"><div class="top">${icon}` +
            `<span class="nm">${a.pin >= 0 ? "📌 " : ""}${esc(a.name)}</span>` +
            (a.sp ? `<span class="tag">${a.sp.tag}</span>` : "") + byHtml +
            `<span class="st">${stackText(a)}</span><span class="tm"></span></div>` +
            (pills && !open.has(a.key) ? `<div class="pls">${pills}</div>` : "") +
            `<div class="bar"></div>${info}</div>`;
    }

    function section(title, list, side) {
        const f = store.get("auraFilter." + side, "all");
        const shown = list.filter(a => passes(a, f));
        const chips = `<div class="flt">` + FILTERS.map(([k, n]) => `<span class="fc${k === f ? " on" : ""}" data-f="${side}:${k}">${n}</span>`).join("") + `</div>`;
        const rows = shown.slice(0, MAX_ROWS).map(rowHtml).join("");
        const more = shown.length > MAX_ROWS ? `<div class="more">+${shown.length - MAX_ROWS} more</div>` : "";
        return `<div class="sh">${esc(title)}</div>${chips}${rows || '<div class="none">No auras</div>'}${more}`;
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
            // Ten times a second for every row: write only what changed, and move the bar with a
            // transform, so the page doesn't lay out and repaint on the thread the game runs on
            // (redrawing widths and text cost ~5 fps in combat on the Poco X6 Pro).
            const txt = node.classList.contains("chip") ? (a.left != null ? " " + Math.ceil(Math.max(0, a.left)) + "s" : "") : fmtTime(a.left);
            if (tm && tm.textContent !== txt) tm.textContent = txt;
            const bar = node.querySelector(".bar");
            if (bar) {
                const frac = a.left != null && a.dur > 0 ? Math.max(0, Math.min(1, a.left / a.dur)) : 0;
                const sx = "scaleX(" + frac.toFixed(3) + ")";
                if (bar.style.transform !== sx) bar.style.transform = sx;
                bar.classList.toggle("low", a.left != null && a.left < 3);
            }
        }
    }

    function draw() {
        if (paused()) return;
        const W = windows();
        if (!enabled()) {
            for (const w of Object.values(W)) { w.el.style.display = "none"; w.pill.style.display = "none"; }
            return;
        }
        iconBudget = 2;
        const now = Date.now();
        const mine = decorate(state.mine, now), theirs = decorate(state.theirs, now);
        const split = store.get("auraSplit", true);
        const hasMe = !!state.me;
        const tTitle = state.target ? "Target: " + state.target.name : "Target";
        const meHtml = section("You", mine, "me"), tHtml = section(tTitle, theirs, "t");
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
            gate.me.at = gate.t.at = 0;
            poll();
            draw();
        }
    };

    // Game reads go through Ruffle's ExternalInterface on the thread the game runs on. Each poll is
    // scheduled only after the previous one finishes, waiting max(POLL_MS, 20 × its duration) capped
    // at 1 s, so reads stay under ~5% of the time and back off when the game is busy (same rule as
    // desktop VibeSkua). window.__mvsAuraStats keeps the timings for measuring on a phone.
    const stats = window.__mvsAuraStats = { polls: 0, pollMs: 0, pollMax: 0, draws: 0, drawMs: 0, drawMax: 0, wait: POLL_MS, since: Date.now() };
    function loopPoll() {
        const t0 = performance.now();
        try { poll(); } catch (e) { }
        const d = performance.now() - t0;
        stats.polls++; stats.pollMs += d; stats.pollMax = Math.max(stats.pollMax, d);
        stats.wait = Math.min(1000, Math.max(POLL_MS, 20 * d));
        setTimeout(loopPoll, stats.wait);
    }
    function loopDraw() {
        const t0 = performance.now();
        try { draw(); } catch (e) { }
        const d = performance.now() - t0;
        stats.draws++; stats.drawMs += d; stats.drawMax = Math.max(stats.drawMax, d);
        setTimeout(loopDraw, Math.min(1000, Math.max(DRAW_MS, 20 * d)));
    }
    setTimeout(loopPoll, POLL_MS);
    setTimeout(loopDraw, DRAW_MS);
})();
