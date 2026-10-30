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
        if (name === "loaded") setStatus("");
        send({ t: "ext", n: String(name), a: Array.from(args || []) });
        return undefined;
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
        connect();
        let cfg = { socketProxy: [], quality: "medium", frameRate: 0 };
        try {
            cfg = await (await nativeFetch(base + "config.json")).json();
        } catch (e) {
            log("config load failed: " + e);
        }

        window.addEventListener("error", e => log("page error: " + e.message));
        window.addEventListener("unhandledrejection", e => log("unhandled rejection: " + (e.reason && e.reason.message || e.reason)));

        if (cfg.maxPixelRatio > 0 && window.devicePixelRatio > cfg.maxPixelRatio) {
            const cap = cfg.maxPixelRatio;
            Object.defineProperty(window, "devicePixelRatio", { get: () => cap, configurable: true });
        }

        setStatus("Loading game client...");
        const ruffle = window.RufflePlayer.newest();
        player = ruffle.createPlayer();
        document.getElementById("player").appendChild(player);

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
