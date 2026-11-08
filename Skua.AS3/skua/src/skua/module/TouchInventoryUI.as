package skua.module {

import flash.display.DisplayObject;
import flash.display.DisplayObjectContainer;
import flash.display.Sprite;
import flash.events.MouseEvent;
import flash.geom.Point;
import flash.geom.Rectangle;
import flash.text.TextField;
import flash.text.TextFormat;
import flash.utils.Dictionary;
import flash.utils.getQualifiedClassName;

import skua.Main;

// MVS touch-friendly item lists (phones). Merged into Modules as static members (see the
// scratchpad merge script and the memory note on skua.swf patching): touchInvFrame runs from
// Modules.handleFrame and Externalizer registers "mvsTouchUi" -> Modules.setTouchUi.
//
// The rows themselves get taller in Ruffle (MVS overrides LPFFrameInvAQ.ROW_H for phones, which
// every list window and its row widget use). This module does the part that has no constant:
// the inventory's column of category icons (under the Q key) is drawn 1.4x larger and, as it no
// longer fits beside the list, scrolls on its own (finger swipes arrive as wheel steps). The taller
// rows are laid out for the room they have: "Lvl" stacked over the level at the right edge, the class
// stars (ClassRatingsUI's badge) larger beside it, and the name shown in full, wrapping to a second
// line instead of being cut short with "...".
public class TouchInventoryUI extends Module {

    private static const TI_SCALE:Number = 1.4;
    private static const TI_STRIP_X:Number = 6;
    private static const TI_LIST_Y:Number = 100;
    private static const TI_LIST_H:Number = 336;
    private static const TI_STRIP_W:Number = 30;
    private static const TI_NAME:String = "mvsTouchStrip";
    private static const TI_STARS_SCALE:Number = 1.35;
    // Where the row widget puts the name (set once when the row is made; rows are reused).
    private static const TI_NAME_X:Number = 38;

    private static var tiOn:Boolean = false;

    public function TouchInventoryUI() {
        super("TouchInventoryUI");
    }

    public static function setTouchUi(on:Boolean):void {
        tiOn = on;
    }

    override public function onFrame(game:*):void {
        if (!tiOn) {
            return;
        }
        var popup:DisplayObjectContainer = game.ui.mcPopup as DisplayObjectContainer;
        if (popup == null || popup.numChildren == 0) {
            return;
        }
        tiVisit(popup, 0);
    }

    private function tiVisit(c:DisplayObjectContainer, depth:int):void {
        var i:int = 0;
        while (i < c.numChildren) {
            var d:DisplayObject = c.getChildAt(i);
            if (d is DisplayObjectContainer) {
                if (getQualifiedClassName(d) == "LPFFrameInvAQ") {
                    tiPatch(d as DisplayObjectContainer);
                } else if (depth < 5) {
                    tiVisit(d as DisplayObjectContainer, depth + 1);
                }
            }
            i++;
        }
    }

    // The strip is the frame's Sprite at (14, LIST_Y) holding one Sprite of icons per tab.
    private function tiPatch(frame:DisplayObjectContainer):void {
        var i:int = 0;
        while (i < frame.numChildren) {
            var s:Sprite = frame.getChildAt(i) as Sprite;
            if (s != null && (s.name == TI_NAME || (s.x == 14 && s.y == TI_LIST_Y && s.numChildren > 0 && s.getChildAt(0) is Sprite))) {
                if (s.name != TI_NAME) {
                    s.name = TI_NAME;
                    s.x = TI_STRIP_X;
                    s.scaleX = s.scaleY = TI_SCALE;
                    s.scrollRect = new Rectangle(0, 0, TI_STRIP_W, TI_LIST_H / TI_SCALE);
                    s.addEventListener(MouseEvent.MOUSE_WHEEL, tiWheel, false, 0, true);
                }
                tiScroll(s, 0);
            }
            i++;
        }
        tiVisible = [];
        tiRows(frame, 0);
        tiSort(frame);
        tiPreview(frame.parent, 0);
    }

    // ---- MVS sort for gear lists (weapons, armor, helms, capes) by a damage type or a boost ----
    // The game's list (LPFFrameInvAQ) keeps its order private, but each row has a public
    // bind(item, root, selected, equipped) and a tap acts on whatever item the row holds. So, as
    // ClassRatingsUI does for classes, the game's order is modelled here (that category's items
    // matching the search, in the game's compareItems order, favourites first), checked against every
    // row the game bound itself, and only when they agree are the rows rebound in the chosen order.
    private static const TI_SORTS:Array = [["A-Z", null], ["All dmg", "dmgall"], ["Undead", "undead"], ["Human", "human"], ["Chaos", "chaos"], ["Dragonkin", "dragonkin"], ["Orc", "orc"], ["Drakath", "drakath"], ["Elemental", "elemental"], ["Gold", "gold"], ["XP", "exp"], ["Rep", "rep"], ["Class pts", "cp"]];
    // The game's own type order (LPFFrameInvAQ.SORT_ORDER).
    private static const TI_TYPE_ORDER:Array = ["Note", "Resource", "Item", "Quest Item", "ServerUse", "Enhancement", "Sword", "Axe", "Gauntlet", "Dagger", "HandGun", "Rifle", "Gun", "Whip", "Bow", "Mace", "Polearm", "Staff", "Wand", "Class", "Armor", "Helm", "Cape", "Misc", "Earring", "Amulet", "Necklace", "Belt", "Ring", "Pet"];
    private static var tiSortIdx:int = 0;
    private static var tiVisible:Array = [];
    // Rows showing a different item than the game put there, rows with our tap listener, the item
    // last tapped (the game's selection).
    private static var tiBound:Dictionary = new Dictionary(true);
    private static var tiHooked:Dictionary = new Dictionary(true);
    private static var tiSel:* = null;
    private static var tiOrder:Array = null;
    private static var tiOrderKey:String = null;
    private static var tiOkKey:String = null;
    private static var tiTarget:Array = null;
    private static var tiTargetKey:String = null;

    private function tiSort(frame:DisplayObjectContainer):void {
        var cat:String = null;
        var rows:Array = [];
        for each (var row:* in tiVisible) {
            var it:* = null;
            try {
                it = row.item;
            } catch (e:Error) {
            }
            if (it == null) {
                continue;
            }
            // Only a list of one gear category (a weapons, armor, helms or capes filter) is sorted.
            if (TI_GEAR.indexOf(String(it.sES)) < 0 || (cat != null && cat != String(it.sES))) {
                tiSortChip(frame, false);
                return;
            }
            cat = String(it.sES);
            rows.push(row);
        }
        tiSortChip(frame, cat != null);
        if (cat == null) {
            return;
        }
        var game:* = Main.instance.getGame();
        var fav:* = null;
        try {
            fav = game.loaderInfo.applicationDomain.getDefinition("liteAssets.draw.FavStore");
        } catch (e2:Error) {
        }
        tiModel(game, cat, frame, fav);
        var rowH:Number = tiRowH();
        var idx:int;
        var checked:Boolean = false;
        for each (row in rows) {
            if (tiBound[row] != null && tiBound[row] === row.item) {
                continue;
            }
            delete tiBound[row];
            idx = Math.round(row.y / rowH);
            if (tiOrder[idx] !== row.item) {
                // The game shows something this model does not know (a filter, a pending search).
                tiOkKey = null;
                return;
            }
            checked = true;
        }
        if (checked) {
            tiOkKey = tiOrderKey;
        } else if (tiOkKey != tiOrderKey) {
            return;
        }
        for each (row in rows) {
            idx = Math.round(row.y / rowH);
            var want:* = tiTarget[idx];
            if (want == null) {
                continue;
            }
            if (tiHooked[row] !== true) {
                row.addEventListener(MouseEvent.CLICK, tiRowTap, false, 0, true);
                tiHooked[row] = true;
            }
            if (row.item !== want) {
                row.bind(want, game, want === tiSel, want.bEquip == 1 || want.bWear == 1);
                tiRow(row);
            }
            if (want !== tiOrder[idx]) {
                tiBound[row] = want;
            } else {
                delete tiBound[row];
            }
        }
    }

    private function tiRowTap(e:MouseEvent):void {
        try {
            tiSel = e.currentTarget.item;
        } catch (err:Error) {
        }
    }

    // The category's items as the game lists them, and the order shown for the chosen sort.
    private function tiModel(game:*, cat:String, frame:DisplayObjectContainer, fav:*):void {
        var items:Array = game.world.myAvatar.items as Array;
        var search:TextField = tiFindSearch(frame, 0);
        var q:String = search != null ? search.text.toLowerCase() : "";
        var key:String = cat + "|" + q + "|" + (items != null ? items.length : 0) + "|" + (fav != null ? fav.count : 0);
        if (key != tiOrderKey) {
            tiOrderKey = key;
            tiTargetKey = null;
            var list:Array = [];
            var it:*;
            for each (it in items) {
                if (it != null && String(it.sES) == cat && it.sType != "Enhancement" && (q.length == 0 || String(it.sName).toLowerCase().indexOf(q) >= 0)) {
                    list.push(it);
                }
            }
            list.sort(tiGameCompare);
            if (fav != null && fav.count > 0) {
                var favs:Array = [];
                var rest:Array = [];
                for each (it in list) {
                    if (fav.has(it.ItemID)) {
                        favs.push(it);
                    } else {
                        rest.push(it);
                    }
                }
                list = favs.concat(rest);
            }
            tiOrder = list;
        }
        var tkey:String = key + "|" + tiSortIdx;
        if (tkey == tiTargetKey) {
            return;
        }
        tiTargetKey = tkey;
        var by:String = TI_SORTS[tiSortIdx][1];
        if (by == null) {
            tiTarget = tiOrder;
            return;
        }
        var ranked:Array = [];
        for (var i:int = 0; i < tiOrder.length; i++) {
            ranked.push({"it": tiOrder[i], "v": tiBoostValue(tiOrder[i].sMeta, by), "i": i});
        }
        ranked.sort(tiRankCompare);
        tiTarget = [];
        for each (var r:Object in ranked) {
            tiTarget.push(r.it);
        }
    }

    // Highest bonus first; ties (and items without it) keep the game's order.
    private static function tiRankCompare(a:Object, b:Object):int {
        if (a.v != b.v) {
            return a.v > b.v ? -1 : 1;
        }
        return a.i - b.i;
    }

    // The game's compareItems: type order, then name, level high first, id.
    private static function tiGameCompare(a:Object, b:Object):int {
        var ar:int = TI_TYPE_ORDER.indexOf(String(a.sType));
        var br:int = TI_TYPE_ORDER.indexOf(String(b.sType));
        if (ar < 0) {
            ar = 10000;
        }
        if (br < 0) {
            br = 10000;
        }
        if (ar != br) {
            return ar < br ? -1 : 1;
        }
        if (ar == 10000 && String(a.sType) != String(b.sType)) {
            return String(a.sType) < String(b.sType) ? -1 : 1;
        }
        var an:String = String(a.sName);
        var bn:String = String(b.sName);
        if (an != bn) {
            return an < bn ? -1 : 1;
        }
        if (int(a.iLvl) != int(b.iLvl)) {
            return int(a.iLvl) > int(b.iLvl) ? -1 : 1;
        }
        if (int(a.ItemID) != int(b.ItemID)) {
            return int(a.ItemID) < int(b.ItemID) ? -1 : 1;
        }
        return 0;
    }

    // An item's boost for a key ("dmgall", "undead", "gold", ...): the % it adds, -1 when it has none.
    private static function tiBoostValue(meta:*, by:String):Number {
        if (meta == null || meta is Number) {
            return -1;
        }
        for each (var part:String in String(meta).split(",")) {
            var kv:Array = part.split(":");
            if (kv.length >= 2 && String(kv[0]).toLowerCase() == by && !isNaN(Number(kv[1]))) {
                return Math.round((Number(kv[1]) - 1) * 100);
            }
        }
        return -1;
    }

    private function tiFindSearch(c:DisplayObjectContainer, depth:int):TextField {
        var i:int = 0;
        while (i < c.numChildren) {
            var d:DisplayObject = c.getChildAt(i);
            if (d is TextField && TextField(d).type == "input") {
                return d as TextField;
            }
            if (d is DisplayObjectContainer && depth < 3 && d.name != "mvsSortMenu") {
                var found:TextField = tiFindSearch(d as DisplayObjectContainer, depth + 1);
                if (found != null) {
                    return found;
                }
            }
            i++;
        }
        return null;
    }

    // "Sort: All dmg" beside the title's "MVS:" chip; a tap opens the list of sorts.
    private function tiSortChip(frame:DisplayObjectContainer, show:Boolean):void {
        var panel:DisplayObjectContainer = frame.parent;
        if (panel == null) {
            return;
        }
        var anchor:DisplayObject = null;
        var i:int = 0;
        while (i < panel.numChildren && anchor == null) {
            var c:DisplayObjectContainer = panel.getChildAt(i) as DisplayObjectContainer;
            if (c != null) {
                anchor = c.getChildByName("mvsFocus");
            }
            i++;
        }
        if (anchor == null) {
            return;
        }
        var holder:DisplayObjectContainer = anchor.parent;
        var chip:Sprite = holder.getChildByName("mvsSort") as Sprite;
        var menu:Sprite = panel.getChildByName("mvsSortMenu") as Sprite;
        if (!show) {
            if (chip != null) {
                chip.visible = false;
            }
            if (menu != null) {
                menu.visible = false;
            }
            return;
        }
        var label:String = "Sort: " + TI_SORTS[tiSortIdx][0];
        if (chip == null) {
            chip = new Sprite();
            chip.name = "mvsSort";
            chip.buttonMode = true;
            chip.mouseChildren = false;
            var tf:TextField = new TextField();
            tf.name = "t";
            tf.selectable = false;
            tf.mouseEnabled = false;
            tf.autoSize = "left";
            tf.defaultTextFormat = new TextFormat("Arial", 12, 0xFFFFFF, true);
            tf.x = 8;
            tf.y = 1;
            chip.addChild(tf);
            chip.addEventListener(MouseEvent.CLICK, tiSortChipTap, false, 0, true);
            holder.addChild(chip);
        }
        var t:TextField = chip.getChildByName("t") as TextField;
        if (t.text != label) {
            t.text = label;
            chip.graphics.clear();
            chip.graphics.lineStyle(1, 0x7C93B5, 1);
            chip.graphics.beginFill(0x10161F, 0.95);
            chip.graphics.drawRoundRect(0, 0, t.width + 16, 21, 10, 10);
            chip.graphics.endFill();
        }
        chip.x = anchor.x + anchor.width + 6;
        chip.y = anchor.y;
        chip.visible = true;
        if (menu != null && menu.visible) {
            tiPlaceMenu(menu, chip);
        }
    }

    // The menu sits on the inventory panel (above the list), just under the chip.
    private function tiPlaceMenu(menu:Sprite, chip:Sprite):void {
        var at:Point = menu.parent.globalToLocal(chip.parent.localToGlobal(new Point(chip.x, chip.y + 25)));
        menu.x = at.x;
        menu.y = at.y;
        menu.parent.setChildIndex(menu, menu.parent.numChildren - 1);
    }

    private function tiSortChipTap(e:MouseEvent):void {
        var chip:Sprite = e.currentTarget as Sprite;
        var holder:DisplayObjectContainer = chip.parent.parent;
        var menu:Sprite = holder.getChildByName("mvsSortMenu") as Sprite;
        if (menu == null) {
            menu = new Sprite();
            menu.name = "mvsSortMenu";
            var w:Number = 110;
            var h:Number = 26;
            menu.graphics.lineStyle(1, 0x7C93B5, 1);
            menu.graphics.beginFill(0x10161F, 0.97);
            menu.graphics.drawRoundRect(0, 0, w, TI_SORTS.length * h + 6, 10, 10);
            menu.graphics.endFill();
            for (var i:int = 0; i < TI_SORTS.length; i++) {
                var opt:Sprite = new Sprite();
                opt.name = String(i);
                opt.buttonMode = true;
                opt.mouseChildren = false;
                opt.graphics.beginFill(0xFFFFFF, 0.001);
                opt.graphics.drawRect(0, 0, w, h);
                opt.graphics.endFill();
                var tf:TextField = new TextField();
                tf.selectable = false;
                tf.mouseEnabled = false;
                tf.autoSize = "left";
                tf.defaultTextFormat = new TextFormat("Arial", 13, 0xFFFFFF, true);
                tf.text = TI_SORTS[i][0];
                tf.x = 10;
                tf.y = Math.round((h - tf.height) / 2);
                opt.addChild(tf);
                opt.y = 3 + i * h;
                opt.addEventListener(MouseEvent.CLICK, tiSortPick, false, 0, true);
                menu.addChild(opt);
            }
            holder.addChild(menu);
            menu.visible = false;
        }
        menu.visible = !menu.visible;
        tiPlaceMenu(menu, chip);
        e.stopPropagation();
    }

    private function tiSortPick(e:MouseEvent):void {
        var opt:Sprite = e.currentTarget as Sprite;
        tiSortIdx = int(opt.name);
        if (opt.parent != null) {
            opt.parent.visible = false;
        }
        tiTargetKey = null;
        e.stopPropagation();
    }

    // The inventory's Preview panel (its "tInfo" text: name, then type): for gear, a card with the
    // enhancement and the bonuses above the name, where the class stars go for classes.
    private function tiPreview(c:DisplayObjectContainer, depth:int):void {
        if (c == null) {
            return;
        }
        var root:DisplayObjectContainer = c;
        while (depth < 3 && root.parent != null && root.name != "mcInventory") {
            root = root.parent;
            depth++;
        }
        var tf:TextField = tiFindInfo(root, 0);
        if (tf != null) {
            tiPreviewCard(tf);
        }
    }

    private function tiFindInfo(c:DisplayObjectContainer, depth:int):TextField {
        var i:int = 0;
        while (i < c.numChildren) {
            var d:DisplayObject = c.getChildAt(i);
            if (d is TextField && d.name == "tInfo") {
                return d as TextField;
            }
            if (d is DisplayObjectContainer && depth < 5) {
                var found:TextField = tiFindInfo(d as DisplayObjectContainer, depth + 1);
                if (found != null) {
                    return found;
                }
            }
            i++;
        }
        return null;
    }

    private function tiPreviewCard(tf:TextField):void {
        var parent:DisplayObjectContainer = tf.parent;
        var card:Sprite = parent.getChildByName("mvsGearInfo") as Sprite;
        var item:* = parent.visible ? tiItemNamed(String(tf.text.split("\r")[0])) : null;
        var enh:String = item != null && TI_GEAR.indexOf(String(item.sES)) >= 0 ? tiEnhName(item) : null;
        var boosts:Array = item != null && TI_GEAR.indexOf(String(item.sES)) >= 0 ? tiBoosts(item.sMeta) : [];
        if (item == null || (enh == null && boosts.length == 0)) {
            if (card != null) {
                card.visible = false;
            }
            return;
        }
        if (card == null) {
            card = new Sprite();
            card.name = "mvsGearInfo";
            card.mouseEnabled = false;
            card.mouseChildren = false;
            parent.addChild(card);
        }
        if (tiChipsFor[card] !== item) {
            tiChipsFor[card] = item;
            while (card.numChildren > 0) {
                card.removeChildAt(0);
            }
            card.graphics.clear();
            var y:Number = 0;
            if (enh != null) {
                var label:TextField = new TextField();
                label.selectable = false;
                label.mouseEnabled = false;
                label.autoSize = "left";
                label.defaultTextFormat = new TextFormat(tf.getTextFormat(0).font, 13, tiEnhColor(enh), true);
                label.text = enh;
                card.addChild(label);
                y = label.height + 1;
            }
            if (boosts.length > 0) {
                var chips:Sprite = tiChips(card, null, item, boosts, tf.getTextFormat(0), true);
                chips.y = y + 10;
            }
        }
        card.x = tf.x + 2;
        card.y = tf.y - card.height - 2;
        card.visible = true;
    }

    // The player's item with this name (the Preview shows one item at a time).
    private function tiItemNamed(name:String):* {
        try {
            for each (var it:* in Main.instance.getGame().world.myAvatar.items) {
                if (it != null && it.sName == name) {
                    return it;
                }
            }
        } catch (e:Error) {
        }
        return null;
    }

    // The list's rows (LPFElementInvAQRow) sit two levels down, in the list area's row holder.
    private function tiRows(c:DisplayObjectContainer, depth:int):void {
        var i:int = 0;
        while (i < c.numChildren) {
            var d:DisplayObjectContainer = c.getChildAt(i) as DisplayObjectContainer;
            if (d != null && d.visible) {
                if (getQualifiedClassName(d) == "LPFElementInvAQRow") {
                    tiVisible.push(d);
                    tiRow(d);
                } else if (depth < 3) {
                    tiRows(d, depth + 1);
                }
            }
            i++;
        }
    }

    private function tiRow(row:DisplayObjectContainer):void {
        var item:* = null;
        var rowW:Number = 0;
        try {
            item = row["item"];
            rowW = Number(row["rowW"]);
        } catch (e:Error) {
        }
        if (item == null || item.sName == null || !(rowW > 0)) {
            return;
        }
        var name:String = String(item.sName);
        var rowH:Number = tiRowH();
        var y1:Number = Math.round(rowH * 0.3);
        var y2:Number = Math.round(rowH * 0.7);
        var nameTf:TextField = null;
        var lvlLabel:TextField = null;
        var lvlTf:TextField = null;
        var rankTf:TextField = row.getChildByName("mvsRank") as TextField;
        var i:int = 0;
        while (i < row.numChildren) {
            var tf:TextField = row.getChildAt(i) as TextField;
            if (tf != null && tf.visible && tf != rankTf) {
                var t:String = tf.text;
                if (t == "Lvl") {
                    lvlLabel = tf;
                } else if (t.length > 0 && t.length < 5 && !isNaN(Number(t)) && tf.x > rowW / 2) {
                    lvlTf = tf;
                } else if (nameTf == null && tiNameStart(t, name)) {
                    nameTf = tf;
                }
            }
            i++;
        }
        // "Lvl" over the level, in one column at the right edge (line 1 over line 2).
        var right:Number = rowW - 6;
        if (lvlTf != null) {
            var colW:Number = Math.max(lvlLabel != null ? lvlLabel.textWidth : 0, lvlTf.textWidth) + 4;
            var cx:Number = right - colW / 2;
            lvlTf.x = Math.round(cx - lvlTf.width / 2);
            lvlTf.y = Math.round(y2 - lvlTf.height / 2);
            if (lvlLabel != null) {
                lvlLabel.x = Math.round(cx - lvlLabel.width / 2);
                lvlLabel.y = Math.round(y1 - lvlLabel.height / 2);
            }
            right -= colW + 6;
        }
        if (nameTf == null) {
            return;
        }
        // Line 1 of a two-line row: the rank for armor and classes, the enhancement for gear.
        var label:String = null;
        var labelColor:int = -1;
        var boosts:Array = null;
        if (item.sES == "ar" && int(item.EnhID) > 0) {
            try {
                label = "Rank " + Main.instance.getGame().getRankFromPoints(int(item.iQty));
            } catch (e2:Error) {
            }
        } else if (TI_GEAR.indexOf(String(item.sES)) >= 0) {
            boosts = tiBoosts(item.sMeta);
            label = tiEnhName(item);
            labelColor = tiEnhColor(label);
            if (label == null && boosts.length > 0) {
                label = "";
            }
        }
        var badge:DisplayObject = row.getChildByName("mvsGrade");
        var chips:Sprite = row.getChildByName("mvsBoosts") as Sprite;
        if (label != null) {
            // Icon and rank or enhancement (and boosts) on line 1; the whole name on line 2; the class
            // stars in the middle, beside the level.
            if (rankTf == null) {
                rankTf = new TextField();
                rankTf.name = "mvsRank";
                rankTf.selectable = false;
                rankTf.mouseEnabled = false;
                rankTf.autoSize = "left";
                row.addChild(rankTf);
            }
            var fmt:TextFormat = nameTf.getTextFormat(0);
            fmt.color = 0x999999;
            // The game's text is "Name, Rank 10" (the name maybe cut short): the rank takes its look.
            var full:String = nameTf.text;
            var ri:int = full.lastIndexOf(", Rank");
            if (ri >= 0) {
                fmt = nameTf.getTextFormat(ri + 2);
            }
            if (labelColor >= 0) {
                fmt.color = labelColor;
            }
            rankTf.defaultTextFormat = fmt;
            if (ri >= 0) {
                nameTf.replaceText(0, nameTf.length, name);
            } else {
                tiFullName(nameTf, name);
            }
            rankTf.text = label;
            rankTf.visible = label.length > 0;
            var iconX:Number = tiLiftIcon(row, y1);
            rankTf.x = Math.round(iconX + 4);
            rankTf.y = Math.round(y1 - rankTf.height / 2);
            var lineRight:Number = right;
            if (badge != null && badge.visible) {
                badge.scaleX = badge.scaleY = TI_STARS_SCALE;
                var b:Rectangle = badge.getBounds(badge);
                badge.x = Math.round(right - b.right * TI_STARS_SCALE);
                badge.y = Math.round(rowH / 2);
                lineRight = badge.x + b.left * TI_STARS_SCALE - 6;
            }
            if (boosts != null && boosts.length > 0) {
                chips = tiChips(row, chips, item, boosts, nameTf.getTextFormat(0));
                chips.x = Math.round((rankTf.visible ? rankTf.x + rankTf.width : iconX) + 10);
                chips.y = y1;
                chips.scaleX = chips.scaleY = 1;
                var room:Number = lineRight - 4 - chips.x;
                if (chips.width > room && room > 20) {
                    chips.scaleX = chips.scaleY = room / chips.width;
                }
                chips.visible = true;
            } else if (chips != null) {
                chips.visible = false;
            }
            nameTf.wordWrap = false;
            nameTf.x = 8;
            nameTf.width = Math.max(40, lineRight - 8);
            nameTf.height = nameTf.textHeight + 4;
            nameTf.y = Math.round(y2 - nameTf.height / 2);
            return;
        }
        if (chips != null) {
            chips.visible = false;
        }
        if (rankTf != null) {
            rankTf.visible = false;
        }
        // Other items: the whole name, on two lines when it needs them (the row may have shown armor).
        tiLiftIcon(row, Math.round(rowH / 2));
        nameTf.x = TI_NAME_X;
        if (badge != null && badge.visible) {
            badge.scaleX = badge.scaleY = TI_STARS_SCALE;
            var b2:Rectangle = badge.getBounds(badge);
            badge.x = Math.round(right - b2.right * TI_STARS_SCALE);
            badge.y = Math.round(rowH / 2);
            right = badge.x + b2.left * TI_STARS_SCALE - 6;
        }
        tiFullName(nameTf, name);
        nameTf.wordWrap = true;
        nameTf.multiline = true;
        nameTf.width = Math.max(40, right - nameTf.x);
        nameTf.height = nameTf.textHeight + 4;
        nameTf.y = Math.round((rowH - nameTf.height) / 2);
    }

    // Each forge and special enhancement in its own colour (no yellow or gold: that reads as
    // member/legend-only in AQW); regular types (Lucky, Wizard, ...) share a quiet blue-grey.
    private static const TI_ENH_COLORS:Object = {
        "Valiance": 0x4FC3F7, "Ravenous": 0xEF5350, "Dauntless": 0xFF8A65, "Praxis": 0xBA68C8,
        "Arcana's Concerto": 0x9575CD, "Elysium": 0x4DD0E1, "Acheron": 0x7986CB, "Lacerate": 0xE57373,
        "Smite": 0xF06292, "Spiral Carve": 0x81C784, "Awe Blast": 0x64B5F6, "Health Vamp": 0xAED581,
        "Mana Vamp": 0x90CAF9, "Powerword DIE": 0xB0BEC5,
        "Lament": 0x4DB6AC, "Vainglory": 0xFF7043, "Penitence": 0x90A4AE, "Absolution": 0x9CCC65, "Avarice": 0xF48FB1,
        "Anima": 0xCE93D8, "Pneuma": 0x80DEEA, "Vim": 0xFF8A80, "Examen": 0x9FA8DA, "Grimskull": 0xA1887F,
        "Forge": 0xB39DDB
    };
    private static const TI_ENH_PLAIN:uint = 0x9EB3C8;
    // Bonus card borders by kind: damage, class points, gold, reputation, experience.
    private static const TI_CHIP_BORDER:Object = {dmg: 0xE57373, cp: 0x64B5F6, gold: 0xA1887F, rep: 0x81C784, exp: 0xBA68C8};

    private function tiEnhColor(name:String):uint {
        return name != null && TI_ENH_COLORS[name] != null ? uint(TI_ENH_COLORS[name]) : TI_ENH_PLAIN;
    }

    // Weapons, armor, helms and capes.
    private static const TI_GEAR:Array = ["Weapon", "co", "he", "ba"];
    // Weapon specials by ProcID (as the game's enhancement panel names them).
    private static const TI_PROCS:Object = {2: "Spiral Carve", 3: "Awe Blast", 4: "Health Vamp", 5: "Mana Vamp", 6: "Powerword DIE", 7: "Lacerate", 8: "Smite", 9: "Valiance", 10: "Arcana's Concerto", 11: "Acheron", 12: "Elysium", 13: "Praxis", 14: "Dauntless", 15: "Ravenous"};
    // Boosts in an item's sMeta ("dmgAll:1.1,undead:1.3,gold:1.25"), as the game's BoostIcons reads them.
    private static const TI_DMG:Object = {dmgall: "All", undead: "Undead", human: "Human", chaos: "Chaos", dragonkin: "Dragonkin", orc: "Orc", drakath: "Drakath", elemental: "Elemental"};
    private static const TI_BOOST_ART:Object = {dmg: "damageBoost", cp: "classBoost", gold: "goldBoost", rep: "repBoost", exp: "xpBoost"};
    private static const TI_BOOST_ORDER:Array = ["dmg", "cp", "gold", "rep", "exp"];
    private static const TI_BOOST_LABEL:Object = {cp: "Class", gold: "Gold", rep: "Rep", exp: "XP"};
    // The item each row's boost chips were drawn for.
    private static var tiChipsFor:Dictionary = new Dictionary(true);

    private function tiEnhPattern(item:*):* {
        try {
            if (item.EnhPatternID != null) {
                return Main.instance.getGame().world.enhPatternTree[item.EnhPatternID];
            }
        } catch (e:Error) {
        }
        return null;
    }

    private function tiEnhDis(item:*):Boolean {
        var enh:* = tiEnhPattern(item);
        return enh != null && enh.hasOwnProperty("DIS");
    }

    // "Ravenous", "Valiance", "Lament", "Lucky" ...: the weapon special, else the forge trait, else the
    // enhancement type; null when the item has none.
    private function tiEnhName(item:*):String {
        var enh:* = tiEnhPattern(item);
        if (enh == null) {
            return null;
        }
        var game:* = Main.instance.getGame();
        try {
            if (int(item.ProcID) > 0 && !enh.hasOwnProperty("DIS") && TI_PROCS[int(item.ProcID)] != null) {
                return TI_PROCS[int(item.ProcID)];
            }
            var shown:String = game.getDisplayEnhName(enh);
            if (enh.hasOwnProperty("DIS") && shown == "Forge") {
                return game.getDisplayEnhTraitName(enh);
            }
            return shown;
        } catch (e:Error) {
        }
        return enh.sName != null ? String(enh.sName) : null;
    }

    // [kind, text] per boost, in the game's order: ["dmg", "All +10%"], ["gold", "+25%"] ...
    private function tiBoosts(meta:*):Array {
        var out:Array = [];
        if (meta == null || meta is Number || String(meta).indexOf(":") < 0) {
            return out;
        }
        var found:Object = {};
        for each (var part:String in String(meta).split(",")) {
            var kv:Array = part.split(":");
            if (kv.length < 2 || isNaN(Number(kv[1]))) {
                continue;
            }
            var pct:String = "+" + Math.round((Number(kv[1]) - 1) * 100) + "%";
            var key:String = String(kv[0]).toLowerCase();
            if (TI_DMG[key] != null) {
                if (found.dmg == null) {
                    found.dmg = [];
                }
                found.dmg.push(TI_DMG[key] + " " + pct);
            } else if (TI_BOOST_ART[key] != null) {
                found[key] = [pct];
            }
        }
        for each (var kind:String in TI_BOOST_ORDER) {
            for each (var text:String in found[kind]) {
                out.push([kind, text]);
            }
        }
        return out;
    }

    // The game's boost icons with their values, drawn once per item shown in the row.
    private function tiChips(row:DisplayObjectContainer, chips:Sprite, item:*, boosts:Array, fmt:TextFormat, vertical:Boolean = false):Sprite {
        if (chips == null) {
            chips = new Sprite();
            chips.name = "mvsBoosts";
            chips.mouseEnabled = false;
            chips.mouseChildren = false;
            row.addChild(chips);
        }
        if (tiChipsFor[chips] === item) {
            return chips;
        }
        tiChipsFor[chips] = item;
        while (chips.numChildren > 0) {
            chips.removeChildAt(0);
        }
        var domain:* = Main.instance.getGame().loaderInfo.applicationDomain;
        var x:Number = 0;
        var f:TextFormat = new TextFormat(fmt.font, 11, 0xF2F2F2, true);
        for each (var b:Array in boosts) {
            var card:Sprite = new Sprite();
            chips.addChild(card);
            var cx:Number = 4;
            var drawn:Boolean = false;
            try {
                var art:Class = domain.getDefinition(TI_BOOST_ART[b[0]]) as Class;
                var icon:DisplayObject = new art() as DisplayObject;
                card.addChild(icon);
                icon.width = icon.height = 15;
                var r:Rectangle = icon.getBounds(card);
                icon.x += cx - r.left;
                icon.y += -7.5 - r.top;
                cx += 17;
                drawn = true;
            } catch (e:Error) {
            }
            var tf:TextField = new TextField();
            tf.selectable = false;
            tf.mouseEnabled = false;
            tf.autoSize = "left";
            tf.defaultTextFormat = f;
            tf.text = drawn || b[0] == "dmg" ? b[1] : TI_BOOST_LABEL[b[0]] + " " + b[1];
            tf.x = cx;
            tf.y = Math.round(-tf.height / 2);
            card.addChild(tf);
            var w:Number = cx + tf.width + 3;
            card.graphics.lineStyle(1, uint(TI_CHIP_BORDER[b[0]]), 1);
            card.graphics.beginFill(0x0E0B0A, 0.88);
            card.graphics.drawRoundRect(0, -10, w, 20, 10, 10);
            card.graphics.endFill();
            if (vertical) {
                card.y = chips.numChildren * 22 - 22;
            } else {
                card.x = x;
                x += w + 5;
            }
        }
        return chips;
    }

    // Moves the row's icon (and its equipped ring) so it is centred on line 1; returns its right edge.
    private function tiLiftIcon(row:DisplayObjectContainer, y1:Number):Number {
        var nameX:Number = TI_NAME_X;
        var right:Number = nameX - 4;
        var i:int = 0;
        while (i < row.numChildren) {
            var d:DisplayObject = row.getChildAt(i);
            if (!(d is TextField) && d.visible && d.name != "mvsGrade" && d.name != "mvsRank" && d.name != "mvsBoosts") {
                var r:Rectangle = d.getBounds(row);
                // The icon and its ring sit left of the name; the row background spans the row.
                if (r.width > 0 && r.right <= nameX + 2 && r.width < nameX + 4) {
                    d.y += Math.round(y1 - (r.top + r.height / 2));
                    if (r.right > right) {
                        right = r.right;
                    }
                }
            }
            i++;
        }
        return right;
    }

    // The name field shows the item's name, maybe cut short ("Bioluminessen... x1").
    private function tiNameStart(shown:String, name:String):Boolean {
        if (shown.indexOf(name) == 0) {
            return true;
        }
        var cut:int = shown.indexOf("...");
        if (cut < 0) {
            cut = shown.indexOf("…");
        }
        return cut >= 3 && name.indexOf(shown.substr(0, cut)) == 0;
    }

    // Puts the whole name back in place of the cut-short part, keeping the field's formatting.
    private function tiFullName(tf:TextField, name:String):void {
        var shown:String = tf.text;
        if (shown.indexOf(name) == 0) {
            return;
        }
        var cut:int = shown.indexOf("...");
        var len:int = 3;
        if (cut < 0) {
            cut = shown.indexOf("…");
            len = 1;
        }
        if (cut >= 3) {
            tf.replaceText(0, cut + len, name);
        }
    }

    // The game's row height (MVS may have replaced it for touch screens).
    private function tiRowH():Number {
        try {
            var c:* = Main.instance.getGame().loaderInfo.applicationDomain.getDefinition("LPFFrameInvAQ");
            if (c.ROW_H > 0) {
                return c.ROW_H;
            }
        } catch (e:Error) {
        }
        return 30;
    }

    private function tiContentH(s:Sprite):Number {
        var h:Number = 0;
        var i:int = 0;
        while (i < s.numChildren) {
            var d:DisplayObject = s.getChildAt(i);
            if (d.visible && d.height > h) {
                h = d.height;
            }
            i++;
        }
        return h + 4;
    }

    private function tiScroll(s:Sprite, by:Number):void {
        var r:Rectangle = s.scrollRect;
        if (r == null) {
            return;
        }
        var max:Number = Math.max(0, tiContentH(s) - r.height);
        var y:Number = Math.max(0, Math.min(max, r.y + by));
        if (y != r.y) {
            r.y = y;
            s.scrollRect = r;
        }
    }

    private function tiWheel(e:MouseEvent):void {
        tiScroll(e.currentTarget as Sprite, -e.delta * 10 / TI_SCALE);
    }
}
}
