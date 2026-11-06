package skua.module {
import flash.display.DisplayObject;
import flash.display.DisplayObjectContainer;
import flash.events.MouseEvent;

import skua.Main;

/**
 * Big menus for touch screens. When the game opens one of its small click menus (ui.cMenu: your
 * portrait's Char Page / Reputation / Leave Party / Disband Party / Close, or another player's), this
 * reports its entries to the page ("mvsMenu", JSON of {i, t, on}), which shows them as large buttons.
 * A tap there replays the click on the game's own entry (mvsMenuClick), and a tap beside it closes the
 * game's menu (mvsMenuClose), so the game does exactly what its menu does. Disabled entries are the ones
 * without a click listener.
 *
 * The bottom bar's Char and Quests buttons open a different small list (mcMenu.menu, an interfaceMenu
 * stacked upward from the button, its title on top); that one is reported the same way as
 * {"title": ..., "items": [...]}, top entry first.
 *
 * In bin/skua.swf (patched with FFDec, see ClassRatingsUI.as) this code lives as static members of
 * Modules, and Externalizer registers Modules.menuClick / Modules.menuClose.
 */
public class GameMenusUI extends Module {
    private static var _menuKey:String = "";

    public function GameMenusUI() {
        super("GameMenusUI");
        enabled = true;
    }

    private static function cMenuOpen(game:*):Boolean {
        var menu:* = game.ui.cMenu;
        return menu != null && menu.visible && menu.alpha >= 0.5;
    }

    /** The open Char / Quests list of the bottom bar, or null. */
    private static function barList(game:*):DisplayObjectContainer {
        try {
            var bar:* = game.ui.mcInterface.mcMenu;
            if (bar == null || bar.menu == null) return null;
            var list:DisplayObjectContainer = bar.menu.mcMenu as DisplayObjectContainer;
            return list != null && list.stage != null ? list : null;
        } catch (e:Error) {
        }
        return null;
    }

    private static function barMenuKey(game:*):String {
        var list:DisplayObjectContainer = barList(game);
        if (list == null) return "";
        var items:Array = [];
        var title:String = "";
        for (var i:int = 0; i < list.numChildren; i++) {
            var child:* = list.getChildAt(i);
            var label:* = null;
            try {
                label = child.mTxt;
            } catch (e:Error) {
            }
            if (label != null) {
                items.unshift({"i": i, "t": String(label.text), "on": child.hasEventListener(MouseEvent.CLICK)});
                continue;
            }
            try {
                if (child.txt != null) title = String(child.txt.text);
            } catch (e2:Error) {
            }
        }
        if (items.length == 0) return "";
        return JSON.stringify({"title": title, "items": items});
    }

    private static function menuItems(game:*):Array {
        var items:Array = [];
        var menu:* = game.ui.cMenu;
        if (!cMenuOpen(game)) return items;
        var cnt:DisplayObjectContainer = menu.getChildByName("cnt") as DisplayObjectContainer;
        if (cnt == null) return items;
        for (var i:int = 0; i < 40; i++) {
            var item:DisplayObjectContainer = cnt.getChildByName("i" + i) as DisplayObjectContainer;
            if (item == null) break;
            if (!item.visible) continue;
            var label:String = "";
            var ti:* = item.getChildByName("ti");
            if (ti != null) label = String(ti.text);
            items.push({"i": i, "t": label, "on": item.hasEventListener(MouseEvent.CLICK)});
        }
        return items;
    }

    override public function onFrame(game:*):void {
        var key:String = "";
        try {
            var items:Array = menuItems(game);
            if (items.length > 0) key = JSON.stringify(items);
            else key = barMenuKey(game);
        } catch (e:Error) {
        }
        if (key == _menuKey) return;
        _menuKey = key;
        try {
            Main.instance.external.call("mvsMenu", key);
        } catch (e2:Error) {
        }
    }

    public static function menuClick(index:int):void {
        try {
            var game:* = Main.instance.getGame();
            if (!cMenuOpen(game)) {
                var list:DisplayObjectContainer = barList(game);
                if (list != null && index >= 0 && index < list.numChildren) {
                    var entry:DisplayObject = list.getChildAt(index);
                    if (entry.hasEventListener(MouseEvent.CLICK)) {
                        entry.dispatchEvent(new MouseEvent(MouseEvent.CLICK, true, false, 1, 1));
                    }
                }
                return;
            }
            var cnt:DisplayObjectContainer = Main.instance.getGame().ui.cMenu.getChildByName("cnt") as DisplayObjectContainer;
            var item:DisplayObject = cnt.getChildByName("i" + index);
            if (item != null && item.hasEventListener(MouseEvent.CLICK)) {
                item.dispatchEvent(new MouseEvent(MouseEvent.CLICK, true, false, 1, 1));
            }
        } catch (e:Error) {
        }
    }

    /** Taps one of the game's bottom-bar buttons (btnRest, btnChar, btnQuest, btnMap, btnHouse, btnBook,
     *  btnOption, btnMenu, btnBag) for the app's side rails, the same way a finger tap does; "temp" opens
     *  the Temp Inventory. */
    public static function gameButton(name:String):void {
        try {
            // Temp Inventory has no bar button; the bar's own toggle opens and closes it.
            if (name == "temp") {
                Main.instance.getGame().ui.mcInterface.mcMenu.toggleTempInventory();
                return;
            }
            var menu:DisplayObjectContainer = Main.instance.getGame().ui.mcInterface.mcMenu as DisplayObjectContainer;
            var btn:DisplayObject = menu.getChildByName(name);
            if (btn != null && btn.hasEventListener(MouseEvent.CLICK)) {
                btn.dispatchEvent(new MouseEvent(MouseEvent.CLICK, true, false, 1, 1));
            }
        } catch (e:Error) {
        }
    }

    /** Closes the game's menu the way it closes itself: the pointer leaving it. */
    public static function menuClose():void {
        try {
            var game:* = Main.instance.getGame();
            var menu:* = game.ui.cMenu;
            if (menu != null && menu.visible) menu.dispatchEvent(new MouseEvent(MouseEvent.MOUSE_OUT, true, false));
            else if (barList(game) != null) game.ui.mcInterface.mcMenu.handleMenu(null);
        } catch (e:Error) {
        }
    }
}
}
