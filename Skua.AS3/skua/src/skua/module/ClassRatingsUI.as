package skua.module {
import flash.display.DisplayObject;
import flash.display.DisplayObjectContainer;
import flash.display.Graphics;
import flash.display.Sprite;
import flash.events.MouseEvent;
import flash.text.TextField;
import flash.text.TextFieldAutoSize;
import flash.text.TextFormat;
import flash.utils.Dictionary;
import flash.utils.getTimer;

import skua.Main;

/**
 * MVS class ratings inside the game's own inventory, drawn only on this screen: 0-5 stars beside each
 * class in the list, the stars for every use case in the Preview panel, and a chip beside the panel's
 * title that switches the use case (A-Z, Farming, Solo, Bossing, Ultra, PvP). For a use case the class
 * list is ordered best first by its stars (favourites stay on top); A-Z is the game's own order. Stars
 * are drawn as shapes (the game's fonts have no star glyph). Nothing here changes game data or sends
 * anything to the server; the app pushes the ratings in with mvsClassRatings.
 *
 * Sorting: the game's list (LPFFrameInvAQ) keeps its order private, but each row has a public
 * bind(item, root, selected, equipped) and a tap selects whatever item the row holds. So the game's order
 * is modelled here (classes matching the search, by name, level, id, favourites first), checked against
 * every row the game bound itself, and only when they agree are the rows rebound in star order.
 *
 * Without mxmlc, bin/skua.swf was patched with FFDec -importScript, which only replaces existing
 * scripts: there this code lives as static members of Modules (run from Modules.handleFrame) and
 * Externalizer registers Modules.setRatings / Modules.setFocus. Same behaviour as this module.
 */
public class ClassRatingsUI extends Module {
    private static var _ratings:Object = {};
    private static var _hasRatings:Boolean = false;
    private static var _focus:String = "A-Z";
    private static const FOCI:Array = ["A-Z", "Farm", "Solo", "Boss", "Ultra", "PvP"];
    private static const FOCUS_LABELS:Object = {"A-Z": "A-Z", "Farm": "Farming", "Solo": "Solo", "Boss": "Bossing", "Ultra": "Ultra", "PvP": "PvP"};
    private static const GOLD:uint = 0xE8C15A;
    private static const SCAN_MS:int = 250;
    private static const GRADE_NAME:String = "mvsGrade";
    private static const CHIP_NAME:String = "mvsFocus";
    private static const INFO_NAME:String = "mvsInfo";
    private static const ROW_H:Number = 30;

    // The game's list row height: 30, unless MVS replaced it for touch screens (see TouchInventoryUI).
    private static function rowH():Number {
        try {
            var c:* = Main.instance.getGame().loaderInfo.applicationDomain.getDefinition("LPFFrameInvAQ");
            if (c.ROW_H > 0) return c.ROW_H;
        } catch (e:Error) {
        }
        return ROW_H;
    }

    private var _lastScan:int = 0;
    // What each star badge and Preview list shows, to redraw only on changes.
    private var _drawn:Dictionary = new Dictionary(true);
    // Left edge of the "Lvl" column (it moves with the level's digits): every row's stars line up on it.
    private var _lvlCol:Number = 10000;
    // Title fields already moved left to make room for the chip.
    private var _titleMoved:Dictionary = new Dictionary(true);
    // The game's class list rows and its search box, found by scan.
    private var _holder:DisplayObjectContainer = null;
    private var _searchTf:TextField = null;
    // Rows showing a different item than the game put there, and rows with our tap listener.
    private var _bound:Dictionary = new Dictionary(true);
    private var _hooked:Dictionary = new Dictionary(true);
    private var _sel:Object = null;
    // The game's order as modelled, its state key, the last key confirmed against the game's own rows,
    // and the order shown for the focus.
    private var _order:Array = null;
    private var _orderKey:String = null;
    private var _okKey:String = null;
    private var _target:Array = null;
    private var _targetKey:String = null;
    private var _favStore:* = undefined;

    public function ClassRatingsUI() {
        super("ClassRatingsUI");
        enabled = true;
    }

    /** {"class name in lower case": [pve, pvp, farm, solo, boss, ultra, "ultra role"]}, ratings 1-10. */
    public static function setRatings(json:String):void {
        try {
            _ratings = JSON.parse(json);
            _hasRatings = true;
        } catch (e:Error) {
        }
    }

    public static function setFocus(focus:String):void {
        if (FOCI.indexOf(focus) >= 0) {
            _focus = focus;
        }
    }

    /** 1-10 as whole stars 0-5 (9 and 10 are both five). */
    private static function starCount(r:*):int {
        var n:int = int((int(r) + 1) / 2);
        return n < 0 ? 0 : (n > 5 ? 5 : n);
    }

    private static function lookup(name:String):Array {
        if (name == null) return null;
        var key:String = name.toLowerCase();
        var r:Array = _ratings[key] as Array;
        if (r == null) {
            // Variants share a kit with the base class: "Void Highlord (IoDA)".
            var paren:int = key.indexOf(" (");
            if (paren > 0) r = _ratings[key.substr(0, paren)] as Array;
        }
        return r;
    }

    /** Index in the ratings array for the use case (overall PvE for All). */
    private static function focusIndex():int {
        switch (_focus) {
            case "Farm": return 2;
            case "Solo": return 3;
            case "Boss": return 4;
            case "Ultra": return 5;
            case "PvP": return 1;
        }
        return 0;
    }

    /** Five stars from x, centred on cy: filled gold for the rating, dark outlines for the rest. */
    private static function drawStars(g:Graphics, x:Number, cy:Number, count:int, radius:Number, gap:Number):void {
        for (var i:int = 0; i < 5; i++) {
            var cx:Number = x + radius + i * gap;
            if (i < count) {
                g.lineStyle();
                g.beginFill(GOLD, 1);
            } else {
                g.lineStyle(1, 0x8A6A2E, 0.9);
                g.beginFill(0x2A231C, 0.85);
            }
            for (var k:int = 0; k <= 10; k++) {
                var a:Number = -Math.PI / 2 + k * Math.PI / 5;
                var rr:Number = (k % 2 == 0) ? radius : radius * 0.45;
                var px:Number = cx + rr * Math.cos(a);
                var py:Number = cy + rr * Math.sin(a);
                if (k == 0) g.moveTo(px, py); else g.lineTo(px, py);
            }
            g.endFill();
        }
        g.lineStyle();
    }

    override public function onFrame(game:*):void {
        if (!_hasRatings) return;
        var now:int = getTimer();
        try {
            var popup:* = game.ui.mcPopup;
            if (popup == null || !popup.visible || popup.currentLabel != "Inventory") {
                _holder = null;
                _searchTf = null;
                return;
            }
            // Every frame, so a row scrolled into view never shows the game's item for long.
            if (_holder != null) sortRows(game);
            if (now - _lastScan < SCAN_MS) return;
            _lastScan = now;
            var inventory:DisplayObjectContainer = popup.getChildByName("mcInventory") as DisplayObjectContainer;
            if (inventory != null) scan(inventory, 0);
        } catch (e:Error) {
        }
    }

    private function scan(container:DisplayObjectContainer, depth:int):void {
        for (var i:int = 0; i < container.numChildren; i++) {
            var child:DisplayObject = container.getChildAt(i);
            if (child is TextField) {
                var tf:TextField = child as TextField;
                if (tf.name == "tInfo") {
                    decorateInfo(tf);
                } else if (tf.name == "tTitle" && tf.text == "Inventory" && tf.parent != null) {
                    decorateTitle(tf);
                } else if (tf.type == "input") {
                    _searchTf = tf;
                }
                continue;
            }
            var sub:DisplayObjectContainer = child as DisplayObjectContainer;
            if (sub == null || sub.name == CHIP_NAME || sub.name == INFO_NAME || sub.name == GRADE_NAME) continue;
            var item:* = null;
            try {
                item = sub["item"];
            } catch (e:Error) {
            }
            if (item != null && item.sType != null) {
                decorateRow(sub, item);
                if (sub.parent != null) _holder = sub.parent;
                continue;
            }
            if (depth < 6) scan(sub, depth + 1);
        }
    }

    private static function makeLabel(size:int, color:uint):TextField {
        var tf:TextField = new TextField();
        tf.selectable = false;
        tf.mouseEnabled = false;
        tf.autoSize = TextFieldAutoSize.LEFT;
        tf.defaultTextFormat = new TextFormat("Arial", size, color, true);
        return tf;
    }

    /** Stars beside the class name, before the "Lvl" column, or under the name when the row is full. */
    private function decorateRow(row:DisplayObjectContainer, item:*):void {
        var badge:Sprite = row.getChildByName(GRADE_NAME) as Sprite;
        var r:Array = item.sType == "Class" ? lookup(item.sName) : null;
        if (r == null) {
            if (badge != null) badge.visible = false;
            return;
        }
        if (badge == null) {
            badge = new Sprite();
            badge.name = GRADE_NAME;
            badge.mouseEnabled = false;
            badge.mouseChildren = false;
            row.addChild(badge);
        }
        var stars:int = starCount(r[focusIndex()]);
        var width:Number = 4 * 8 + 7.2;
        if (_drawn[badge] !== stars) {
            var g:Graphics = badge.graphics;
            g.clear();
            // A dark pill keeps the stars readable over the row's AC coin.
            g.lineStyle(1, 0x5A4528, 1);
            g.beginFill(0x120E0A, 0.9);
            g.drawRoundRect(-5, -7, width + 10, 14, 12, 12);
            g.endFill();
            drawStars(g, 0, 0, stars, 3.6, 8);
            _drawn[badge] = stars;
        }
        var nameTf:TextField = null;
        var lvlX:Number = 0;
        for (var i:int = 0; i < row.numChildren; i++) {
            var tf:TextField = row.getChildAt(i) as TextField;
            if (tf == null) continue;
            // Long names are cut short with "…" ("Chaos Slayer Mys…, Rank 10"): match the part shown.
            var shown:String = tf.text;
            var cut:int = shown.indexOf("…");
            if (cut < 0) cut = shown.indexOf("...");
            if (shown.indexOf(item.sName) == 0 || (cut >= 4 && String(item.sName).indexOf(shown.substr(0, cut)) == 0)) nameTf = tf;
            else if (tf.text == "Lvl") lvlX = tf.x;
        }
        if (nameTf == null) {
            badge.visible = false;
            return;
        }
        if (lvlX <= 0) lvlX = row.width / (row.scaleX || 1) - 50;
        if (lvlX < _lvlCol) _lvlCol = lvlX;
        badge.x = _lvlCol - 12 - width;
        badge.y = nameTf.y + 2 + nameTf.textHeight / 2;
        badge.visible = true;
    }

    /**
     * The use-case stars in the Preview's empty top-left corner, above the class name and left of the
     * character, so the game's own text (name, rank, description) stays as it is.
     */
    private function decorateInfo(tf:TextField):void {
        var parent:DisplayObjectContainer = tf.parent;
        if (parent == null) return;
        var info:Sprite = parent.getChildByName(INFO_NAME) as Sprite;
        var lines:Array = tf.text.split("\r");
        var r:Array = (lines.length >= 2 && String(lines[1]).indexOf("Class") == 0) ? lookup(lines[0]) : null;
        if (r == null || !parent.visible) {
            if (info != null) info.visible = false;
            return;
        }
        if (info == null) {
            info = new Sprite();
            info.name = INFO_NAME;
            info.mouseEnabled = false;
            info.mouseChildren = false;
            parent.addChild(info);
        }
        var key:String = _focus + "|" + r.join(",");
        if (_drawn[info] != key) {
            drawInfo(info, r);
            _drawn[info] = key;
        }
        info.x = tf.x + 2;
        info.y = tf.y - info.height - 4;
        info.visible = true;
    }

    /** Farm, Solo, Boss, Ultra and PvP one per line with their stars, then the ultra role. */
    private function drawInfo(info:Sprite, r:Array):void {
        while (info.numChildren > 0) info.removeChildAt(0);
        var g:Graphics = info.graphics;
        g.clear();
        var cells:Array = [["Farm", 2, "Farm"], ["Solo", 3, "Solo"], ["Boss", 4, "Boss"], ["Ultra", 5, "Ultra"], ["PvP", 1, "PvP"]];
        var role:String = r.length > 6 ? String(r[6]) : "";
        var rowH:Number = 13;
        var height:Number = cells.length * rowH + (role.length > 0 ? rowH : 0) + 6;
        // A dark card so the list reads over the preview's background art.
        g.lineStyle(1, 0x5A4528, 0.9);
        g.beginFill(0x120E0A, 0.78);
        g.drawRoundRect(-5, -3, 92, height, 10, 10);
        g.endFill();
        for (var i:int = 0; i < cells.length; i++) {
            var c:Array = cells[i];
            var y:Number = i * rowH;
            var focused:Boolean = c[2] == _focus;
            var label:TextField = makeLabel(10, focused ? 0xFFFFFF : GOLD);
            label.text = c[0];
            label.x = 0;
            label.y = y - 1;
            info.addChild(label);
            drawStars(g, 38, y + 7, starCount(r[c[1]]), 3.6, 9);
        }
        if (role.length > 0) {
            var roleLabel:TextField = makeLabel(9, 0xC8B89A);
            roleLabel.text = role;
            roleLabel.x = 0;
            roleLabel.y = cells.length * rowH - 1;
            info.addChild(roleLabel);
        }
    }

    /** The use-case chip right of the panel's "Inventory" title, which moves left to make room. */
    private function decorateTitle(title:TextField):void {
        var frame:DisplayObjectContainer = title.parent;
        if (_titleMoved[title] !== true) {
            // The title is centred in a wide field: move its text to the left edge.
            title.x = title.x - (title.width - title.textWidth) / 2 + 14;
            _titleMoved[title] = true;
        }
        var chip:Sprite = frame.getChildByName(CHIP_NAME) as Sprite;
        if (chip == null) {
            chip = new Sprite();
            chip.name = CHIP_NAME;
            chip.buttonMode = true;
            chip.useHandCursor = true;
            chip.mouseChildren = false;
            var label:TextField = makeLabel(11, GOLD);
            label.name = "t";
            label.x = 8;
            label.y = 1;
            chip.addChild(label);
            chip.addEventListener(MouseEvent.CLICK, onChipClick, false, 0, true);
            // The press must not reach the frame (it drags the window).
            chip.addEventListener(MouseEvent.MOUSE_DOWN, stopEvent, false, 0, true);
            chip.addEventListener(MouseEvent.MOUSE_UP, stopEvent, false, 0, true);
            frame.addChild(chip);
        }
        var t:TextField = chip.getChildByName("t") as TextField;
        var text:String = "MVS: " + FOCUS_LABELS[_focus] + "  >";
        if (t.text != text) {
            t.text = text;
            chip.graphics.clear();
            chip.graphics.lineStyle(1, 0x8A6A2E);
            chip.graphics.beginFill(0x1C1714, 0.95);
            chip.graphics.drawRoundRect(0, 0, t.width + 16, 20, 10, 10);
            chip.graphics.endFill();
        }
        chip.x = title.x + (title.width - title.textWidth) / 2 + title.textWidth + 14;
        chip.y = title.y + (title.height - 20) / 2;
    }

    private function favStore(game:*):* {
        if (_favStore === undefined) {
            _favStore = null;
            try {
                _favStore = game.loaderInfo.applicationDomain.getDefinition("liteAssets.draw.FavStore");
            } catch (e:Error) {
            }
        }
        return _favStore;
    }

    // The game's own order for class items (LPFFrameInvAQ.compareItems): name, level high first, id.
    private static function gameCompare(a:Object, b:Object):int {
        var an:String = String(a.sName);
        var bn:String = String(b.sName);
        if (an != bn) return an < bn ? -1 : 1;
        var al:int = int(a.iLvl);
        var bl:int = int(b.iLvl);
        if (al != bl) return al > bl ? -1 : 1;
        var ai:int = int(a.ItemID);
        var bi:int = int(b.ItemID);
        if (ai != bi) return ai < bi ? -1 : 1;
        return 0;
    }

    private static function starCompare(a:Object, b:Object):int {
        if (a.f != b.f) return a.f ? -1 : 1;
        if (a.s != b.s) return a.s > b.s ? -1 : 1;
        return a.i - b.i;
    }

    // The class list as the game orders it for the current search, and the order shown for the focus.
    private function modelOrder(game:*, fav:*):void {
        var items:Array = game.world.myAvatar.items as Array;
        var q:String = _searchTf != null ? _searchTf.text : "";
        var key:String = q + "|" + (items != null ? items.length : 0) + "|" + (fav != null ? fav.version : 0);
        if (key != _orderKey) {
            _orderKey = key;
            _targetKey = null;
            var lq:String = q.toLowerCase();
            var list:Array = [];
            var it:*;
            for each (it in items) {
                if (it == null || it.sType != "Class") continue;
                if (lq.length > 0 && String(it.sName).toLowerCase().indexOf(lq) < 0) continue;
                list.push(it);
            }
            list.sort(gameCompare);
            if (fav != null && fav.count > 0) {
                var favs:Array = [];
                var rest:Array = [];
                for each (it in list) {
                    if (fav.has(it.ItemID)) favs.push(it); else rest.push(it);
                }
                list = favs.concat(rest);
            }
            _order = list;
        }
        var tkey:String = key + "|" + _focus;
        if (tkey == _targetKey) return;
        _targetKey = tkey;
        if (_focus == "A-Z") {
            _target = _order;
            return;
        }
        var fi:int = focusIndex();
        var ranked:Array = [];
        for (var i:int = 0; i < _order.length; i++) {
            var o:* = _order[i];
            var r:Array = lookup(o.sName);
            ranked.push({"it": o, "s": r != null ? starCount(r[fi]) : -1, "i": i, "f": fav != null && fav.has(o.ItemID)});
        }
        ranked.sort(starCompare);
        _target = [];
        for each (var e:Object in ranked) _target.push(e.it);
    }

    // Rebinds the visible class rows so the list reads in star order for the focus (the game's for A-Z).
    private function sortRows(game:*):void {
        var holder:DisplayObjectContainer = _holder;
        if (holder == null || holder.stage == null) return;
        var rows:Array = [];
        var row:*;
        var i:int;
        for (i = 0; i < holder.numChildren; i++) {
            row = holder.getChildAt(i);
            var it:* = null;
            try {
                it = row["item"];
            } catch (e:Error) {
            }
            if (it == null || !row.visible) continue;
            // Not the class list (or a mix): leave it as the game drew it.
            if (it.sType != "Class") return;
            rows.push(row);
        }
        if (rows.length == 0) return;
        var fav:* = favStore(game);
        modelOrder(game, fav);
        var idx:int;
        var checked:Boolean = false;
        for each (row in rows) {
            if (_bound[row] != null && _bound[row] === row.item) continue;
            delete _bound[row];
            idx = Math.round(row.y / rowH());
            if (_order[idx] !== row.item) {
                // The game shows something this model does not know (a filter, a pending search).
                _okKey = null;
                return;
            }
            checked = true;
        }
        if (checked) _okKey = _orderKey;
        else if (_okKey != _orderKey) return;
        for each (row in rows) {
            idx = Math.round(row.y / rowH());
            var want:* = _target[idx];
            if (want == null) continue;
            if (_hooked[row] !== true) {
                row.addEventListener(MouseEvent.CLICK, onRowTap, false, 0, true);
                _hooked[row] = true;
            }
            if (row.item !== want) {
                row.bind(want, game, want === _sel, want.bEquip == 1 || want.bWear == 1);
                decorateRow(row as DisplayObjectContainer, want);
            }
            if (want !== _order[idx]) _bound[row] = want;
            else delete _bound[row];
        }
    }

    private function onRowTap(e:MouseEvent):void {
        try {
            _sel = e.currentTarget.item;
        } catch (err:Error) {
        }
    }

    private function stopEvent(e:MouseEvent):void {
        e.stopPropagation();
    }

    private function onChipClick(e:MouseEvent):void {
        e.stopPropagation();
        _focus = FOCI[(FOCI.indexOf(_focus) + 1) % FOCI.length];
        _lastScan = 0;
        // Never leave a text field focused (that opens the phone's keyboard).
        try {
            var chip:DisplayObject = e.currentTarget as DisplayObject;
            if (chip != null && chip.stage != null) chip.stage.focus = null;
        } catch (focusErr:Error) {
        }
        try {
            Main.instance.external.call("mvsRatingFocus", _focus);
        } catch (err:Error) {
        }
    }
}
}
