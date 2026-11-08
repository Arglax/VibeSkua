// Glue between Ruffle and the bot. Talks to RuffleHost over the control WebSocket.
(function () {
    "use strict";

    const base = location.href.replace(/[?#].*$/, "").replace(/[^/]*$/, "");
    const statusEl = () => document.getElementById("status");
    let socket = null;
    let player = null;
    let backlog = [];

    // Ruffle loads everything with fetch(). Send cross-origin requests through the host proxy so
    // the game servers' missing CORS headers do not matter.
    const nativeFetch = window.fetch.bind(window);
    function proxied(url) {
        let u;
        try { u = new URL(url, location.href); } catch (e) { return null; }
        if (u.origin === location.origin) return null;
        if (u.protocol !== "http:" && u.protocol !== "https:") return null;
        return base + "p/" + u.protocol.slice(0, -1) + "/" + u.host + u.pathname + u.search;
    }
    window.fetch = async function (input, init) {
        if (typeof input === "string" || input instanceof URL) {
            return nativeFetch(proxied(String(input)) || input, init);
        }
        if (input instanceof Request) {
            const target = proxied(input.url);
            if (target) {
                const opts = { method: input.method, headers: input.headers, credentials: "omit", cache: input.cache, redirect: "follow" };
                if (input.method !== "GET" && input.method !== "HEAD") opts.body = await input.arrayBuffer();
                return nativeFetch(target, Object.assign(opts, init || {}));
            }
        }
        return nativeFetch(input, init);
    };

    // Graphics & Performance settings from MVS: render resolution, anti-aliasing, colour style and an
    // FPS meter. Kept here so a reloaded page or a later game load gets them again.
    const nativeDpr = window.devicePixelRatio || 1;
    let gfx = { dpr: 0, quality: "", filter: "natural", fps: false, auto: true, heat: true, smooth: 0 };
    let screenRatioCap = 0;
    const colourFilters = {
        natural: "",
        vivid: "saturate(1.3) contrast(1.08)",
        cinematic: "contrast(1.15) saturate(1.12) brightness(0.95)",
        bright: "brightness(1.12) contrast(1.05)"
    };

    function setPixelRatio(ratio, scale) {
        // 0 = the screen's own ratio; a negative value supersamples above it (sharper edges, more GPU).
        let want = ratio > 0 ? ratio : ratio < 0 ? Math.min(nativeDpr * -ratio, 3.5) : nativeDpr;
        if (ratio === 0 && screenRatioCap > 0) want = Math.min(want, screenRatioCap);
        // The frame governor's step down (never below 1x).
        if (scale && scale < 1) want = Math.max(1, want * scale);
        want = Math.round(want * 100) / 100;
        if (window.devicePixelRatio === want) return;
        Object.defineProperty(window, "devicePixelRatio", { get: () => want, configurable: true });
        // Ruffle reads the ratio when its element resizes: nudge it by one pixel for a frame.
        const el = player;
        if (!el) return;
        el.style.width = "calc(100% - 1px)";
        requestAnimationFrame(() => requestAnimationFrame(() => { el.style.width = ""; }));
    }

    // Touch-friendly item lists on phones (config touchRowH > 0): Ruffle replaces the row height every
    // AQW list window uses (set before the game loads, see boot), and skua.swf enlarges the inventory's
    // category icons and lets them scroll. The same switch turns on skua.swf's layout for the game's
    // touch controls (MobileHudUI) while the game's touch mode is on.
    let touchRowH = 0;
    function applyTouchUi() {
        if (touchRowH <= 0) return;
        try { player.ruffle().callExternalInterface("mvsTouchUi", true); } catch (e) { log("touch ui: " + e); }
    }

    function applyQuality() {
        // stage.quality also sets Ruffle's anti-aliasing (MSAA). Empty = leave it to the game's own switch.
        const q = governedQuality();
        if (!q || !player) return;
        try { player.ruffle().callExternalInterface("setGameObject", "stage.quality", q); } catch (e) { }
    }

    // ---------------- MVS options (the game's Options, "MVS" rows) ----------------
    // skua.swf (MobileHudUI) owns them in the game's settings and tells the page each value through
    // ExternalInterface "mvsOption" ("key:on" / "key:off") when the game starts and when one is switched.
    // A copy is kept here for what has to be decided before the game loads (Improve FPS, Long Dash).
    function mvsOptStored(key) {
        try { return localStorage.getItem("mvs.opt." + key) !== "off"; } catch (e) { return true; }
    }
    window.__mvsOpts = {
        mvsImproveFps: mvsOptStored("mvsImproveFps"), mvsLongDash: mvsOptStored("mvsLongDash"),
        mvsSwipe: mvsOptStored("mvsSwipe"), mvsAuras: mvsOptStored("mvsAuras"), mvsDrops: mvsOptStored("mvsDrops")
    };
    window.__mvsSwipeScroll = window.__mvsOpts.mvsSwipe;

    function onMvsOption(key, value) {
        const on = String(value) !== "off";
        try { localStorage.setItem("mvs.opt." + key, on ? "on" : "off"); } catch (e) { }
        const was = window.__mvsOpts[key];
        window.__mvsOpts[key] = on;
        if (key === "mvsSwipe") window.__mvsSwipeScroll = on;
        if (key === "mvsImproveFps") {
            window.__mvsFreezeOrphans = on;
            if (!on && gov.level !== 0) setLevel(0, "Improve FPS off");
            applySmooth();
        }
        if (key === "mvsLongDash" && was !== undefined && was !== on) log("Long Dash " + (on ? "on" : "off") + ": takes effect the next time the game starts");
        try { window.dispatchEvent(new CustomEvent("mvsoption", { detail: { key, on } })); } catch (e) { }
    }

    // ---------------- MVS frame governor ----------------
    // Keeps the game at its own frame rate: when the measured rate stays below it (a crowded map, a
    // heavy menu, a hot phone), it lowers anti-aliasing first, then render resolution, one step at a
    // time; when there is room again it steps back up to the Graphics page's own settings (level 0).
    // A hot phone also sets a floor, so it cools down before Android slows the whole phone.
    const aaLadder = ["8X8", "HIGH", "MEDIUM", "LOW"];
    const govLevels = [
        { smooth: true, aa: 0, res: 1 }, { smooth: false, aa: 0, res: 1 }, { smooth: false, aa: 1, res: 1 },
        { smooth: false, aa: 2, res: 1 }, { smooth: false, aa: 2, res: 0.8 }, { smooth: false, aa: 3, res: 0.8 },
        { smooth: false, aa: 3, res: 0.65 }
    ];
    // Ruffle MVS switch: clips the game took off the stage (old monsters after each respawn, spent skill
    // effects, avatars of players who left) are invisible but Flash keeps animating them until memory is
    // cleaned up; on a busy map that is thousands of objects every frame. Leave them as they are.
    window.__mvsFreezeOrphans = window.__mvsOpts.mvsImproveFps;
    const gov = {
        level: 0, floor: 0, low: 0, good: 0, settle: 0, gameRate: 30, rateAt: 0,
        upWait: 30, failedUpAt: 0, heat: "ok", lastFps: 0, gameQuality: "",
        trial: null, gpuBlockUntil: 0, recent: [],
        throttled: false, smoothPoor: 0, smoothPausedUntil: 0
    };

    function governedQuality() {
        // "Game setting" (empty) means the game's own quality, saved when the governor first steps down.
        const own = gfx.quality || gov.gameQuality;
        if (!gfx.auto || gov.level === 0) return own;
        let base = aaLadder.indexOf(own || "HIGH");
        if (base < 0) base = 1;
        return aaLadder[Math.min(aaLadder.length - 1, base + govLevels[gov.level].aa)];
    }

    function governedScale() {
        return gfx.auto ? govLevels[gov.level].res : 1;
    }

    // In-betweening (Ruffle's MVS patch): draws up to gfx.smooth pictures a second between game frames.
    function governedSmooth() {
        if (!(gfx.smooth > 0) || window.__mvsNoDraw || !window.__mvsOpts.mvsImproveFps) return 0;
        if (!gfx.auto) return gfx.smooth;
        if (window.__mvsNoDraw || !govLevels[gov.level].smooth || gov.throttled || Date.now() < gov.smoothPausedUntil) return 0;
        return gfx.smooth;
    }

    // In-betweening costs processor time, and a throttled phone has none to spare: it then adds few
    // pictures, costs game frames and heats the phone further. Pause it while Android's throttling
    // forecast is past 1.0 (MIUI caps the clocks there while the battery still reads ~43 C), and for a
    // minute whenever it adds fewer than 15 pictures a second for 5 seconds.
    function checkSmooth(fps, drawn) {
        if (!gfx.auto || !(gfx.smooth > 0)) return;
        if (Number(window.__mvsInterp) > 0 && drawn >= 0) {
            gov.smoothPoor = drawn - fps < 15 ? gov.smoothPoor + 1 : 0;
            if (gov.smoothPoor >= 5) {
                gov.smoothPoor = 0;
                gov.smoothPausedUntil = Date.now() + 60000;
                log("governor: in-betweening paused for a minute (" + drawn + " pictures for " + fps + " game frames)");
            }
        } else {
            gov.smoothPoor = 0;
        }
        applySmooth();
    }

    function applySmooth() {
        window.__mvsInterp = governedSmooth();
    }

    function levelKey(level) {
        const saved = gov.level;
        gov.level = level;
        const key = governedSmooth() + "|" + governedQuality() + "|" + governedScale();
        gov.level = saved;
        return key;
    }

    function setLevel(level, why) {
        level = Math.max(gov.floor, Math.min(govLevels.length - 1, level));
        if (level === gov.level) return;
        const now = levelKey(gov.level), step = level > gov.level ? 1 : -1;
        while (level > gov.floor && level < govLevels.length - 1 && levelKey(level) === now) level += step;
        const up = level < gov.level;
        if (gov.level === 0 && !gfx.quality) {
            try { gov.gameQuality = String(JSON.parse(player.ruffle().callExternalInterface("getGameObject", "stage.quality"))).toUpperCase(); }
            catch (e) { gov.gameQuality = ""; }
        }
        gov.level = level;
        gov.low = 0; gov.good = 0; gov.settle = 3;
        setPixelRatio(gfx.dpr, governedScale());
        applyQuality();
        applySmooth();
        log("governor: level " + level + " (" + why + "; " + gov.lastFps + " fps of " + gov.gameRate +
            ", aa " + (governedQuality() || "game") + ", ratio " + window.devicePixelRatio + ", in-between " + governedSmooth() + ")");
        if (up) gov.failedUpAt = Date.now();
    }

    // A level that lowers anti-aliasing or resolution (the in-betweening step is 1).
    function isGpuStep(level) {
        return level >= 2;
    }

    function recentFps() {
        const r = gov.recent;
        return r.length ? r.reduce((a, b) => a + b, 0) / r.length : gov.lastFps;
    }

    function readGameRate() {
        try {
            const v = Number(JSON.parse(player.ruffle().callExternalInterface("getGameObject", "stage.frameRate")));
            if (v >= 10 && v <= 120) gov.gameRate = v;
        } catch (e) { }
    }

    function governorTick(fps) {
        gov.lastFps = fps;
        if (!gfx.auto || !player || document.hidden || window.__mvsNoDraw || player.style.visibility === "hidden") return;
        if (!window.__mvsOpts.mvsImproveFps) return;
        if (Date.now() - gov.rateAt > 15000) { gov.rateAt = Date.now(); readGameRate(); }
        if (gov.settle > 0) { gov.settle--; return; }
        gov.recent.push(fps);
        if (gov.recent.length > 4) gov.recent.shift();
        // Did the last step down help? On a phone whose processor is the limit (the game runs on one
        // browser thread), lower anti-aliasing or resolution gains nothing and only looks worse: put
        // the quality back and leave it for two minutes.
        if (gov.trial) {
            gov.trial.samples.push(fps);
            if (gov.trial.samples.length >= 4) {
                const t = gov.trial, avg = t.samples.reduce((a, b) => a + b, 0) / t.samples.length;
                gov.trial = null;
                if (t.gpu && avg < t.before + 2 && t.from >= gov.floor) {
                    gov.gpuBlockUntil = Date.now() + 120000;
                    setLevel(t.from, "lower graphics gained nothing (" + avg.toFixed(1) + " vs " + t.before.toFixed(1) + " fps)");
                    return;
                }
            }
        }
        // AQW's own "Max FPS" option can run the game at 60. Above 30 the game's logic, not drawing,
        // sets the pace on phones, so lower anti-aliasing or resolution would only cost looks: the
        // graphics are lowered only to hold the game's classic 30.
        const want = Math.min(gov.gameRate, 30);
        if (fps < want * 0.85) {
            gov.good = 0;
            // A step down after 3 slow seconds in a row.
            if (++gov.low >= 3) {
                // Stepping up just failed: wait longer before the next try.
                if (Date.now() - gov.failedUpAt < 20000) gov.upWait = Math.min(gov.upWait * 2, 600);
                const from = gov.level;
                if (isGpuStep(from + 1) && Date.now() < gov.gpuBlockUntil) { gov.low = 0; return; }
                const before = recentFps();
                setLevel(from + 1, "slow");
                if (gov.level !== from) gov.trial = { from, before, gpu: isGpuStep(gov.level), samples: [] };
            }
        } else {
            gov.low = 0;
            if (fps >= want * 0.95 && gov.level > gov.floor && ++gov.good >= gov.upWait) setLevel(gov.level - 1, "room to spare");
        }
    }

    // Phone temperature from ThermalMonitor.cs: Android's thermal status (0 none .. 6 shutdown), its
    // 10-second headroom forecast (1.0 = throttling starts) and the battery temperature in Celsius.
    let lastThermal = null;
    function setThermal(msg) {
        lastThermal = msg;
        const status = Number(msg.status), headroom = Number(msg.headroom), battery = Number(msg.battery);
        // A margin on the way down, so readings hovering at a line do not flip the warning on and off.
        const was = gov.heat;
        const hot = status >= 3 || battery >= 47 || (was === "hot" && battery >= 45);
        // Android's 10-second forecast (headroom) passes 1.0 on this kind of phone even at 34 C, so it
        // does not warn on its own: the warning is about nearing the 47 C cool-down.
        const warm = status >= 2 || battery >= 45 || (was !== "ok" && battery >= 43.5);
        const heat = hot ? "hot" : warm ? "warm" : "ok";
        if (heat !== gov.heat) log("thermal: " + heat + " (status " + status + ", headroom " + headroom + ", battery " + battery + " C)");
        gov.heat = heat;
        // -1: Android had no forecast this time (asked too soon after the last one).
        if (headroom >= 0) {
            const was = gov.throttled;
            gov.throttled = was ? headroom >= 0.9 : headroom >= 1;
            if (was !== gov.throttled) {
                log("governor: phone " + (gov.throttled ? "throttling" : "no longer throttling") + " (headroom " + headroom + "), in-betweening " + (gov.throttled ? "paused" : "back"));
                applySmooth();
            }
        }
        // Warm only warns (full speed up to 47 C); hot sets the cooler mode's floor: no in-betweening,
        // lighter anti-aliasing and 80% resolution, so the GPU and the phone run cooler.
        gov.floor = gfx.auto && heat === "hot" ? 4 : 0;
        if (gov.floor > 0) gov.trial = null;
        if (gov.level < gov.floor) setLevel(gov.floor, "phone " + heat);
        if (window.MvsOverlays && window.MvsOverlays.heat)
            window.MvsOverlays.heat(gfx.heat ? { heat, battery, status, headroom, lowered: gfx.auto && gov.floor > 0 } : { heat: "ok" });
    }

    let fpsHooked = false, drawCalls = 0, framesDrawn = 0, fpsEl = null, fpsInterval = 0;
    function hookDraws() {
        if (fpsHooked) return;
        fpsHooked = true;
        for (const ctx of [window.WebGLRenderingContext, window.WebGL2RenderingContext]) {
            if (!ctx) continue;
            for (const name of ["drawElements", "drawArrays"]) {
                const original = ctx.prototype[name];
                ctx.prototype[name] = function () { drawCalls++; return original.apply(this, arguments); };
            }
        }
    }

    // Frames are always counted (the governor needs them); the meter only shows them.
    function startFrameCounter() {
        if (fpsInterval) return;
        // Game frames come from Ruffle (window.__mvsFrames); without them, count drawn pictures.
        if (!(Number(window.__mvsFrames) >= 0)) hookDraws();
        // A game frame counts once per display frame in which Ruffle drew anything.
        const tick = () => {
            if (drawCalls > 0) { framesDrawn++; drawCalls = 0; }
            requestAnimationFrame(tick);
        };
        requestAnimationFrame(tick);
        let lastGameFrames = -1, lastDrawn = -1;
        fpsInterval = setInterval(() => {
            let drawn = framesDrawn;
            framesDrawn = 0;
            const pictures = Number(window.__mvsDrawn);
            if (pictures >= 0) {
                if (lastDrawn >= 0) drawn = pictures - lastDrawn;
                lastDrawn = pictures;
            }
            const total = Number(window.__mvsFrames);
            const fps = total >= 0 && lastGameFrames >= 0 ? total - lastGameFrames : drawn;
            if (total >= 0) lastGameFrames = total;
            if (fpsEl && !window.__mvsNoDraw) {
                // Players see the frame rate; developer mode adds pictures drawn and Auto smooth's step.
                fpsEl.textContent = fps + " fps" + (gfx.dev
                    ? (governedSmooth() && drawn > fps ? " \u00B7 " + drawn + " drawn" : "") + (gfx.auto && gov.level > 0 ? " \u2193" + gov.level : "")
                    : "");
                fpsEl.style.color = fps >= gov.gameRate * 0.95 ? "#7CFC7C" : fps >= gov.gameRate * 0.7 ? "#FFD34D" : "#FF6B6B";
            }
            try { governorTick(fps); } catch (e) { log("governor: " + e); }
            try { if (!document.hidden) checkSmooth(fps, drawn); } catch (e) { log("smooth: " + e); }
        }, 1000);
    }

    function setFpsMeter(on) {
        startFrameCounter();
        // Counting draw calls costs a little on every one of them: only while the meter shows.
        if (on) hookDraws();
        if (!on) {
            if (fpsEl) { fpsEl.remove(); fpsEl = null; }
            return;
        }
        if (fpsEl) return;
        fpsEl = document.createElement("div");
        fpsEl.style.cssText = "position:fixed;left:6px;top:6px;z-index:10;padding:2px 6px;border-radius:4px;" +
            "font:bold 13px monospace;color:#7CFC7C;background:rgba(0,0,0,0.55);pointer-events:none";
        fpsEl.textContent = "-- fps";
        document.body.appendChild(fpsEl);
    }

    function applyGraphics() {
        if (!gfx.auto) { gov.level = 0; gov.floor = 0; }
        setPixelRatio(gfx.dpr, governedScale());
        applySmooth();
        if (player) player.style.filter = colourFilters[gfx.filter] || "";
        applyQuality();
        setFpsMeter(!!gfx.fps);
    }

    function setGraphics(msg) {
        gfx = {
            dpr: Number(msg.dpr) || 0,
            quality: String(msg.quality || ""),
            filter: String(msg.filter || "natural"),
            fps: !!msg.fps,
            auto: msg.auto !== false,
            smooth: Math.max(0, Number(msg.smooth) || 0),
            dev: !!msg.dev,
            heat: msg.heat !== false
        };
        try { applyGraphics(); } catch (e) { log("graphics: " + e); }
        if (lastThermal) { try { setThermal(lastThermal); } catch (e) { } }
    }

    function send(msg) {
        const text = JSON.stringify(msg);
        if (socket && socket.readyState === 1) socket.send(text);
        else if (backlog.length < 500) backlog.push(text);
    }

    function log(message) {
        console.log("[skua]", message);
        send({ t: "log", m: String(message) });
    }

    function setStatus(text) {
        const el = statusEl();
        if (!el) return;
        el.textContent = text || "";
        el.style.display = text ? "block" : "none";
    }

    // Called by the patched ruffle.js for every ExternalInterface.call from the SWF.
    window.__skuaExt = function (name, args) {
        if (name === "loaded") { setStatus(""); setTimeout(applyQuality, 0); setTimeout(applyTouchUi, 0); }
        if (name === "mvsMenu") { try { window.MvsOverlays.menu(args); } catch (e) { log("menu: " + e); } }
        if (name === "mvsDrops") { try { window.MvsOverlays.dropsList(args); } catch (e) { log("drops: " + e); } }
        if (name === "mvsOption") {
            try {
                let raw = args;
                while (Array.isArray(raw)) raw = raw[0];
                const m = /^(\w+):(on|off)$/.exec(String(raw));
                if (m) onMvsOption(m[1], m[2]);
            } catch (e) { log("option: " + e); }
        }
        send({ t: "ext", n: String(name), a: Array.from(args || []) });
        return undefined;
    };

    // Ruffle types through a hidden <input> that it focuses when a Flash text field takes focus. A tap
    // on the game then focuses the player container (the browser does it when the tap ends), which
    // pulls focus off that input: the keyboard closes, or stays up typing into nothing. Keep the input
    // focused while Flash's text field has focus, and ask the app to show the keyboard, since a field
    // tapped again after the keyboard was dismissed has nothing new to focus.
    // The patched ruffle.js calls these instead of its own methods.
    let keyboardWanted = false;
    function focusKeyboard(p) {
        setTimeout(() => {
            if (!keyboardWanted) return;
            if (!p.isVirtualKeyboardFocused()) p.virtualKeyboard.focus({ preventScroll: true });
            send({ t: "keyboard" });
        }, 0);
    }
    window.__skuaKeyboard = {
        openVirtualKeyboard(p) {
            keyboardWanted = true;
            if (!p.__skuaKeyboard) {
                p.__skuaKeyboard = true;
                p.container.addEventListener("mousedown", e => {
                    if (keyboardWanted && p.isVirtualKeyboardFocused()) e.preventDefault();
                });
                p.container.addEventListener("focus", () => focusKeyboard(p));
            }
            focusKeyboard(p);
        },
        closeVirtualKeyboard(p) {
            keyboardWanted = false;
            if (p.isVirtualKeyboardFocused()) p.container.focus({ preventScroll: true });
        }
    };

    // Streamer Mode: show a stand-in name and guild on the player's own name tag and portraits.
    // AQW redraws them on every map join, so they are checked a few times a second, here in the page,
    // instead of being set once from the app.
    let streamer = null;
    let streamerShown = "";
    let streamerTimer = 0;
    function applyStreamer(on) {
        const r = player && player.ruffle();
        if (!r || r.callExternalInterface("isNull", "world.myAvatar.pMC.pname") !== "false") return;
        const get = path => { try { return JSON.parse(r.callExternalInterface("getGameObject", path)); } catch (e) { return null; } };
        const set = (path, text) => {
            const current = get(path);
            if (typeof current === "string" && current !== text) r.callExternalInterface("setGameObject", path, text);
        };
        const real = String(get("world.myAvatar.objData.strUsername") || "");
        if (!real) return;
        const guild = get("world.myAvatar.objData.guild.Name");
        const name = on ? streamer.name : real;
        const tag = get("world.myAvatar.pMC.pname.ti.text");
        set("world.myAvatar.pMC.pname.ti.text", (String(tag).startsWith("<AFK> ") ? "<AFK> " : "") + name);
        set("world.myAvatar.pMC.pname.tg.text", on ? "< " + streamer.guild + " >" : guild ? "< " + guild + " >" : "");
        set("world.rootClass.ui.mcPortrait.strName.text", name.toUpperCase());
        // The target portrait shows the player too while they target themselves.
        const shown = on ? real : streamerShown;
        if (shown && get("world.rootClass.ui.mcPortraitTarget.strName.text") === shown.toUpperCase())
            set("world.rootClass.ui.mcPortraitTarget.strName.text", name.toUpperCase());
    }
    function setStreamer(msg) {
        const next = msg.on ? { name: String(msg.name), guild: String(msg.guild) } : null;
        if (JSON.stringify(next) === JSON.stringify(streamer)) return;
        streamer = next;
        clearInterval(streamerTimer);
        streamerTimer = 0;
        // skua.swf (MobileHudUI) does the masking every frame while litePreference.data.mvsStreamer is
        // fresh: names, portraits, chat and the house title. Kept fresh once a second; a stale value
        // (page gone) counts as off. Masking from here a few times a second let the real name flash.
        const beat = () => {
            const r = player && player.ruffle();
            if (!r) return;
            r.callExternalInterface("setGameObject", "litePreference.data.mvsStreamer",
                streamer ? streamer.name + "	" + streamer.guild + "	" + Date.now() : "");
        };
        try {
            if (streamer) {
                streamerShown = streamer.name;
                beat();
                streamerTimer = setInterval(() => { try { beat(); } catch (e) { } }, 1000);
            } else {
                beat();
                applyStreamer(false);
            }
        } catch (e) { log("streamer: " + e); }
    }

    // Silent while any reason holds (page hidden, app in the background); the volume comes back after.
    const muteReasons = new Set();
    let mutedVolume = null;
    function setMuted(reason, on) {
        try {
            if (on) muteReasons.add(reason); else muteReasons.delete(reason);
            const r = player && player.ruffle();
            if (!r) return;
            if (muteReasons.size > 0) {
                if (mutedVolume === null) { mutedVolume = r.volume; r.volume = 0; }
            } else if (mutedVolume !== null) {
                r.volume = mutedVolume;
                mutedVolume = null;
            }
        } catch (e) { log("mute: " + e); }
    }

    // Ruffle draws nothing (window.__mvsNoDraw) in bot-only mode and while the app is in the
    // background: the app keeps the game view running there so the bot is not frozen, which also
    // means the page never learns it is out of sight.
    let appInBackground = false;
    window.__mvsUpdateNoDraw = function () {
        window.__mvsNoDraw = !!(window.__mvsBotOnly || appInBackground);
        applySmooth();
    };

    function handle(msg) {
        switch (msg.t) {
            case "call": {
                let reply;
                try {
                    if (!player) throw new Error("player not ready");
                    const v = player.ruffle().callExternalInterface(msg.f, ...(msg.a || []));
                    reply = { t: "ret", id: msg.id, v: v === undefined ? null : v };
                } catch (e) {
                    reply = { t: "ret", id: msg.id, e: String((e && e.message) || e) };
                }
                send(reply);
                break;
            }
            case "batch": {
                // Several read calls in one round trip (the skill loop's reads, fetched ahead).
                let reply;
                try {
                    if (!player) throw new Error("player not ready");
                    const r = player.ruffle();
                    const out = (msg.c || []).map(c => {
                        try {
                            const v = r.callExternalInterface(c.f, ...(c.a || []));
                            return { v: v === undefined ? null : v };
                        } catch (e) {
                            return { e: String((e && e.message) || e) };
                        }
                    });
                    reply = { t: "ret", id: msg.id, v: out };
                } catch (e) {
                    reply = { t: "ret", id: msg.id, e: String((e && e.message) || e) };
                }
                send(reply);
                break;
            }
            case "botOnly":
                try { window.MvsOverlays.botOnly(!!msg.v, false); } catch (e) { log("botOnly: " + e); }
                window.__mvsUpdateNoDraw();
                break;
            case "background":
                appInBackground = !!msg.v;
                window.__mvsUpdateNoDraw();
                setMuted("app", appInBackground);
                // The page stays "visible" (the app keeps it so, or Chromium suspends it); have Ruffle
                // tick from its background worker as for a hidden tab (patched in RuffleHost).
                if (!!window.__mvsBackground !== appInBackground) {
                    window.__mvsBackground = appInBackground;
                    try { document.dispatchEvent(new Event("visibilitychange")); } catch (e) { log("background: " + e); }
                }
                break;
            case "reload":
                location.reload();
                break;
            case "quality":
                try { player.ruffle().config = Object.assign({}, player.ruffle().config, { quality: msg.v }); } catch (e) { log(e); }
                break;
            case "visible":
                document.getElementById("player").style.visibility = msg.v ? "visible" : "hidden";
                break;
            case "eval":
                try { (0, eval)(msg.code); } catch (e) { log(e); }
                break;
            case "streamer":
                setStreamer(msg);
                break;
            case "graphics":
                setGraphics(msg);
                break;
            case "drops":
                try { window.MvsOverlays.toggleDrops(); } catch (e) { log("drops: " + e); }
                break;
            case "thermal":
                try { setThermal(msg); } catch (e) { log("thermal: " + e); }
                break;
            case "scriptStatus":
                try { window.MvsOverlays.scriptStatus(msg); } catch (e) { log("scriptStatus: " + e); }
                break;
        }
    }

    function connect() {
        socket = new WebSocket(base.replace(/^http/, "ws") + "ws/ctl");
        socket.onopen = () => {
            const pending = backlog;
            backlog = [];
            pending.forEach(t => socket.send(t));
        };
        socket.onmessage = e => {
            try { handle(JSON.parse(e.data)); } catch (err) { log("bad message: " + err); }
        };
        socket.onclose = () => setTimeout(connect, 1000);
    }

    async function boot() {
        if (window.MvsOverlays) window.MvsOverlays.init(() => player);
        connect();
        let cfg = { socketProxy: [], quality: "medium", frameRate: 0 };
        try {
            cfg = await (await nativeFetch(base + "config.json")).json();
        } catch (e) {
            log("config load failed: " + e);
        }
        touchRowH = Number(cfg.touchRowH) || 0;
        // The game's touch dash goes 180 px from a stand; the dash it replaced crossed the screen.
        const consts = {};
        if (window.__mvsOpts.mvsLongDash) consts["MoveInput.DASH_PX"] = 540;
        if (touchRowH > 0) {
            consts["LPFFrameInvAQ.ROW_H"] = touchRowH;
            // The touch skill buttons keep the game's size (58 and 80 px before its own Mobile UI Size
            // setting); skua.swf lays them out (see MobileHudUI). SkillCluster.SK_S / AA_S can make
            // them larger. They are private constants, which Ruffle only started replacing on
            // 2026-11-08, so the 100/138 set here before never showed; at 1.7x they covered the
            // field. The user chose to keep the game's size (2026-11-08, after samples).
        }
        window.__mvsAs3Consts = consts;
        // The shared Ruffle fork's draw-call batching made AQW slower (Artix battleon, 10 players:
        // 28-32 fps with it, 41-42 without), so it stays off here.
        window.__arglaxNoBatch = true;

        window.addEventListener("error", e => {
            log("page error: " + e.message);
            // A WebAssembly trap ("unreachable": a Ruffle panic, or its memory running out) leaves the
            // game dead behind Ruffle's error panel. Reload the page so the game (and the bot's
            // auto re-login) can carry on; at most once every 3 minutes, so a crash at start-up
            // can't loop.
            if (/RuntimeError|panicked/.test(String(e.message))) {
                let last = 0;
                try { last = Number(sessionStorage.getItem("mvsCrashReload")) || 0; } catch (err) { }
                if (Date.now() - last > 180000) {
                    try { sessionStorage.setItem("mvsCrashReload", String(Date.now())); } catch (err) { }
                    log("game engine crashed (last memory report: " + (window.__mvsMem || "none") + "); reloading the game");
                    setTimeout(() => location.reload(), 1500);
                }
            }
        });
        // The engine's memory report (window.__mvsMem, refreshed every ~5 s by Ruffle) in the app
        // log once a minute, so a crash can be traced from the log alone.
        setInterval(() => { if (window.__mvsMem) log("memory: " + window.__mvsMem); }, 60000);
        window.addEventListener("unhandledrejection", e => log("unhandled rejection: " + (e.reason && e.reason.message || e.reason)));

        // A hidden game (screen off, or an army client not on screen) keeps playing but stays
        // silent. Ruffle also skips drawing it until it is shown again.
        document.addEventListener("visibilitychange", () => setMuted("page", document.hidden));

        screenRatioCap = cfg.maxPixelRatio > 0 ? cfg.maxPixelRatio : 0;
        if (cfg.maxPixelRatio > 0 && window.devicePixelRatio > cfg.maxPixelRatio) {
            const cap = cfg.maxPixelRatio;
            Object.defineProperty(window, "devicePixelRatio", { get: () => cap, configurable: true });
        }

        setStatus("Loading game client...");
        const ruffle = window.RufflePlayer.newest();
        player = ruffle.createPlayer();
        document.getElementById("player").appendChild(player);
        try { applyGraphics(); } catch (e) { log("graphics: " + e); }

        const options = {
            url: base + "skua.swf",
            autoplay: "on",
            unmuteOverlay: "hidden",
            splashScreen: false,
            contextMenu: "off",
            letterbox: "on",
            scale: "showAll",
            logLevel: "warn",
            allowScriptAccess: true,
            allowNetworking: "all",
            openUrlMode: "allow",
            warnOnUnsupportedContent: false,
            showSwfDownload: false,
            maxExecutionDuration: 30,
            backgroundExecutionMode: "mainThread",
            quality: cfg.quality || "medium",
            socketProxy: cfg.socketProxy || []
        };
        if (cfg.frameRate > 0) options.frameRate = cfg.frameRate;
        if (cfg.preferredRenderer) options.preferredRenderer = cfg.preferredRenderer;

        try {
            await player.ruffle().load(options);
        } catch (e) {
            log("start failed: " + e);
            setStatus("The game couldn't start. Close MVS and open it again.");
            log("load failed: " + e);
        }
    }

    window.SkuaBridge = { boot, log };
})();
