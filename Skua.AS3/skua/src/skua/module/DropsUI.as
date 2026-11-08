package skua.module {
import skua.Main;

/**
 * MVS Drops: drops that need accepting are kept in the game's own drop store (its "Custom Drops UI",
 * game.cDropsUI) instead of popping up over the screen, and the app shows them in a touch-sized panel
 * (overlays.js). The list goes to the page as "mvsDrops" (JSON of {id, n, q, t, ac, mem, r}) whenever it
 * changes; mvsDropAccept / mvsDropDecline act on one item the way the game's own Yes / No buttons do
 * (requestDrop checks bag space and asks the server; the server's reply removes it from the store).
 * The page switches the feature on with mvsDropsOn(true); the game's small drops icon by the portrait is
 * hidden while it is on, since the app's Drops button replaces it.
 *
 * In bin/skua.swf (patched with FFDec, see ClassRatingsUI.as) this code lives as static members of
 * Modules, like the other MVS modules.
 */
public class DropsUI extends Module {
    private static var _dropsOn:Boolean = false;
    private static var _dropsKey:String = "";

    public function DropsUI() {
        super("DropsUI");
        enabled = true;
    }

    private static function dropStore(game:*):* {
        if (!_dropsOn || game == null || game.litePreference == null) return null;
        if (!game.litePreference.data.bCustomDrops) {
            game.litePreference.data.bCustomDrops = true;
            game.litePreference.flush();
        }
        if (game.cDropsUI == null) {
            var cls:Class = game.loaderInfo.applicationDomain.getDefinition("liteAssets.draw.customDrops") as Class;
            game.cDropsUI = new cls(game);
        }
        try {
            if (game.ui.mcPortrait.iconDrops.visible) game.ui.mcPortrait.iconDrops.visible = false;
        } catch (e:Error) {
        }
        return game.cDropsUI;
    }

    private static function findDrop(store:*, id:int):* {
        for each (var item:* in store.invTree) {
            if (item != null && int(item.ItemID) == id) return item;
        }
        return null;
    }

    override public function onFrame(game:*):void {
        var store:* = dropStore(game);
        if (store == null) return;
        var list:Array = [];
        for each (var item:* in store.invTree) {
            if (item == null) continue;
            var qty:* = store.itemCount[item.dID];
            list.push({
                "id": int(item.ItemID),
                "n": String(item.sName),
                "q": qty == null ? int(item.dQty) : int(qty),
                "t": String(item.sType),
                "ac": int(item.bCoins) == 1,
                "mem": int(item.bUpg) == 1,
                "r": int(item.iRty),
                "i": String(item.sIcon)
            });
        }
        var key:String = JSON.stringify(list);
        if (key == _dropsKey) return;
        _dropsKey = key;
        try {
            Main.instance.external.call("mvsDrops", key);
        } catch (e:Error) {
        }
    }

    public static function dropsOn(on:Boolean):void {
        _dropsOn = on;
        _dropsKey = "";
        if (on) return;
        try {
            var game:* = Main.instance.getGame();
            if (game.cDropsUI != null) game.ui.mcPortrait.iconDrops.visible = true;
        } catch (e:Error) {
        }
    }

    /** "ok" when asked for, "full" when the bag (or house) has no room, "" when it is not waiting. */
    public static function dropAccept(id:int):String {
        try {
            var store:* = dropStore(Main.instance.getGame());
            var item:* = store == null ? null : findDrop(store, id);
            if (item == null) return "";
            return store.requestDrop(item, true) ? "ok" : "full";
        } catch (e:Error) {
        }
        return "";
    }

    /** The game's own preview of a waiting drop (what its drop card's Preview button opens). */
    public static function dropPreview(id:int):void {
        try {
            var store:* = dropStore(Main.instance.getGame());
            var item:* = store == null ? null : findDrop(store, id);
            if (item != null) store.previewDrop(item);
        } catch (e:Error) {
        }
    }

    public static function dropDecline(id:int):void {
        try {
            var store:* = dropStore(Main.instance.getGame());
            var item:* = store == null ? null : findDrop(store, id);
            if (item != null) store.onBtNo(item);
        } catch (e:Error) {
        }
    }
}
}
