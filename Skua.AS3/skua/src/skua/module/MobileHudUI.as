package skua.module {

import flash.display.BitmapData;
import flash.display.DisplayObject;
import flash.display.DisplayObjectContainer;
import flash.display.MovieClip;
import flash.display.Graphics;
import flash.display.InteractiveObject;
import flash.display.Shape;
import flash.display.Sprite;
import flash.events.Event;
import flash.events.MouseEvent;
import flash.geom.ColorTransform;
import flash.geom.Matrix;
import flash.geom.Point;
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

    // ---- MVS features in the game's Options: one row each, at the top of Controls ----

    // [key in litePreference.data.dOptions, row name, description]. All are on unless set to false.
    // The page (bridge.js, overlays) is told each value as "key:on" / "key:off" through mvsOption and keeps a
    // copy for what it needs before the game has loaded (Improve FPS, Long Dash).
    private static const MH_OPTS:Array = [
        ["mvsImproveFps", "Improve FPS", "MVS: smoother, steadier frames: in-between pictures, a frame governor that lowers graphics only when that helps, and removed objects left still.\nOff runs the game the way plain Ruffle does."],
        ["mvsMobileLayout", "Mobile Layout", "MVS: lays out the game's mobile controls for phones while Mobile Controls is on.\nThe skill bar becomes your drops and potion, the skill buttons sit around auto attack, the portraits are larger, and the room's player count moves into the bar.\nOff gives the game's own layout."],
        ["mvsJoystick", "Dynamic Joystick", "MVS: the joystick appears wherever your thumb lands on the left 35% of the screen, not only near its corner, so a move never turns into a tap.\nA touch that doesn't move is still a tap. Needs Mobile Controls."],
        ["mvsLongDash", "Long Dash", "MVS: the mobile dash goes 3 times as far, like the classic dash across the screen.\nTakes effect the next time the game starts."],
        ["mvsSwipe", "Swipe Scrolling", "MVS: swipe lists, shops, quests and the chat with a finger."],
        ["mvsAuras", "Improved Auras", "MVS: windows for your and your target's auras: stacks, timers, Decay and Enrage warnings, tap one for details."],
        ["mvsInvUI", "Inventory UI", "MVS: larger inventory rows and category icons; classes show their rank, gear its enhancement.\nReopen the bag to see the change."],
        ["mvsInvFilter", "Inventory Filter", "MVS: sort weapons, armor, helms and capes by a damage type or a boost (the Sort chip in the bag).\nReopen the bag to see the change."],
        ["mvsClassRating", "Class Rating", "MVS: star ratings for classes in the inventory.\nReopen the bag to see the change."],
        ["mvsDrops", "MVS Drops", "MVS: drops wait in the Drops panel and the bar instead of popping up over the game."]
    ];
    // The (?) guide's "When ON" / "When OFF" lines, by key.
    private static const MH_OPT_ONOFF:Object = {
        "mvsImproveFps": ["Smoother, steadier frames: in-between pictures, the frame governor, removed objects left still.", "Frames the way plain Ruffle draws them."],
        "mvsMobileLayout": ["Skill buttons around auto attack, drops and potion in the bar, bigger portraits.", "The game's own mobile layout."],
        "mvsJoystick": ["Put your thumb down anywhere on the left 35% to steer.", "The game's normal joystick: only its fixed corner spot works."],
        "mvsLongDash": ["The dash goes 3 times as far (from the next game start).", "The game's normal short mobile dash."],
        "mvsSwipe": ["Drag lists, shops, quests and the chat with a finger.", "Use the scroll bars and arrows."],
        "mvsAuras": ["Aura windows for you and your target, with stacks and timers.", "Only the game's own aura icons."],
        "mvsInvUI": ["Bigger rows and icons; class ranks and enhancements shown.", "The game's normal inventory."],
        "mvsInvFilter": ["A Sort chip in the bag: by damage type or boost.", "The game's normal sorting."],
        "mvsClassRating": ["Star ratings on classes in the inventory and preview.", "No stars."],
        "mvsDrops": ["Drops collect in the Drops panel and the bar.", "Drops pop up over the game as usual."]
    };
    private static var mhOptRowOf:Object = {};
    private static var mhOptTagOf:Dictionary = new Dictionary(true);
    private static var mhGuide:Sprite = null;
    private static var mhGuideKnob:Shape = null;
    private static var mhGuideT:Number = 0;
    private static var mhOptTick:int = 0;
    private static var mhOptRows:Dictionary = new Dictionary(true);
    private static var mhOptSynced:Boolean = false;

    // ---- Streamer Mode ----
    // The page (bridge.js) writes litePreference.data.mvsStreamer = "name<TAB>guild<TAB>time ms" every
    // second while Streamer Mode is on. Done here, every frame, before the frame is drawn: the game
    // rewrites the portrait name on every HP or MP update, so a page that put the stand-in back a few
    // times a second let the real name flash in between. A stale value (the page stopped) counts as off.
    private static var mhStreamOn:Boolean = false;
    private static var mhStreamHidden:Array = [];
    private static var mhStreamTexts:Array = [];   // [TextField, original text] changed in the house
    private static var mhStreamScan:int = 0;
    private static const MH_STREAM_HOUSE:String = "MVS is the best!";

    private static function mhStreamer(game:*):void {
        var cfg:Array = null;
        try {
            var raw:* = game.litePreference.data.mvsStreamer;
            if (raw is String && String(raw).length > 0) {
                cfg = String(raw).split("\t");
                if (cfg.length < 3 || new Date().time - Number(cfg[2]) > 6000) {
                    cfg = null;
                }
            }
        } catch (e0:Error) {
        }
        if (cfg == null) {
            if (mhStreamOn) {
                mhStreamerOff();
            }
            return;
        }
        mhStreamOn = true;
        var name:String = String(cfg[0]);
        var guild:String = String(cfg[1]);
        var real:String = "";
        try {
            real = String(game.world.myAvatar.objData.strUsername);
        } catch (e1:Error) {
        }
        // Name tag over the character (keeps the game's AFK prefix) and guild tag.
        try {
            var ti:* = game.world.myAvatar.pMC.pname.ti;
            var want:String = (String(ti.text).indexOf("<AFK> ") == 0 ? "<AFK> " : "") + name;
            if (ti.text != want) {
                ti.text = want;
            }
            var tg:* = game.world.myAvatar.pMC.pname.tg;
            if (tg.text != "< " + guild + " >") {
                tg.text = "< " + guild + " >";
            }
        } catch (e2:Error) {
        }
        // Own portrait, and the target portrait only while it shows the player (never a monster).
        var up:String = name.toUpperCase();
        try {
            if (game.ui.mcPortrait.strName.text != up) {
                game.ui.mcPortrait.strName.text = up;
            }
        } catch (e3:Error) {
        }
        try {
            var tn:* = game.ui.mcPortraitTarget.strName;
            if (real.length > 0 && String(tn.text).toUpperCase() == real.toUpperCase()) {
                tn.text = up;
            }
        } catch (e4:Error) {
        }
        // Chat: the classic chat (mcInterface.t1, input te) and the new chat UI (ui.nc).
        mhStreamHide(game, ["ui", "mcInterface", "t1"]);
        mhStreamHide(game, ["ui", "mcInterface", "te"]);
        mhStreamHide(game, ["ui", "nc"]);
        // The house title ("<name>'s House"), looked for once a second while in a house.
        if (++mhStreamScan >= 30) {
            mhStreamScan = 0;
            try {
                if (String(game.world.strMapName).toLowerCase().indexOf("house") == 0 && real.length > 0) {
                    mhStreamHouse(game.world as DisplayObjectContainer, real.toLowerCase(), 0);
                    mhStreamHouse(game.ui as DisplayObjectContainer, real.toLowerCase(), 0);
                }
            } catch (e5:Error) {
            }
        }
    }

    private static function mhStreamHide(game:*, path:Array):void {
        try {
            var o:* = game;
            for each (var k:String in path) {
                o = o[k];
            }
            if (o != null && o.visible) {
                o.visible = false;
                if (mhStreamHidden.indexOf(o) < 0) {
                    mhStreamHidden.push(o);
                }
            }
        } catch (e:Error) {
        }
    }

    private static function mhStreamHouse(c:DisplayObjectContainer, real:String, depth:int):void {
        if (c == null || depth > 8) {
            return;
        }
        for (var i:int = 0; i < c.numChildren; i++) {
            var d:DisplayObject = c.getChildAt(i);
            if (d is TextField) {
                var t:String = TextField(d).text;
                var low:String = t.toLowerCase();
                if (low.indexOf(real) >= 0 && low.indexOf("house") >= 0) {
                    mhStreamTexts.push([d, t]);
                    TextField(d).text = MH_STREAM_HOUSE;
                }
            } else if (d is DisplayObjectContainer) {
                mhStreamHouse(DisplayObjectContainer(d), real, depth + 1);
            }
        }
    }

    private static function mhStreamerOff():void {
        mhStreamOn = false;
        for each (var o:* in mhStreamHidden) {
            try {
                o.visible = true;
            } catch (e:Error) {
            }
        }
        mhStreamHidden = [];
        for each (var p:Array in mhStreamTexts) {
            try {
                if (TextField(p[0]).text == MH_STREAM_HOUSE) {
                    TextField(p[0]).text = String(p[1]);
                }
            } catch (e2:Error) {
            }
        }
        mhStreamTexts = [];
        // The name tag and portraits come back by themselves (the page writes the real name once).
    }

    // Whether an MVS feature is on (the game's settings; on unless turned off).
    private static function mvsOpt(game:*, key:String):Boolean {
        try {
            return game.litePreference.data.dOptions[key] !== false;
        } catch (e:Error) {
        }
        return true;
    }

    private static function mhLayoutOn(game:*):Boolean {
        return mvsOpt(game, "mvsMobileLayout");
    }

    private static function mhOptLabel(game:*, o:Array):String {
        return o[1] + ": " + (mvsOpt(game, o[0]) ? "On" : "Off");
    }

    // The option a row or list entry stands for ("Name" once the row has split off its value).
    private static function mhOptFor(text:String):Array {
        for each (var o:Array in MH_OPTS) {
            if (text == o[1] || text.indexOf(o[1] + ":") == 0) {
                return o;
            }
        }
        return null;
    }

    // Tells the page every option's value once the game's settings are there.
    private static function mhOptSync(game:*):void {
        if (mhOptSynced || game == null || game.litePreference == null || game.litePreference.data == null) {
            return;
        }
        mhOptSynced = true;
        for each (var o:Array in MH_OPTS) {
            mhOptSend(o[0], mvsOpt(game, o[0]));
        }
    }

    private static function mhOptSend(key:String, on:Boolean):void {
        try {
            skua.Main.instance.external.call("mvsOption", key + ":" + (on ? "on" : "off"));
        } catch (e:Error) {
        }
    }

    // The Options window (mcOption) rebuilds game.litePref and draws it every time it opens (one
    // window is kept and reopened, so this can't be done once per window): while it's up and the list
    // lacks the entries, add them and draw the list again. Not while a search is typed: the search
    // list is drawn from a fresh rebuild, and clearing the search rebuilds it again.
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

    // The Options list groups rows into sections by name and sorts them, so names it doesn't know
    // land in "Other" at the bottom. Move the MVS rows, in MH_OPTS order, to the top of the first
    // section (Controls), drop any header left with no rows, and stack the list again: headers are
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
        var top:Number = DisplayObject(items[0]).y;
        var mine:Array = [];
        var rest:Array = [];
        for each (var d:* in items) {
            var o:Array = !mhIsHeader(d) && ("txtName" in d) && d.txtName != null ? mhOptFor(String(d.txtName.text)) : null;
            if (o != null) {
                mine.push({"row": d, "at": MH_OPTS.indexOf(o)});
            } else {
                rest.push(d);
            }
        }
        if (mine.length == 0 || rest.length == 0 || !mhIsHeader(rest[0])) {
            return;
        }
        mhOptPlaced[list] = true;
        mine.sortOn("at", Array.NUMERIC);
        var kept:Array = [];
        for (i = 0; i < rest.length; i++) {
            if (i > 0 && mhIsHeader(rest[i]) && (i == rest.length - 1 || mhIsHeader(rest[i + 1]))) {
                list.removeChild(rest[i]);
                continue;
            }
            kept.push(rest[i]);
        }
        var rowX:Number = kept.length > 1 ? DisplayObject(kept[1]).x : DisplayObject(mine[0].row).x;
        for (i = mine.length - 1; i >= 0; i--) {
            DisplayObject(mine[i].row).x = rowX;
            kept.splice(1, 0, mine[i].row);
        }
        var y:Number = top;
        for each (var e:DisplayObject in kept) {
            e.y = y;
            y += mhIsHeader(e) ? 26 : 35;
        }
        try {
            win.dRun = list.height - win.bg2.cntMask.height + 5;
        } catch (err:Error) {
        }
    }

    private static function mhIsHeader(d:*):Boolean {
        return getQualifiedClassName(d).indexOf("RowHeader") >= 0;
    }

    private static function mhOptInsert(game:*):Boolean {
        var list:Array = game.litePref as Array;
        if (list == null) {
            return false;
        }
        var added:Boolean = false;
        for each (var o:Array in MH_OPTS) {
            var have:Boolean = false;
            for (var i:int = 0; i < list.length; i++) {
                if (list[i] != null && mhOptFor(String(list[i].strName)) == o) {
                    have = true;
                    break;
                }
            }
            if (!have) {
                list.push({"strName": mhOptLabel(game, o), "btn": true, "sDesc": o[2]});
                added = true;
            }
        }
        return added;
    }

    private static function mhOptionRows(c:DisplayObjectContainer, game:*, depth:int):void {
        for (var i:int = 0; i < c.numChildren; i++) {
            var d:DisplayObject = c.getChildAt(i);
            if (getQualifiedClassName(d) == "liteAssets.listOptionsItem::listOptionsItemBtn") {
                var row:* = d;
                var ro:Array = row.txtName != null ? mhOptFor(String(row.txtName.text)) : null;
                if (ro == null) {
                    continue;
                }
                if (mhOptRows[row] == null) {
                    mhOptRows[row] = true;
                    row.btnActive.addEventListener(MouseEvent.CLICK, mhOptTap, false, 0, true);
                    var tag:Sprite = mhTag();
                    tag.x = Math.round(row.txtName.x + row.txtName.textWidth + 8);
                    tag.y = Math.round(row.txtName.y + 3);
                    row.addChild(tag);
                    mhOptTagOf[row] = tag;
                    var q:Sprite = mhHelpButton(ro);
                    q.x = Math.round(tag.x + tag.width + 6);
                    q.y = Math.round(tag.y - 2);
                    row.addChild(q);
                }
                mhOptRowOf[ro[0]] = row;
                mhOptLook(row, mvsOpt(game, ro[0]));
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
            var row:* = e.currentTarget.parent;
            var o:Array = mhOptFor(String(row.txtName.text));
            if (o != null) {
                mhOptSet(game, o, !mvsOpt(game, o[0]));
            }
        } catch (err:Error) {
        }
    }

    // Switches an MVS option (its pill, or the guide's button) and updates its row and the page.
    private static function mhOptSet(game:*, o:Array, on:Boolean):void {
        game.litePreference.data.dOptions[o[0]] = on;
        game.litePreference.flush();
        for each (var entry:* in game.litePref) {
            if (entry != null && mhOptFor(String(entry.strName)) == o) {
                entry.strName = mhOptLabel(game, o);
            }
        }
        var row:* = mhOptRowOf[o[0]];
        if (row != null && row.txtName != null) {
            // The row splits "Name: Value" into its name and pill right after this.
            row.txtName.text = mhOptLabel(game, o);
            mhOptLook(row, on);
        }
        mhOptSend(o[0], on);
    }

    // An MVS row that is off looks off, like the game's own OFF buttons: a dark pill and a dimmed name.
    private static function mhOptLook(row:*, on:Boolean):void {
        try {
            row.btnActive.transform.colorTransform = on ? new ColorTransform() : new ColorTransform(0.4, 0.34, 0.34, 1, 20, 14, 14, 0);
            row.txtName.alpha = on ? 1 : 0.6;
            var tag:Sprite = mhOptTagOf[row] as Sprite;
            if (tag != null) {
                tag.alpha = on ? 1 : 0.55;
            }
        } catch (err:Error) {
        }
    }

    // ---- (?) guide for each MVS option ----

    private static function mhHelpButton(o:Array):Sprite {
        var b:Sprite = new Sprite();
        b.buttonMode = true;
        b.useHandCursor = true;
        var g:Graphics = b.graphics;
        g.beginFill(0x000000, 0);
        g.drawRect(-5, -5, 25, 25);
        g.endFill();
        g.lineStyle(1.5, 0xD9B25A, 1);
        g.beginFill(0x1C1406, 1);
        g.drawCircle(7.5, 7.5, 7.5);
        g.endFill();
        var tf:TextField = mhText("?", 10, 0xF4DC98, true);
        tf.x = 7.5 - tf.width / 2;
        tf.y = 7.5 - tf.height / 2;
        tf.mouseEnabled = false;
        b.addChild(tf);
        b.addEventListener(MouseEvent.CLICK, function (e:MouseEvent):void {
            e.stopPropagation();
            mhGuideShow(o, b.stage);
        }, false, 0, true);
        return b;
    }

    private static function mhText(text:String, size:Number, color:uint, bold:Boolean, width:Number = 0, font:String = "Arial"):TextField {
        var tf:TextField = new TextField();
        tf.selectable = false;
        tf.defaultTextFormat = new TextFormat(font, size, color, bold);
        if (width > 0) {
            tf.wordWrap = true;
            tf.multiline = true;
            tf.width = width;
        }
        tf.autoSize = "left";
        tf.text = text;
        return tf;
    }

    private static function mhPill(label:String, red:Boolean, w:Number):Sprite {
        var p:Sprite = new Sprite();
        p.buttonMode = true;
        var g:Graphics = p.graphics;
        g.lineStyle(2, red ? 0xE7B65A : 0x7A6440, 1);
        g.beginFill(red ? 0xC8261B : 0x3E2C2C, 1);
        g.drawRoundRect(0, 0, w, 24, 8, 8);
        g.endFill();
        var tf:TextField = mhText(label, 11, red ? 0xFFFFFF : 0xD8CACA, true);
        tf.x = (w - tf.width) / 2;
        tf.y = (24 - tf.height) / 2;
        tf.mouseEnabled = false;
        p.addChild(tf);
        return p;
    }

    private static function mhGuideClose(e:Event = null):void {
        if (mhGuide != null) {
            mhGuide.removeEventListener(Event.ENTER_FRAME, mhGuideFrame);
            if (mhGuide.parent != null) {
                mhGuide.parent.removeChild(mhGuide);
            }
        }
        mhGuide = null;
        mhGuideKnob = null;
    }

    // Dynamic Joystick's sample: the knob moves around under the thumb.
    private static function mhGuideFrame(e:Event):void {
        if (mhGuideKnob == null) {
            return;
        }
        mhGuideT += 0.08;
        mhGuideKnob.x = Math.cos(mhGuideT) * 10;
        mhGuideKnob.y = Math.sin(mhGuideT * 1.3) * 8;
    }

    private static function mhGuideShow(o:Array, st:*):void {
        mhGuideClose();
        if (st == null) {
            return;
        }
        var game:* = skua.Main.instance.getGame();
        var on:Boolean = mvsOpt(game, o[0]);
        var W:Number = st.stageWidth > 0 ? st.stageWidth : 960;
        var H:Number = st.stageHeight > 0 ? st.stageHeight : 550;
        var root:Sprite = new Sprite();
        var shade:Sprite = new Sprite();
        shade.graphics.beginFill(0x000000, 0.45);
        shade.graphics.drawRect(0, 0, W, H);
        shade.graphics.endFill();
        shade.addEventListener(MouseEvent.CLICK, mhGuideClose, false, 0, true);
        root.addChild(shade);

        var pw:Number = 300;
        var panel:Sprite = new Sprite();
        root.addChild(panel);
        var y:Number = 12;
        var title:TextField = mhText(o[1], 16, 0xF4E2B0, false, 0, "_serif");
        title.x = 14;
        title.y = y;
        panel.addChild(title);
        var close:Sprite = mhPill("X", true, 24);
        close.x = pw - 36;
        close.y = y;
        close.addEventListener(MouseEvent.CLICK, mhGuideClose, false, 0, true);
        panel.addChild(close);
        y += 30;
        var what:String = String(o[2]);
        if (what.indexOf("MVS: ") == 0) {
            what = what.substr(5);
        }
        var body:TextField = mhText(what, 11, 0xE4E4E4, false, pw - 28);
        body.x = 14;
        body.y = y;
        panel.addChild(body);
        y += body.height + 8;

        if (o[0] == "mvsJoystick") {
            var dw:Number = pw - 28;
            var demo:Sprite = new Sprite();
            var dg:Graphics = demo.graphics;
            dg.lineStyle(1, 0x4A3A1A, 1);
            dg.beginFill(0x15151C, 1);
            dg.drawRoundRect(0, 0, dw, 92, 8, 8);
            dg.endFill();
            dg.lineStyle(0, 0, 0);
            dg.beginFill(0xE8C15A, 0.16);
            dg.drawRect(1, 1, dw * 0.35, 90);
            dg.endFill();
            dg.lineStyle(1.5, 0xE8C15A, 0.9);
            dg.moveTo(dw * 0.35, 4);
            dg.lineTo(dw * 0.35, 88);
            dg.lineStyle(1.5, 0xFFFFFF, 0.6);
            dg.drawCircle(46, 54, 22);
            dg.lineStyle(0, 0, 0);
            dg.beginFill(0x7AA7FF, 1);
            dg.drawRoundRect(170, 36, 14, 30, 6, 6);
            dg.endFill();
            var zl:TextField = mhText("left 35%", 9, 0xF4DC98, false);
            zl.x = 5;
            zl.y = 3;
            demo.addChild(zl);
            var knobHolder:Sprite = new Sprite();
            knobHolder.x = 46;
            knobHolder.y = 54;
            mhGuideKnob = new Shape();
            mhGuideKnob.graphics.beginFill(0xFFFFFF, 1);
            mhGuideKnob.graphics.drawCircle(0, 0, 9);
            mhGuideKnob.graphics.endFill();
            knobHolder.addChild(mhGuideKnob);
            demo.addChild(knobHolder);
            demo.x = 14;
            demo.y = y;
            panel.addChild(demo);
            y += 100;
        }

        var texts:Array = MH_OPT_ONOFF[o[0]] as Array;
        if (texts != null) {
            for (var i:int = 0; i < 2; i++) {
                var h:TextField = mhText(i == 0 ? "WHEN ON" : "WHEN OFF", 10, 0xE8C15A, true);
                h.x = 14;
                h.y = y;
                panel.addChild(h);
                y += h.height;
                var t:TextField = mhText(String(texts[i]), 11, 0xE4E4E4, false, pw - 28);
                t.x = 14;
                t.y = y;
                panel.addChild(t);
                y += t.height + 6;
            }
        }
        y += 4;
        var bw:Number = (pw - 38) / 2;
        var turn:Sprite = mhPill(on ? "Turn off" : "Turn on", !on, bw);
        turn.x = 14;
        turn.y = y;
        turn.addEventListener(MouseEvent.CLICK, function (e:MouseEvent):void {
            mhOptSet(game, o, !mvsOpt(game, o[0]));
            mhGuideClose();
        }, false, 0, true);
        panel.addChild(turn);
        var ok:Sprite = mhPill("Got it", true, bw);
        ok.x = 24 + bw;
        ok.y = y;
        ok.addEventListener(MouseEvent.CLICK, mhGuideClose, false, 0, true);
        panel.addChild(ok);
        y += 36;

        var pg:Graphics = panel.graphics;
        pg.lineStyle(3, 0xC99A3A, 1);
        pg.beginFill(0x0A0806, 0.97);
        pg.drawRoundRect(0, 0, pw, y, 16, 16);
        pg.endFill();
        pg.lineStyle(1, 0x3B2B0E, 1);
        pg.drawRoundRect(4, 4, pw - 8, y - 8, 12, 12);
        panel.x = Math.round((W - pw) / 2);
        panel.y = Math.max(6, Math.round((H - y) / 2));
        mhGuide = root;
        root.addEventListener(Event.ENTER_FRAME, mhGuideFrame, false, 0, true);
        st.addChild(root);
    }

    // ---- Dynamic joystick: the game's joystick takes the left 35% of the screen ----

    // The game's own zone is a square around the joystick's corner; a touch just outside it walks
    // to that spot instead. The game's mobile HUD sits under its interface (chat, bars, portraits),
    // which keep their own touches, and taps that don't move are passed on by the joystick itself.
    private static const MH_JOY_ZONE:Rectangle = new Rectangle(0, 0, 336, 500);
    private static var mhJoyTick:int = 0;
    private static var mhJoy:* = null;
    // The zone the game last set (put back when this is off) and the one set here.
    private static var mhJoyGame:Rectangle = null;
    private static var mhJoyOurs:Rectangle = null;

    private static function mhJoystick(game:*, on:Boolean):void {
        if (++mhJoyTick % 15 != 0) {
            return;
        }
        if (mhJoy == null || mhJoy.stage == null) {
            mhJoy = mhFind(game.ui as DisplayObjectContainer, "liteAssets.mobile::Joystick", 0);
            mhJoyGame = null;
            mhJoyOurs = null;
            if (mhJoy == null) {
                return;
            }
        }
        var hit:DisplayObject = mhJoy.numChildren > 0 ? mhJoy.getChildAt(0) : null;
        if (hit == null || mhJoy.pressed) {
            return;
        }
        var now:Rectangle = hit.getBounds(mhJoy);
        if (mhJoyOurs == null || !mhRectNear(now, mhJoyOurs)) {
            // Not ours (first look, or the game laid its controls out again): that's the game's zone.
            mhJoyGame = now;
            mhJoyOurs = null;
        }
        if (!on) {
            if (mhJoyOurs != null && mhJoyGame != null) {
                mhJoy.setZone(mhJoyGame);
                mhJoyOurs = null;
            }
            return;
        }
        if (mhJoyOurs != null) {
            return;
        }
        var tl:Point = mhJoy.globalToLocal(new Point(MH_JOY_ZONE.x, MH_JOY_ZONE.y));
        var br:Point = mhJoy.globalToLocal(new Point(MH_JOY_ZONE.right, MH_JOY_ZONE.bottom));
        mhJoy.setZone(new Rectangle(tl.x, tl.y, br.x - tl.x, br.y - tl.y));
        mhJoyOurs = hit.getBounds(mhJoy);
    }

    private static function mhRectNear(a:Rectangle, b:Rectangle):Boolean {
        return Math.abs(a.x - b.x) < 1 && Math.abs(a.y - b.y) < 1 && Math.abs(a.width - b.width) < 1 && Math.abs(a.height - b.height) < 1;
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
        try {
            mhStreamer(game);
        } catch (es:Error) {
        }
        if (mhOn) {
            try {
                mhOptSync(game);
                mhOptions(game);
            } catch (eo:Error) {
            }
            try {
                if (mhMobile(game)) {
                    mhJoystick(game, mvsOpt(game, "mvsJoystick"));
                }
            } catch (ej:Error) {
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
