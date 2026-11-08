// Finger-swipe scrolling for the game's lists (inventory, shops, bank, quests, drops, mail, ...).
//
// AQW's lists scroll with the mouse wheel; its own touch scrolling only runs in its mobile mode,
// which MVS keeps off (parts of it draw with BitmapData.draw, which Ruffle's WebGL renderer can't
// run). So a vertical finger drag that starts on a scrollable window becomes wheel steps at the
// spot where the finger went down, tracking the finger 1:1, with a short fling after release.
//
// Ruffle reports what the finger pressed (window.__mvsPressChain, the AS3 classes from the
// pressed object up to the stage). Drags that start anywhere else (the map, house items being
// moved, windows being dragged) are left alone. Once a drag scrolls, the game no longer sees
// the finger move, and on release the game's pointer is let go outside the stage, so the drag
// can't count as a tap on whatever row or button it started on.
(() => {
    "use strict";

    const STAGE_W = 960, STAGE_H = 550;
    // Stage pixels per wheel line: AQW's lists move ROW_H / 3 = 10 px per line.
    const PX_PER_LINE = 10;
    // How far (CSS px) a finger must move, mostly vertically, before it counts as a swipe.
    const START_PX = 10;
    // Classes whose windows scroll with the wheel.
    const SCROLLERS = /^(LPF\w+|monsterDrops|MailWindow|KeybindsPanel|keybinds|cEntry|\w*ScrollPane|TextArea|List|TileList|DataGrid|TouchSlider|Chat2?|HouseMenu|ItemList|QuestLayout|qRewardPrev|mcOption|QFrameMC|sBar)$/;

    // Windows whose lists the game scrolls by itself while its own touch controls are on (its
    // TouchScroll: inventory, shop, bank, merge and enhancement lists, quests, mail, options, keybinds,
    // drops...). Turning their swipes into wheel steps as well fought the game's scrolling: the
    // off-stage release below read as a huge drag and flung the list to its end every time.
    const GAME_SCROLLS = /^(LPF\w+|MailWindow|monsterDrops|keybinds|KeybindsPanel|mcOption|QFrameMC|OFrameMC|QTrackerMC|FactionsMC|cellMenu)$/;

    function gameTouchOn() {
        try {
            const r = document.querySelector("ruffle-player").ruffle();
            let v = null;
            try { v = r.callExternalInterface("mvsMobileActive"); } catch (e) { v = null; }
            if (v === true || v === false) return v;
            // Builds without that call: the game's touch setting.
            return r.callExternalInterface("getGameObject", "litePreference.data.sTouchMode") === "\"on\"";
        } catch (e) {
            return false;
        }
    }

    let swipe = null;   // the finger being tracked
    let fling = null;   // the animation after a swipe ends

    function canvas() {
        const p = document.querySelector("ruffle-player");
        return p && p.shadowRoot ? p.shadowRoot.querySelector("canvas") : null;
    }

    // CSS pixels per stage pixel (the game is scaled to fit, letterboxed).
    function cssPerStage(c) {
        return Math.min(c.clientWidth / STAGE_W, c.clientHeight / STAGE_H) || 1;
    }

    function fromGame(e) {
        const p = document.querySelector("ruffle-player");
        return p != null && e.composedPath().includes(p);
    }

    function isScroller(chain) {
        return typeof chain === "string" && chain.split(">").some(name => SCROLLERS.test(name));
    }

    function send(c, type, x, y, pointerId, buttons) {
        const Ctor = type.startsWith("pointer") ? PointerEvent : MouseEvent;
        c.dispatchEvent(new Ctor(type, {
            bubbles: true, cancelable: true, composed: true,
            clientX: x, clientY: y, pointerId, pointerType: "touch", isPrimary: true,
            button: type === "pointermove" ? -1 : 0, buttons
        }));
    }

    // Turns finger travel (CSS px, positive = finger up = list moves on) into whole wheel lines.
    function scrollBy(s, cssDy) {
        s.acc += cssDy / cssPerStage(s.c) / PX_PER_LINE;
        const lines = Math.trunc(s.acc);
        if (lines === 0) return;
        s.acc -= lines;
        s.c.dispatchEvent(new WheelEvent("wheel", {
            bubbles: true, cancelable: true, composed: true,
            clientX: s.x0, clientY: s.y0, deltaMode: WheelEvent.DOM_DELTA_LINE, deltaY: lines
        }));
    }

    function stopFling() {
        if (fling) cancelAnimationFrame(fling.raf);
        fling = null;
    }

    window.addEventListener("pointerdown", e => {
        stopFling();
        if (e.pointerType !== "touch" || !e.isPrimary || window.__mvsSwipeScroll === false || !fromGame(e)) {
            swipe = null;
            return;
        }
        const c = canvas();
        if (!c) return;
        window.__mvsPressChain = "";   // Ruffle fills it while it handles this touch
        swipe = { id: e.pointerId, c, x0: e.clientX, y0: e.clientY, lastY: e.clientY, mode: "pending", acc: 0, track: [],
            gameTouch: gameTouchOn() };
    }, true);

    window.addEventListener("pointermove", e => {
        const s = swipe;
        if (!s || e.pointerId !== s.id) return;
        if (s.mode === "pending") {
            const dx = e.clientX - s.x0, dy = e.clientY - s.y0;
            if (Math.abs(dx) > START_PX && Math.abs(dx) > Math.abs(dy)) {
                s.mode = "pass";
            } else if (Math.abs(dy) > START_PX && Math.abs(dy) > 1.2 * Math.abs(dx)) {
                const chain = window.__mvsPressChain;
                const gameOwns = s.gameTouch && typeof chain === "string" && chain.split(">").some(n => GAME_SCROLLS.test(n));
                s.mode = !gameOwns && isScroller(chain) ? "scroll" : "pass";
                s.lastY = s.y0;
            }
        }
        if (s.mode !== "scroll") return;
        e.stopPropagation();
        e.preventDefault();
        scrollBy(s, s.lastY - e.clientY);
        s.lastY = e.clientY;
        s.track.push({ t: e.timeStamp, y: e.clientY });
        while (s.track.length > 2 && e.timeStamp - s.track[0].t > 100) s.track.shift();
    }, true);

    function end(e) {
        const s = swipe;
        if (!s || e.pointerId !== s.id) return;
        swipe = null;
        if (s.mode !== "scroll") return;
        e.stopPropagation();
        e.preventDefault();
        // Let go outside the stage (no click), then rest the pointer where the swipe began so
        // the fling's wheel steps still land on the list.
        send(s.c, "pointermove", -10000, -10000, s.id, 1);
        send(s.c, "pointerup", -10000, -10000, s.id, 0);
        send(s.c, "pointermove", s.x0, s.y0, s.id, 0);
        if (e.type === "pointercancel" || s.track.length < 2) return;
        const a = s.track[0], b = s.track[s.track.length - 1];
        // CSS px per ms, positive = finger up; capped so a jittery last sample can't fling the list away.
        let v = Math.max(-4, Math.min(4, (a.y - b.y) / Math.max(8, b.t - a.t)));
        if (Math.abs(v) < 0.15) return;
        let last = performance.now();
        const step = now => {
            const dt = Math.min(50, now - last);
            last = now;
            scrollBy(s, v * dt);
            v *= Math.pow(0.95, dt / 16);
            if (Math.abs(v) < 0.03) { fling = null; return; }
            fling.raf = requestAnimationFrame(step);
        };
        fling = { raf: requestAnimationFrame(step) };
    }

    window.addEventListener("pointerup", end, true);
    window.addEventListener("pointercancel", end, true);
})();
