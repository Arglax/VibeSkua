package skua.module {

import flash.display.BitmapData;
import flash.display.DisplayObject;
import flash.display.DisplayObjectContainer;
import flash.display.MovieClip;
import flash.display.Graphics;
import flash.display.InteractiveObject;
import flash.display.Shape;
import flash.display.Sprite;
import flash.events.MouseEvent;
import flash.geom.Matrix;
import flash.geom.Rectangle;
import flash.text.TextField;
import flash.text.TextFormat;
import flash.utils.ByteArray;
import flash.utils.Dictionary;
import flash.utils.getQualifiedClassName;

// MVS layout for AQW's own touch controls (phones, while the game's touch mode is on). Merged into
// Modules as static members like TouchInventoryUI (see the scratchpad merge script): mhFrame runs
// from Modules.handleFrame, and Modules.setTouchUi switches it on with the touch lists.
//
// The touch skill buttons already cover the bottom skill bar and MVS's side rails cover the
// game's menu buttons, so the bottom bar is given to other things:
//  - the skill bar shows only its potion slot, one slot further right and 1.25x larger;
//  - the right-hand menu buttons are hidden; the "n player(s) in <room>" line moves down into the bar;
//  - the drops tray (the game's mobile drops) sits where the skill icons were;
//  - the skill buttons are laid out as auto attack in the middle, 2 lower left, 3 left, 4 upper left,
//    5 upper right at auto attack's size, and dash lower right. The dash distance is a constant Ruffle
//    replaces (MoveInput.DASH_PX, set in bridge.js); SkillCluster.SK_S / AA_S can size the buttons.
// The player and target portraits are drawn 1.5x, the target just right of the player's (the game's
// aura icons move under them). A small
// button on each steps it through 150%, 125%, 100% and hidden (saved in the game's settings).
public class MobileHudUI extends Module {

    private static const MH_POT_SCALE:Number = 1.25;
    // Extra room between the drops tray and the (larger) potion slot, so the tray's frame doesn't cut it.
    private static const MH_POT_GAP:Number = 10;
    // Skill buttons 1-4 (2..5 on their badges) around auto attack, touching it with a small gap:
    // the direction of each (degrees, 0 = right, 90 = down). 2 lower left, 3 left, 4 upper left,
    // 5 upper right; dash lower right (clear of the cluster's collapse handle on the right).
    private static const MH_ANGLE:Array = [0, 125, 180, 235, 290];
    private static const MH_DASH_ANGLE:Number = 50;
    private static const MH_GAP:Number = 12;
    private static const MH_STEPS:Array = [1.5, 1.25, 1, 0];
    private static const MH_STAGE_W:Number = 958;

    private static var mhOn:Boolean = false;
    private static var mhApplied:Boolean = false;
    private static var mhMobileClass:* = null;
    // The skill bar as found (a new class or relog can bring a new one).
    private static var mhBar:* = null;
    private static var mhBarX:Number = 0;
    private static var mhBarY:Number = 0;
    private static var mhSlot:Rectangle = null;
    private static var mhPitch:Number = 45;
    private static var mhSeen:Dictionary = new Dictionary(true);
    private static var mhScaled:Array = [];
    private static var mhHidden:Array = [];
    private static var mhMask:Shape = null;
    private static var mhMenu:* = null;
    private static var mhArea:* = null;
    private static var mhAreaY:Number = 0;
    private static var mhTray:* = null;
    private static var mhCluster:* = null;
    private static var mhClusterPos:Array = null;
    private static var mhClusterAt:int = 0;
    private static var mhPort:* = null;
    private static var mhPortX:Number = 0;
    private static var mhPortY:Number = 0;
    private static var mhTarget:* = null;
    private static var mhTargetX:Number = 0;
    private static var mhTargetY:Number = 0;
    private static var mhTargetW:Number = 0;
    private static var mhBtnP:Sprite = null;
    private static var mhBtnT:Sprite = null;
    private static var mhDrawn:Dictionary = new Dictionary(true);

    public function MobileHudUI() {
        super("MobileHudUI");
    }

    public static function setMobileHud(on:Boolean):void {
        mhOn = on;
    }

    override public function onFrame(game:*):void {
        mhFrame(game);
    }

    // ---- the game's Options: "Mobile Layout" (MVS) right before its "Mobile Controls" ----

    private static const MH_OPT:String = "Mobile Layout";
    private static var mhOptTick:int = 0;
    private static var mhOptRows:Dictionary = new Dictionary(true);

    private static function mhLayoutOn(game:*):Boolean {
        try {
            return game.litePreference.data.dOptions["mvsMobileLayout"] !== false;
        } catch (e:Error) {
        }
        return true;
    }

    private static function mhOptLabel(game:*):String {
        return MH_OPT + ": " + (mhLayoutOn(game) ? "On" : "Off");
    }

    // The Options window (mcOption) rebuilds game.litePref and draws it every time it opens (one
    // window is kept and reopened, so this can't be done once per window): while it's up and the list
    // lacks the entry, add it and draw the list again. Not while a search is typed: the search list
    // is drawn from a fresh rebuild, and clearing the search rebuilds it again.
    private static function mhOptions(game:*):void {
        if (++mhOptTick % 10 != 0) {
            return;
        }
        var win:* = mhFind(game.ui as DisplayObjectContainer, "mcOption", 0);
        if (win == null || win.stage == null || !win.visible) {
            return;
        }
        var searching:Boolean = false;
        try {
            searching = String(win.bg2.txtSearch.text).length > 0;
        } catch (es:Error) {
        }
        if (!searching && mhOptInsert(game)) {
            win.redraw(game.litePref);
        }
        mhOptionRows(win as DisplayObjectContainer, game, 0);
        mhOptPlace(win);
    }

    private static var mhOptPlaced:Dictionary = new Dictionary(true);

    // The Options list groups rows into sections by name and sorts them, so a name it doesn't know
    // lands in "Other" at the bottom. Move the row up into Controls, just above "Mobile Controls"
    // (dropping the "Other" header if it was alone there), and stack the list again: headers are
    // 26 px tall, rows 35 px.
    private static function mhOptPlace(win:*):void {
        var list:DisplayObjectContainer = win.optionList as DisplayObjectContainer;
        if (list == null || mhOptPlaced[list] != null || list.numChildren == 0) {
            return;
        }
        var items:Array = [];
        for (var i:int = 0; i < list.numChildren; i++) {
            items.push(list.getChildAt(i));
        }
        items.sortOn("y", Array.NUMERIC);
        var mine:int = -1;
        var mobile:int = -1;
        for (i = 0; i < items.length; i++) {
            var row:* = items[i];
            if (getQualifiedClassName(row).indexOf("RowHeader") >= 0 || !("txtName" in row) || row.txtName == null) {
                continue;
            }
            var name:String = String(row.txtName.text);
            if (name.indexOf(MH_OPT) == 0) {
                mine = i;
            } else if (mobile < 0 && name.indexOf("Mobile Controls") == 0) {
                mobile = i;
            }
        }
        if (mine < 0 || mobile < 0) {
            return;
        }
        mhOptPlaced[list] = true;
        if (mine < mobile) {
            return;
        }
        var top:Number = DisplayObject(items[0]).y;
        var me:DisplayObject = items[mine];
        var isHeader:Function = function(d:*):Boolean {
            return getQualifiedClassName(d).indexOf("RowHeader") >= 0;
        };
        // Alone in its section: a header just before it and a header (or the end) just after.
        var alone:Boolean = mine > 0 && isHeader(items[mine - 1]) && (mine == items.length - 1 || isHeader(items[mine + 1]));
        if (alone) {
            var hdr:DisplayObject = items[mine - 1];
            list.removeChild(hdr);
            items.splice(mine - 1, 2);
        } else {
            items.splice(mine, 1);
        }
        items.splice(mobile, 0, me);
        var y:Number = top;
        for (i = 0; i < items.length; i++) {
            var d:DisplayObject = items[i];
            d.y = y;
            y += isHeader(d) ? 26 : 35;
        }
        me.x = DisplayObject(items[mobile + 1]).x;
        try {
            win.dRun = list.height - win.bg2.cntMask.height + 5;
        } catch (e:Error) {
        }
    }

    private static function mhOptInsert(game:*):Boolean {
        var list:Array = game.litePref as Array;
        if (list != null) {
            var at:int = -1;
            var have:Boolean = false;
            for (var i:int = 0; i < list.length; i++) {
                var n:String = list[i] != null ? String(list[i].strName) : "";
                if (n.indexOf(MH_OPT) == 0) {
                    have = true;
                }
                if (at < 0 && n.indexOf("Mobile Controls") == 0) {
                    at = i;
                }
            }
            if (!have && at >= 0) {
                list.splice(at, 0, {
                    "strName": mhOptLabel(game),
                    "btn": true,
                    "sDesc": "MVS: lays out the game's mobile controls for phones while Mobile Controls is on.\n" +
                        "The skill bar becomes your drops and potion, the skill buttons are larger, the portraits " +
                        "are larger, and the room's player count moves into the bar.\nTurn it off for the game's own layout."
                });
                return true;
            }
        }
        return false;
    }

    private static function mhOptionRows(c:DisplayObjectContainer, game:*, depth:int):void {
        for (var i:int = 0; i < c.numChildren; i++) {
            var d:DisplayObject = c.getChildAt(i);
            if (getQualifiedClassName(d) == "liteAssets.listOptionsItem::listOptionsItemBtn") {
                var row:* = d;
                if (mhOptRows[row] == null && row.txtName != null && String(row.txtName.text).indexOf(MH_OPT) == 0) {
                    mhOptRows[row] = true;
                    row.btnActive.addEventListener(MouseEvent.CLICK, mhOptTap, false, 0, true);
                    var tag:Sprite = mhTag();
                    tag.x = Math.round(row.txtName.x + row.txtName.textWidth + 8);
                    tag.y = Math.round(row.txtName.y + 3);
                    row.addChild(tag);
                }
                continue;
            }
            if (depth < 7 && d is DisplayObjectContainer) {
                mhOptionRows(d as DisplayObjectContainer, game, depth + 1);
            }
        }
    }

    private static function mhOptTap(e:MouseEvent):void {
        try {
            var game:* = skua.Main.instance.getGame();
            var opts:* = game.litePreference.data.dOptions;
            opts["mvsMobileLayout"] = !mhLayoutOn(game);
            game.litePreference.flush();
            // The row splits "Name: Value" into its name and pill right after this.
            var row:* = e.currentTarget.parent;
            row.txtName.text = mhOptLabel(game);
            for each (var entry:* in game.litePref) {
                if (entry != null && String(entry.strName).indexOf(MH_OPT) == 0) {
                    entry.strName = mhOptLabel(game);
                }
            }
        } catch (err:Error) {
        }
    }

    // The small gold "MVS" badge that marks MVS additions in the game's own windows.
    private static function mhTag():Sprite {
        var s:Sprite = new Sprite();
        s.mouseEnabled = false;
        s.mouseChildren = false;
        var tf:TextField = new TextField();
        tf.selectable = false;
        tf.autoSize = "left";
        tf.defaultTextFormat = new TextFormat("Arial", 8, 0x1A140E, true);
        tf.text = "MVS";
        tf.x = 2;
        tf.y = -2;
        var g:Graphics = s.graphics;
        g.lineStyle(1, 0xFFE9A8, 1);
        g.beginFill(0xD9AE52, 1);
        g.drawRoundRect(0, 0, tf.width + 4, 11, 5, 5);
        g.endFill();
        s.addChild(tf);
        return s;
    }

    private static function mhFrame(game:*):void {
        if (mhOn) {
            try {
                mhOptions(game);
            } catch (eo:Error) {
            }
        }
        if (!(mhOn && mhMobile(game) && mhLayoutOn(game))) {
            if (mhApplied) {
                mhRestore(game);
            }
            return;
        }
        mhApplied = true;
        try {
            mhLayoutBar(game);
        } catch (e1:Error) {
        }
        try {
            mhPlaceTray(game);
        } catch (e2:Error) {
        }
        try {
            mhLayoutSkills(game);
        } catch (e3:Error) {
        }
        try {
            mhPortraits(game);
        } catch (e4:Error) {
        }
    }

    // For the page's swipe scrolling (Externalizer "mvsMobileActive"): while the game's own touch
    // controls are on, its lists scroll themselves and the page leaves those swipes to the game.
    public static function mhMobileActive():Boolean {
        try {
            return mhMobile(skua.Main.instance.getGame());
        } catch (e:Error) {
        }
        return false;
    }

    private static function mhMobile(game:*):Boolean {
        try {
            if (mhMobileClass == null) {
                mhMobileClass = game.loaderInfo.applicationDomain.getDefinition("liteAssets.mobile.Mobile");
            }
            return Boolean(mhMobileClass.active) && game.ui != null && game.ui.mcInterface != null;
        } catch (e:Error) {
        }
        return false;
    }

    // ---- the bottom bar ----

    private static function mhLayoutBar(game:*):void {
        var mci:DisplayObjectContainer = game.ui.mcInterface as DisplayObjectContainer;
        var bar:DisplayObjectContainer = game.ui.mcInterface.actBar as DisplayObjectContainer;
        if (bar == null) {
            return;
        }
        if (bar !== mhBar) {
            mhUndoBar();
            var b0:DisplayObject = bar.getChildByName("blank0");
            var b1:DisplayObject = bar.getChildByName("blank1");
            var b5:DisplayObject = bar.getChildByName("blank5");
            if (b0 == null || b5 == null) {
                return;
            }
            mhBar = bar;
            mhBarX = bar.x;
            mhBarY = bar.y;
            mhSlot = b5.getBounds(bar);
            mhPitch = b1 != null && b1.x - b0.x > 20 ? b1.x - b0.x : 45;
            mhSeen = new Dictionary(true);
            // The empty slot's art keeps its place: the potion icon is put at its x when it's made.
            mhSeen[b5] = true;
            mhHideLabels(mci);
        }
        bar.x = mhBarX + mhPitch + MH_POT_GAP;
        bar.y = mhBarY;
        var cx:Number = mhSlot.x + mhSlot.width / 2;
        var cy:Number = mhSlot.y + mhSlot.height / 2;
        var pot:DisplayObject = bar.getChildByName("i6");
        var i:int = 0;
        while (i < bar.numChildren) {
            var c:DisplayObject = bar.getChildAt(i);
            i++;
            if (mhSeen[c]) {
                continue;
            }
            var r:Rectangle = c.getBounds(bar);
            if (r.width <= 0) {
                continue;
            }
            mhSeen[c] = true;
            var mx:Number = r.x + r.width / 2;
            if (mx < mhSlot.left - 6 || mx > mhSlot.right + 6) {
                continue;
            }
            // The cooldown overlay and its flare copy the (already larger) potion icon's transform.
            if (pot != null && c !== pot && mhSameMatrix(c.transform.matrix, pot.transform.matrix)) {
                continue;
            }
            c.scaleX *= MH_POT_SCALE;
            c.scaleY *= MH_POT_SCALE;
            c.x = cx + (c.x - cx) * MH_POT_SCALE;
            c.y = cy + (c.y - cy) * MH_POT_SCALE;
            mhScaled.push(c);
        }
        // Only the potion slot shows; the other slots can't be pressed through the drops.
        if (mhMask == null) {
            mhMask = new Shape();
        }
        if (mhMask.parent !== mci) {
            mci.addChild(mhMask);
        }
        var hw:Number = mhSlot.width * MH_POT_SCALE / 2 + 1;
        var hh:Number = mhSlot.height * MH_POT_SCALE / 2 + 10;
        var g:Graphics = mhMask.graphics;
        g.clear();
        g.beginFill(0);
        g.drawRect(bar.x + cx - hw, bar.y + cy - hh, hw * 2, hh * 2);
        g.endFill();
        if (bar.mask !== mhMask) {
            bar.mask = mhMask;
        }
        var s:int = 1;
        while (s <= 5) {
            var icon:InteractiveObject = bar.getChildByName("i" + s) as InteractiveObject;
            if (icon != null) {
                icon.mouseEnabled = false;
            }
            s++;
        }
        mhMenu = game.ui.mcInterface.mcMenu;
        if (mhMenu != null) {
            mhMenu.visible = false;
        }
        var area:DisplayObject = game.ui.mcInterface.areaList as DisplayObject;
        if (area != null) {
            if (area !== mhArea) {
                mhArea = area;
                mhAreaY = area.y;
            }
            // Level with the chat entry row.
            area.y = Math.round(mhBarY + cy + 5);
        }
    }

    private static function mhSameMatrix(a:Matrix, b:Matrix):Boolean {
        return Math.abs(a.a - b.a) < 0.001 && Math.abs(a.d - b.d) < 0.001 && Math.abs(a.tx - b.tx) < 0.5 && Math.abs(a.ty - b.ty) < 0.5;
    }

    // The skill bar's 1-6 key numbers are art on the interface itself, under the slots.
    private static function mhHideLabels(mci:DisplayObjectContainer):void {
        var left:Number = mhBarX - 4;
        var right:Number = mhBarX + mhSlot.right + 4;
        var i:int = 0;
        while (i < mci.numChildren) {
            var c:DisplayObject = mci.getChildAt(i);
            i++;
            if (c === mhBar || c === mhMask || !c.visible) {
                continue;
            }
            var r:Rectangle = c.getBounds(mci);
            if (r.width > 0 && r.left >= left && r.right <= right && r.top >= mhBarY - 8 && r.bottom <= mhBarY + 52) {
                c.visible = false;
                mhHidden.push(c);
            }
        }
    }

    private static function mhUndoBar():void {
        if (mhBar != null) {
            mhBar.x = mhBarX;
            mhBar.y = mhBarY;
            if (mhBar.mask === mhMask) {
                mhBar.mask = null;
            }
            var s:int = 1;
            while (s <= 5) {
                var icon:InteractiveObject = mhBar.getChildByName("i" + s) as InteractiveObject;
                if (icon != null) {
                    icon.mouseEnabled = true;
                }
                s++;
            }
            if (mhSlot != null) {
                var cx:Number = mhSlot.x + mhSlot.width / 2;
                var cy:Number = mhSlot.y + mhSlot.height / 2;
                for each (var c:DisplayObject in mhScaled) {
                    if (c.parent === mhBar) {
                        c.scaleX /= MH_POT_SCALE;
                        c.scaleY /= MH_POT_SCALE;
                        c.x = cx + (c.x - cx) / MH_POT_SCALE;
                        c.y = cy + (c.y - cy) / MH_POT_SCALE;
                    }
                }
            }
        }
        mhScaled = [];
        for each (var h:DisplayObject in mhHidden) {
            h.visible = true;
        }
        mhHidden = [];
        if (mhMask != null && mhMask.parent != null) {
            mhMask.parent.removeChild(mhMask);
        }
        mhBar = null;
    }

    // ---- the drops tray, in the skill icons' place ----

    private static function mhPlaceTray(game:*):void {
        var ui:DisplayObjectContainer = game.ui as DisplayObjectContainer;
        if (mhTray == null || mhTray.parent !== ui) {
            mhTray = null;
            var i:int = 0;
            while (i < ui.numChildren) {
                var c:DisplayObject = ui.getChildAt(i);
                if (getQualifiedClassName(c) == "liteAssets.mobile::DropTray") {
                    mhTray = c;
                    break;
                }
                i++;
            }
        }
        if (mhTray == null || mhBar == null) {
            return;
        }
        var pos:* = null;
        try {
            pos = game.litePreference.data.dOptions.posDrops;
        } catch (e:Error) {
        }
        // A tray the player moved (the game's own edit mode) stays where they put it.
        if (pos != null || mhTray.numChildren == 0) {
            mhTray.x = mhTray.y = 0;
            return;
        }
        var row:DisplayObjectContainer = mhTray.getChildAt(0) as DisplayObjectContainer;
        var plate:DisplayObject = row.numChildren > 0 ? row.getChildAt(0) : row;
        var b0:Rectangle = mhBar.getChildByName("blank0").getBounds(ui);
        var tx:Number = b0.x - mhPitch - MH_POT_GAP - 5;
        var ty:Number = b0.y - 5;
        var gold:DisplayObject = game.ui.mcInterface.mcGold as DisplayObject;
        if (gold != null && gold.visible) {
            ty = Math.min(ty, gold.getBounds(ui).top + 1 - plate.height);
        }
        mhTray.x = Math.round(tx - row.x);
        mhTray.y = Math.round(ty - row.y);
        // The game places its drop card (Accept / Decline / Preview) inside the tray as if the tray sat
        // at the screen's corner; keep the card on screen now that the tray is moved.
        for (var k:int = 0; k < mhTray.numChildren; k++) {
            var card:DisplayObject = mhTray.getChildAt(k);
            if (getQualifiedClassName(card) != "liteAssets.mobile::DropPopover") {
                continue;
            }
            var gx:Number = card.x + mhTray.x;
            var gy:Number = card.y + mhTray.y;
            var fx:Number = Math.max(6, Math.min(MH_STAGE_W - 6 - card.width, gx));
            var fy:Number = Math.max(6, gy);
            if (fx != gx) {
                card.x = Math.round(fx - mhTray.x);
            }
            if (fy != gy) {
                card.y = Math.round(fy - mhTray.y);
            }
        }
    }

    // ---- the skill buttons ----

    private static function mhLayoutSkills(game:*):void {
        if (mhCluster == null || mhCluster.stage == null) {
            mhCluster = null;
            mhClusterPos = null;
            if (mhClusterAt-- > 0) {
                return;
            }
            mhClusterAt = 30;
            mhCluster = mhFind(game.ui as DisplayObjectContainer, "liteAssets.mobile::SkillCluster", 0);
            if (mhCluster == null) {
                return;
            }
        }
        var ring:DisplayObjectContainer = mhCluster.numChildren > 1 ? mhCluster.getChildAt(1) as DisplayObjectContainer : null;
        if (ring == null || ring.numChildren < 7) {
            return;
        }
        var i:int;
        if (mhClusterPos == null) {
            mhClusterPos = [];
            i = 0;
            while (i < 7) {
                var d:DisplayObject = ring.getChildAt(i);
                mhClusterPos.push([d.x, d.y, d.scaleX, d.scaleY]);
                i++;
            }
        }
        var x0:Number = mhClusterPos[0][0];
        var y0:Number = mhClusterPos[0][1];
        // Half sizes from the buttons' hit areas (in the buttons' own, unscaled, coordinates).
        var rA:Number = mhHalf(ring.getChildAt(0));
        var rS:Number = mhHalf(ring.getChildAt(1));
        var rD:Number = mhHalf(ring.getChildAt(6)) - 4;
        if (!(rA > 10 && rS > 10)) {
            return;
        }
        i = 1;
        while (i <= 4) {
            var d2:Number = i == 4 ? rA * 2 + MH_GAP : rA + rS + MH_GAP;
            mhPlace(ring.getChildAt(i), x0, y0, MH_ANGLE[i], d2);
            i++;
        }
        // Skill 5 at auto attack's size.
        var s5:DisplayObject = ring.getChildAt(4);
        s5.scaleX = s5.scaleY = rA / rS;
        // The potion has its slot on the bar.
        ring.getChildAt(5).x = -10000;
        mhPlace(ring.getChildAt(6), x0, y0, MH_DASH_ANGLE, rA + rD + MH_GAP);
        mhLowerSkills(game, ring);
    }

    // The whole cluster (with its collapse handle) moves down until its lowest button sits just
    // above the player count line in the bar, where the menu buttons used to be. Where the game
    // put it (its edit mode can move it) is kept as the starting point.
    // The buttons and the collapse handle move inside the cluster: the game sets the cluster's own
    // position whenever it re-places its touch controls, which moving the cluster itself fought.
    private static function mhLowerSkills(game:*, ring:DisplayObjectContainer):void {
        if (mhBar == null || mhSlot == null) {
            return;
        }
        var ui:DisplayObjectContainer = game.ui as DisplayObjectContainer;
        var handle:DisplayObject = mhHandle(ring);
        ring.y = 0;
        var bottom:Number = -10000;
        for each (var i:int in [0, 1, 2, 3, 4, 6]) {
            bottom = Math.max(bottom, ring.getChildAt(i).getBounds(ui).bottom);
        }
        var want:Number = game.ui.mcInterface.y + mhBarY + mhSlot.y + 12;
        var dy:Number = bottom > -10000 && want > bottom ? Math.round(want - bottom) : 0;
        ring.y = dy;
        if (handle != null) {
            handle.y = dy;
        }
    }

    // The cluster's collapse handle: its last sprite after the ring.
    private static function mhHandle(ring:DisplayObjectContainer):DisplayObject {
        var i:int = mhCluster.numChildren - 1;
        while (i >= 0) {
            var d:DisplayObject = mhCluster.getChildAt(i);
            if (d === ring) {
                return null;
            }
            if (d is Sprite) {
                return d;
            }
            i--;
        }
        return null;
    }

    private static function mhHalf(d:DisplayObject):Number {
        var s:Sprite = d as Sprite;
        if (s != null && s.hitArea != null) {
            return s.hitArea.width / 2;
        }
        return d.width / d.scaleX / 2;
    }

    private static function mhPlace(d:DisplayObject, x0:Number, y0:Number, deg:Number, dist:Number):void {
        var a:Number = deg * Math.PI / 180;
        d.x = Math.round(x0 + Math.cos(a) * dist);
        d.y = Math.round(y0 + Math.sin(a) * dist);
    }

    private static function mhUndoSkills():void {
        if (mhCluster == null || mhClusterPos == null) {
            return;
        }
        var ring:DisplayObjectContainer = mhCluster.getChildAt(1) as DisplayObjectContainer;
        var i:int = 0;
        while (ring != null && i < 7 && i < ring.numChildren) {
            var d:DisplayObject = ring.getChildAt(i);
            d.x = mhClusterPos[i][0];
            d.y = mhClusterPos[i][1];
            d.scaleX = mhClusterPos[i][2];
            d.scaleY = mhClusterPos[i][3];
            i++;
        }
        if (ring != null) {
            var handle:DisplayObject = mhHandle(ring);
            ring.y = 0;
            if (handle != null) {
                handle.y = 0;
            }
        }
        mhCluster = null;
        mhClusterPos = null;
    }

    private static function mhFind(c:DisplayObjectContainer, cls:String, depth:int):DisplayObjectContainer {
        if (c == null) {
            return null;
        }
        var i:int = 0;
        while (i < c.numChildren) {
            var d:DisplayObject = c.getChildAt(i);
            if (getQualifiedClassName(d) == cls) {
                return d as DisplayObjectContainer;
            }
            if (depth < 2 && d is DisplayObjectContainer) {
                var f:DisplayObjectContainer = mhFind(d as DisplayObjectContainer, cls, depth + 1);
                if (f != null) {
                    return f;
                }
            }
            i++;
        }
        return null;
    }

    // ---- the player and target portraits ----

    private static function mhStep(v:*):int {
        var n:int = int(v);
        return n >= 0 && n < MH_STEPS.length ? n : 0;
    }

    private static function mhPortraits(game:*):void {
        var data:* = game.litePreference.data;
        var p:DisplayObject = game.ui.mcPortrait as DisplayObject;
        var t:DisplayObject = game.ui.mcPortraitTarget as DisplayObject;
        if (p != null) {
            if (p !== mhPort) {
                mhPort = p;
                mhPortX = p.x;
                mhPortY = p.y;
            }
            var sp:Number = MH_STEPS[mhStep(data.mvsPortP)];
            if (sp > 0) {
                p.scaleX = p.scaleY = sp;
                p.x = mhPortX;
                p.y = mhPortY;
            } else {
                p.x = -5000;
            }
            mhBtnP = mhButton(game, mhBtnP, "mvsPortBtnP", p, false);
            // Just under the name and bars panel's right end; the top left corner when hidden.
            mhBtnP.x = sp > 0 ? mhPortX + 188 * sp : 16;
            mhBtnP.y = sp > 0 ? mhPortY + 81 * sp + 13 : 16;
            mhGlyph(mhBtnP, mhStep(data.mvsPortP));
        }
        if (t != null) {
            if (t !== mhTarget) {
                mhTarget = t;
                mhTargetX = t.x;
                mhTargetY = t.y;
                mhTargetW = t.width / t.scaleX;
            }
            var st:Number = MH_STEPS[mhStep(data.mvsPortT)];
            // Where the game puts it, or just right of the (larger) player portrait.
            var tx:Number = mhTargetX;
            if (p != null && p.x > -1000) {
                tx = Math.max(tx, Math.round(p.x + p.width + 6));
            }
            if (st > 0) {
                t.scaleX = t.scaleY = st;
                t.x = tx;
                t.y = mhTargetY;
            } else {
                t.x = -5000;
            }
            mhBtnT = mhButton(game, mhBtnT, "mvsPortBtnT", t, true);
            mhBtnT.visible = t.visible;
            // Under the target's round portrait; where its corner would be when hidden.
            mhBtnT.x = st > 0 ? t.x + 26 * st : tx + 16;
            mhBtnT.y = st > 0 ? mhTargetY + 82 * st + 13 : 16;
            mhGlyph(mhBtnT, mhStep(data.mvsPortT));
        }
        mhAuraIcons(game, p, t);
    }

    // The game's aura icons sit under the portraits; move them under the larger ones (unless the
    // player dragged them somewhere).
    private static function mhAuraIcons(game:*, p:DisplayObject, t:DisplayObject):void {
        var opts:* = game.litePreference.data.dOptions;
        var pa:DisplayObject = game.pAurasUI as DisplayObject;
        // By the portraits' drawn sizes (96 and 82 px tall at 100%): their measured heights include
        // hidden parts and put the target's icons in the middle of the screen.
        if (pa != null && p != null && (opts == null || opts.posAurasP == null)) {
            pa.x = 130;
            pa.y = p.x > -1000 ? Math.max(100, Math.round(p.y + 96 * p.scaleY + 4)) : 30;
        }
        var ta:DisplayObject = game.tAurasUI as DisplayObject;
        if (ta != null && t != null && (opts == null || opts.posAurasT == null)) {
            if (t.x > -1000) {
                ta.x = Math.round(t.x + 85 * t.scaleX);
                ta.y = Math.max(104, Math.round(t.y + 82 * t.scaleY + 6));
            } else {
                ta.x = 320;
                ta.y = 30;
            }
        }
    }

    private static function mhButton(game:*, btn:Sprite, name:String, over:DisplayObject, target:Boolean):Sprite {
        var ui:DisplayObjectContainer = game.ui as DisplayObjectContainer;
        if (btn == null) {
            btn = new Sprite();
            btn.name = name;
            btn.buttonMode = true;
            btn.mouseChildren = false;
            var hit:Sprite = new Sprite();
            hit.graphics.beginFill(0, 0);
            hit.graphics.drawCircle(0, 0, 22);
            hit.graphics.endFill();
            hit.mouseEnabled = false;
            btn.addChild(hit);
            btn.hitArea = hit;
            var face:Shape = new Shape();
            face.name = "face";
            btn.addChild(face);
            btn.addEventListener(MouseEvent.CLICK, target ? mhTapTarget : mhTapPlayer, false, 0, true);
        }
        var want:int = over.parent === ui ? ui.getChildIndex(over) + 1 : ui.numChildren;
        if (btn.parent !== ui) {
            ui.addChildAt(btn, Math.min(want, ui.numChildren));
        } else if (ui.getChildIndex(btn) < want - 1) {
            ui.setChildIndex(btn, Math.min(want - 1, ui.numChildren - 1));
        }
        return btn;
    }

    // "-" while a tap makes it smaller, "x" when the next tap hides it, "+" while hidden.
    private static function mhGlyph(btn:Sprite, step:int):void {
        var face:Shape = btn.getChildByName("face") as Shape;
        if (face == null || mhDrawn[btn] === step) {
            return;
        }
        mhDrawn[btn] = step;
        var g:Graphics = face.graphics;
        g.clear();
        g.lineStyle(1.5, 0xC9A24A, 1);
        g.beginFill(0x1A1410, 0.9);
        g.drawCircle(0, 0, 11);
        g.endFill();
        g.lineStyle(2.5, 0xFFFFFF, 1);
        if (step == 3) {
            g.moveTo(-5, 0);
            g.lineTo(5, 0);
            g.moveTo(0, -5);
            g.lineTo(0, 5);
        } else if (step == 2) {
            g.moveTo(-4, -4);
            g.lineTo(4, 4);
            g.moveTo(4, -4);
            g.lineTo(-4, 4);
        } else {
            g.moveTo(-5, 0);
            g.lineTo(5, 0);
        }
    }

    private static function mhTapPlayer(e:MouseEvent):void {
        mhCycle("mvsPortP");
    }

    private static function mhTapTarget(e:MouseEvent):void {
        mhCycle("mvsPortT");
    }

    private static function mhCycle(key:String):void {
        try {
            var game:* = skua.Main.instance.getGame();
            var data:* = game.litePreference.data;
            data[key] = (mhStep(data[key]) + 1) % MH_STEPS.length;
            game.litePreference.flush();
        } catch (e:Error) {
        }
    }

    private static function mhUndoPortraits():void {
        if (mhPort != null) {
            mhPort.scaleX = mhPort.scaleY = 1;
            mhPort.x = mhPortX;
            mhPort.y = mhPortY;
        }
        if (mhTarget != null) {
            mhTarget.scaleX = mhTarget.scaleY = 1;
            mhTarget.x = mhTargetX;
            mhTarget.y = mhTargetY;
        }
        for each (var b:Sprite in [mhBtnP, mhBtnT]) {
            if (b != null && b.parent != null) {
                b.parent.removeChild(b);
            }
        }
        mhPort = mhTarget = null;
    }

    // ---- aura icons for the page's aura overlay (Externalizer "mvsAuraIcon") ----

    // The game's icon for an aura ("i,i,ArcInva1": the last name is its art), drawn size x size and
    // returned as "size:" + base64 of its ARGB pixels; "" when it can't be drawn.
    public static function mhAuraIcon(link:String, size:int):String {
        try {
            var game:* = skua.Main.instance.getGame();
            if (link.indexOf(",") > -1) {
                var parts:Array = link.split(",");
                link = String(parts[parts.length - 1]);
            }
            size = Math.max(8, Math.min(64, size));
            var c:Class = game.world.getClass(link) as Class;
            if (c == null) {
                return "";
            }
            var d:DisplayObject = new c() as DisplayObject;
            if (d is MovieClip) {
                MovieClip(d).gotoAndStop(1);
            }
            var r:Rectangle = d.getBounds(d);
            if (!(r.width > 0 && r.height > 0)) {
                return "";
            }
            var k:Number = size / Math.max(r.width, r.height);
            var m:Matrix = new Matrix(k, 0, 0, k, -r.x * k + (size - r.width * k) / 2, -r.y * k + (size - r.height * k) / 2);
            var bmd:BitmapData = new BitmapData(size, size, true, 0);
            bmd.draw(d, m, null, null, null, true);
            var px:ByteArray = bmd.getPixels(bmd.rect);
            bmd.dispose();
            return size + ":" + mhBase64(px);
        } catch (e:Error) {
        }
        return "";
    }

    private static const MH_B64:String = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789+/";

    private static function mhBase64(b:ByteArray):String {
        var out:Array = [];
        var i:int = 0;
        var n:int = b.length;
        while (i < n) {
            var b0:int = b[i];
            var b1:int = i + 1 < n ? b[i + 1] : 0;
            var b2:int = i + 2 < n ? b[i + 2] : 0;
            out.push(MH_B64.charAt(b0 >> 2), MH_B64.charAt(((b0 & 3) << 4) | (b1 >> 4)),
                i + 1 < n ? MH_B64.charAt(((b1 & 15) << 2) | (b2 >> 6)) : "=",
                i + 2 < n ? MH_B64.charAt(b2 & 63) : "=");
            i += 3;
        }
        return out.join("");
    }

    private static function mhRestore(game:*):void {
        mhApplied = false;
        try {
            mhUndoBar();
            if (mhMenu != null) {
                mhMenu.visible = true;
            }
            if (mhArea != null) {
                mhArea.y = mhAreaY;
            }
            if (mhTray != null) {
                mhTray.x = mhTray.y = 0;
            }
            mhUndoSkills();
            mhUndoPortraits();
            var opts:* = game.litePreference.data.dOptions;
            if (game.pAurasUI != null && (opts == null || opts.posAurasP == null)) {
                game.pAurasUI.x = 130;
                game.pAurasUI.y = 100;
            }
            if (game.tAurasUI != null && (opts == null || opts.posAurasT == null)) {
                game.tAurasUI.x = 320;
                game.tAurasUI.y = 104;
            }
        } catch (e:Error) {
        }
    }
}
}
