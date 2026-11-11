// Finger-swipe scrolling for the chat box.
//
// The chat text can't be pressed (AQW turns its mouse off), so a finger on the chat lands on the
// map behind it and touchscroll.js would leave the swipe alone. When the finger goes down inside
// the chat (AQW's own wheel test on the chat pane), this marks the press as a chat press so
// touchscroll.js turns the drag into wheel steps, and rescales those steps for the chat:
//  - classic chat (Chat): one message line per wheel step, about 18 stage px a line;
//  - new chat UI (Chat2): 6 stage px per wheel line.
// Following the newest message is AQW's own: the classic chat keeps showing new messages while
// it's scrolled to the last line and stays put while scrolled up.
(() => {
    "use strict";
    if (window.__mvsChatScroll) return;
    window.__mvsChatScroll = true;

    const STAGE_W = 960, STAGE_H = 550;
    // touchscroll.js sends one wheel line per 10 stage px of finger travel.
    const TOUCH_PX_PER_LINE = 10;
    const CLASSIC_PX_PER_LINE = 18, NEW_PX_PER_LINE = 6;

    let chain = "";        // what Ruffle reported for this press
    let chat = null;       // "classic" or "new" when the current press started on the chat
    let acc = 0;

    function player() {
        return document.querySelector("ruffle-player");
    }

    function call(name, ...args) {
        try {
            const p = player();
            return p && p.ruffle ? p.ruffle().callExternalInterface(name, ...args) : null;
        } catch (e) {
            return null;
        }
    }

    function canvas() {
        const p = player();
        return p && p.shadowRoot ? p.shadowRoot.querySelector("canvas") : null;
    }

    function toStage(c, x, y) {
        const r = c.getBoundingClientRect();
        const k = Math.min(r.width / STAGE_W, r.height / STAGE_H) || 1;
        return {
            x: (x - r.left - (r.width - STAGE_W * k) / 2) / k,
            y: (y - r.top - (r.height - STAGE_H * k) / 2) / k
        };
    }

    // Which chat (if any) is under this stage point, by the same test AQW's wheel handlers use.
    function chatAt(s) {
        if (s.x < 0 || s.x > 480 || s.y < 120 || s.y > 500) return null;
        if (call("getGameObject", "ui.nc.visible") === "true") {
            return call("callGameFunction", "ui.nc.hitTestPoint", s.x, s.y, false) === "true" ? "new" : null;
        }
        return call("callGameFunction", "ui.mcInterface.t1.hitTestPoint", s.x, s.y, false) === "true" ? "classic" : null;
    }

    function wheel(c, x, y, lines) {
        const e = new WheelEvent("wheel", {
            bubbles: true, cancelable: true, composed: true,
            clientX: x, clientY: y, deltaMode: WheelEvent.DOM_DELTA_LINE, deltaY: lines
        });
        e.__mvsChat = true;
        c.dispatchEvent(e);
    }

    // Ruffle writes the pressed object's class chain here on every press; a chat press over the
    // map reads as a scroller to touchscroll.js.
    Object.defineProperty(window, "__mvsPressChain", {
        configurable: true,
        get() { return chat && /(^|>)World(>|$)/.test(chain) ? chain + ">Chat2" : chain; },
        set(v) { chain = v; }
    });

    window.addEventListener("pointerdown", e => {
        chat = null;
        acc = 0;
        if (e.pointerType !== "touch" || !e.isPrimary || window.__mvsSwipeScroll === false) return;
        const p = player(), c = canvas();
        if (!p || !c || !e.composedPath().includes(p)) return;
        chat = chatAt(toStage(c, e.clientX, e.clientY));
        // With the game's touch controls on, the new chat (Chat2) scrolls itself (its TouchScroll on
        // ui.nc acts only while Mobile.active); the classic chat's own touch layer works only while its
        // input is open, so that one stays ours.
        if (chat === "new" && window.__mvsGameTouchOn && window.__mvsGameTouchOn()) chat = null;
    }, true);

    window.addEventListener("wheel", e => {
        if (e.__mvsChat || e.isTrusted || !chat || e.deltaMode !== WheelEvent.DOM_DELTA_LINE) return;
        // A wheel step from touchscroll.js for a chat swipe: rescale it for the chat.
        e.stopImmediatePropagation();
        e.preventDefault();
        const c = canvas();
        if (!c) return;
        if (chat === "new") {
            acc += e.deltaY * TOUCH_PX_PER_LINE / NEW_PX_PER_LINE;
            const lines = Math.trunc(acc);
            acc -= lines;
            if (lines !== 0) wheel(c, e.clientX, e.clientY, lines);
        } else {
            // The classic chat moves one line per wheel event whatever its size.
            acc += e.deltaY * TOUCH_PX_PER_LINE / CLASSIC_PX_PER_LINE;
            while (Math.abs(acc) >= 1) {
                const step = Math.sign(acc);
                acc -= step;
                wheel(c, e.clientX, e.clientY, step);
            }
        }
    }, true);
})();
