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
    let gfx = { dpr: 0, quality: "", filter: "natural", fps: false };
    let screenRatioCap = 0;
    const colourFilters = {
        natural: "",
        vivid: "saturate(1.3) contrast(1.08)",
        cinematic: "contrast(1.15) saturate(1.12) brightness(0.95)",
        bright: "brightness(1.12) contrast(1.05)"
    };

    function setPixelRatio(ratio) {
        // 0 = the screen's own ratio; a negative value supersamples above it (sharper edges, more GPU).
        let want = ratio > 0 ? ratio : ratio < 0 ? Math.min(nativeDpr * -ratio, 3.5) : nativeDpr;
        if (ratio === 0 && screenRatioCap > 0) want = Math.min(want, screenRatioCap);
        want = Math.round(want * 100) / 100;
        if (window.devicePixelRatio === want) return;
        Object.defineProperty(window, "devicePixelRatio", { get: () => want, configurable: true });
        // Ruffle reads the ratio when its element resizes: nudge it by one pixel for a frame.
        const el = player;
        if (!el) return;
        el.style.width = "calc(100% - 1px)";
        requestAnimationFrame(() => requestAnimationFrame(() => { el.style.width = ""; }));
    }

    function applyQuality() {
        // stage.quality also sets Ruffle's anti-aliasing (MSAA). Empty = leave it to the game's own switch.
        if (!gfx.quality || !player) return;
        try { player.ruffle().callExternalInterface("setGameObject", "stage.quality", gfx.quality); } catch (e) { }
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

    function setFpsMeter(on) {
        if (!on) {
            if (fpsEl) { fpsEl.remove(); fpsEl = null; }
            clearInterval(fpsInterval);
            return;
        }
        if (fpsEl) return;
        hookDraws();
        fpsEl = document.createElement("div");
        fpsEl.style.cssText = "position:fixed;left:6px;top:6px;z-index:10;padding:2px 6px;border-radius:4px;" +
            "font:bold 13px monospace;color:#7CFC7C;background:rgba(0,0,0,0.55);pointer-events:none";
        fpsEl.textContent = "-- fps";
        document.body.appendChild(fpsEl);
        // A game frame counts once per display frame in which Ruffle drew anything.
        const tick = () => {
            if (!fpsEl) return;
            if (drawCalls > 0) { framesDrawn++; drawCalls = 0; }
            requestAnimationFrame(tick);
        };
        requestAnimationFrame(tick);
        fpsInterval = setInterval(() => {
            if (!fpsEl) return;
            fpsEl.textContent = framesDrawn + " fps";
            fpsEl.style.color = framesDrawn >= 28 ? "#7CFC7C" : framesDrawn >= 20 ? "#FFD34D" : "#FF6B6B";
            framesDrawn = 0;
        }, 1000);
    }

    function applyGraphics() {
        setPixelRatio(gfx.dpr);
        if (player) player.style.filter = colourFilters[gfx.filter] || "";
        applyQuality();
        setFpsMeter(!!gfx.fps);
    }

    function setGraphics(msg) {
        gfx = {
            dpr: Number(msg.dpr) || 0,
            quality: String(msg.quality || ""),
            filter: String(msg.filter || "natural"),
            fps: !!msg.fps
        };
        try { applyGraphics(); } catch (e) { log("graphics: " + e); }
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
        if (name === "loaded") { setStatus(""); setTimeout(applyQuality, 0); }
        if (name === "mvsMenu") { try { window.MvsOverlays.menu(args); } catch (e) { log("menu: " + e); } }
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
        try {
            if (streamer) {
                streamerShown = streamer.name;
                applyStreamer(true);
                streamerTimer = setInterval(() => { try { applyStreamer(true); } catch (e) { } }, 250);
            } else {
                applyStreamer(false);
            }
        } catch (e) { log("streamer: " + e); }
    }

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

        window.addEventListener("error", e => log("page error: " + e.message));
        window.addEventListener("unhandledrejection", e => log("unhandled rejection: " + (e.reason && e.reason.message || e.reason)));

        // A hidden game (screen off, or an army client not on screen) keeps playing but stays
        // silent. Ruffle also skips drawing it until it is shown again.
        let hiddenVolume = null;
        document.addEventListener("visibilitychange", () => {
            try {
                const r = player && player.ruffle();
                if (!r) return;
                if (document.hidden) {
                    if (hiddenVolume === null) { hiddenVolume = r.volume; r.volume = 0; }
                } else if (hiddenVolume !== null) {
                    r.volume = hiddenVolume;
                    hiddenVolume = null;
                }
            } catch (e) { log("mute: " + e); }
        });

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
            setStatus("Ruffle failed to start: " + e);
            log("load failed: " + e);
        }
    }

    window.SkuaBridge = { boot, log };
})();
