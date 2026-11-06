# MoVibeSkua port status

What from [VibeSkua](https://github.com/NinjaXz/VibeSkua) runs in MoVibeSkua (MVS), and how far each part has been tested.
Status as of 31 Oct 2026, for the build installed on the test phone that day.

**Ported:** ✅ Yes · 🟡 Partly · ⏳ Not yet · 🚫 Removed (PC-only)

**Tested:**
- 📱 **Phone:** used on a real phone while logged in to AQW, and it worked.
- 🖥️ **Emulator:** checked on the Android emulator. AQW can't be logged in there, so anything that needs a character was checked as far as the login screen or against stand-in data.
- 👀 **Opens:** the page opens and fits the screen. Nothing in it has been tried against a logged-in game.
- ⬜ **Not yet:** not tested.

There are no automated tests for the Android app yet; every check above was done by hand.

## Game client

| Function | Ported | Tested | Description | Solution |
|---|---|---|---|---|
| AQW game client | ✅ Yes | 📱 Phone | Plays AQW inside the app. | Windows' Flash Player is replaced by Ruffle, a Flash emulator, inside an Android WebView. `RuffleHost` relays the game's connection to AQW's servers and fetches its files. |
| Link between the bot and the game | ✅ Yes | 📱 Phone | How Skua reads and controls the game. | `RuffleFlashUtil` offers the same interface as the Windows version and talks to `bridge.js` in the game page over a local WebSocket, so the bot core is unchanged. |
| Typing in the game | ✅ Yes | 📱 Phone | Login box, chat, any game text field. | Touches on the game no longer take focus away from it, and Backspace/Enter reach the game first. |
| Sharp graphics | ✅ Yes | 📱 Phone | The game at the phone's full resolution. | Renders at the phone's native pixel ratio with quality "high". Menu → Game → Graphics & Performance changes it (below). |
| In-game Options and quality switch, skill cooldown sweep, drop list, bank/shop/inventory rows | ✅ Yes | 🖥️ Emulator | AQW panels that draw with `BitmapData.draw`. | Stock Ruffle can't do that draw, and each failure aborted AQW's code. Ruffle is rebuilt with draw support (`tools/ruffle-patches`), and the LOW/MEDIUM/HIGH switch now changes anti-aliasing. 5 of 7 of Ruffle's own draw tests match Flash. Not yet confirmed on the phone. |
| Bot keeps running in the background | ✅ Yes | 🖥️ Emulator | Screen off or app in the background. | A foreground service with a notification holds wake and Wi-Fi locks. The notification is checked; a long screen-off run isn't. |
| Recovering from a crashed game page | ⏳ Not yet | ⬜ Not yet | If the in-app browser restarts, the game should reload. | Known bug: it stays on "Loading game client...". Force-close and reopen the app. |

## Scripts

| Function | Ported | Tested | Description | Solution |
|---|---|---|---|---|
| Script Loader (load, start, stop) | ✅ Yes | 📱 Phone | Runs community bot scripts, including CoreBots. | Scripts are compiled on the phone with Roslyn against reference assemblies packed in the APK. `Skua.Compat.WinForms` covers the WinForms classes scripts use, and a shim passes CoreBots' Windows check. |
| Start / Stop button | ✅ Yes | 📱 Phone | One tap to run or stop the loaded script. | Green/red button on the left rail (portrait: top bar). |
| Script questions and options (e.g. Data Collection) | ✅ Yes | 🖥️ Emulator | Pop-ups a script shows while it runs. | Dialogs fit the screen and scroll. Answering one no longer makes Android close the app as frozen (fixed in `MainActivity`). |
| Script Repo | ✅ Yes | 🖥️ Emulator | Browse, download and update community scripts. | VibeSkua's own page, rearranged for narrow screens; the list loads when opened. |
| Edit Script / Open VSCode | ✅ Yes | 🖥️ Emulator | Edit a script's code. | No VS Code on phones: the script opens in any installed code editor (e.g. Acode) and saving there updates it. |
| Scheduler | ✅ Yes | 👀 Opens | Run scripts on a schedule. | VibeSkua's page, unchanged. |
| Loadouts Manager | ✅ Yes | 👀 Opens | Save and swap gear sets. | VibeSkua's page, unchanged. |
| CoreBots options | ✅ Yes | 👀 Opens | Settings shared by CoreBots scripts. | VibeSkua's page, unchanged. |
| Logs | ✅ Yes | 🖥️ Emulator | Script, Flash and debug logs. | VibeSkua's page; lines are also copied to Android's system log for debugging. A log page opened after a script started was empty (its tab only collected new lines); it now loads what was logged before. |
| Script window over the game | ✅ Yes | 🖥️ Emulator | Small window while a script runs: name, running time, status, map and cell, latest log line. | Drawn by the game page (overlays.js; app controls can't cover the game WebView) from ScriptStatusOverlay.cs once a second. Drag it by its header, tap the header or – to collapse it to a bar, × hides it until the next script. A finished run stays for 30 s with its run time. |
| Big game menus | ✅ Yes | 🖥️ Emulator | The game's small click menus (your portrait: Char Page, Reputation, Leave Party, Disband Party, Close; other players) as large buttons. | skua.swf reports ui.cMenu's entries (GameMenusUI.as, patched into Modules with FFDec) and replays the tap on the game's own entry, so the game does what its menu does. Greyed entries are the ones the game disabled. The bottom bar's Char and Quests lists (mcMenu.menu) show the same way, with their own title. A tap beside the panel closes the game's menu. Not yet tried on a logged-in game. |
| Game buttons on the rails | ✅ Yes | 🖥️ Emulator | The side rails show the game's own buttons by default: Quests, Temp Inventory, Map, Lore, Menu, House, Settings on the left; Bag, Bank, Rest, Char and Guide on the right. Two quick taps on the MVS crest switch to the bot tools and back; the choice is remembered. | Each button taps the game's own button inside the game (skua.swf mvsGameButton; Temp Inventory uses the bar's toggleTempInventory, Bank the bot's Bank.Open). One tap on the crest opens Support MVS (it waits 0.4 s for a second tap). Portrait: one bottom bar with all the buttons and Guide, and a small crest in the top bar. Leaving bot mode closes the bot's pages. |
| How-to guide, bot guide and support | ✅ Yes | 🖥️ Emulator | Guide: the screen, the crest, in-game features. Bot tools guide: a 4-step quick start, then every tool by what you want to do, in plain words, with a glossary. Support MVS: a note from Arglax, MVS and AUQW Discords, Buy Me a Coffee, credits (Skua team from the Skua donations page, VibeSkua, ArrowDev, Ruffle, the bosses-guide sheet). | GuideView, BotGuideView, SupportView; the bot guide is also in the bot Menu. Telemetry counts them as "other" screens, so no rules change. An InstaPay card is ready but hidden until its details are filled in (SupportViewModel). |
| One Streamer Mode | ✅ Yes | ⏳ Not yet | The Streamer button and Skua's Options > Streamer Mode are one switch: stand-in name "MVS User" and guild "MVS" on your name tag and portrait, other players, gold, coins and chat hidden, room name tidied. | ShellViewModel keeps IScriptOption.StreamerMode and its own flag in step both ways. The page draws the name tag and portrait (keeping the AFK prefix and guild brackets); ScriptOption.NameTagsDrawnByHost stops Skua's loop from writing them too, so they don't flicker. |
| Inventory sorted by stars | ✅ Yes | ⏳ Not yet | With the inventory chip on a use case (Farming, Solo, Bossing, Ultra, PvP) the class list reads best first by its stars, favourites still on top; A-Z is the game's own order. | The game keeps its list order private, so ClassRatingsUI models it (classes matching the search; name, level, id; favourites first), checks it against every row the game bound itself, and only then rebinds the visible rows in star order. A filter it doesn't model (equipped, favourites only) leaves the list as the game drew it. |

## Combat

| Function | Ported | Tested | Description | Solution |
|---|---|---|---|---|
| Auto Attack / Auto Hunt | ✅ Yes | 📱 Phone | Fights or hunts monsters automatically. | Checked on the phone (icestormarena, private room): Auto Attack kills the cell's monsters, and Stop leaves combat. Hunt goes after the monsters of the cell it started in (or Manual MapIDs), moving to whichever cell has them alive through Skua's pad-corrected jump (checked: r4 has no Left pad, so it lands on Right). Each new target is logged as name, MapID, MonID and cell, shown on the page, and saved to monster-ids.json. If attacks keep failing against live monsters in the cell (standing "flat" on an invalid pad), it re-enters the cell at a valid pad. "Monsters in this map" lists every monster with its IDs; tapping one targets only it. |
| Smart skill rotations | ✅ Yes | ⬜ Not yet | Premade per-class rotations with conditions, e.g. "use the heal when HP is below 70%". | `AdvancedSkills.json` is the community's current file (268 classes, from auqw/Scripts) packed in the APK; updates keep your own edits and replace rotations you never changed. Auto uses the chosen class, or the equipped one, with all rule types: HP, mana, auras, party HP and waits. A heal whose HP rule is met now goes before the rest of the rotation (checked every 0.3 s). Not yet watched in a real fight. |
| Select Class / Select Mode (Auto page) | ✅ Yes | 📱 Phone | Pick which class and rotation Auto uses. | The list was empty on the phone: Ruffle's `JSON.stringify` wrote big whole numbers as `1354390967.0` and Skua refused the whole inventory. Fixed in Ruffle (`tools/ruffle-patches/json-whole-numbers.patch`). The list has a search box (no longer opens the keyboard by itself), stars to pin classes to the top (auto-pinned-classes.json), and use-case ranking: A–Z, Farming, Solo, Bossing, Ultra and PvP, each rated 0–5 stars like the r/AQW Simplified Bosses Guide sheet, with a one-line description, plus every use case's stars and the class's ultra role under each class (Skua.App.Avalonia/Assets/class-ratings.json: Ultra from that sheet's per-fight stars for its 42 classes, and on Ultra a picker ranks by one of its 22 fights; PvE anchored to the AQWG ultra-boss guide and AQWHub; farm/solo/boss from the aqwxcommunity class-setups tags, else estimated; most PvP ratings are estimates). Picking a use case also picks the class's matching mode (Farm, Solo, Atk, Supp…), and a line under the pickers says when to use each mode. Starting Auto Attack or Auto Hunt first picks up the class equipped in the game. The monster list refreshes by itself on each map join. The game's own inventory shows the same grades, drawn locally by skua.swf (nothing sent to the server): stars beside each class (drawn as shapes; the game fonts have no star glyph), Farm/Solo/Boss/Ultra/PvP stars and the ultra role in the Preview panel, and an "MVS: <use case>" chip on the list header that cycles the use case for both the inventory and the Auto page. skua.swf was patched with FFDec (no mxmlc here), see ClassRatingsUI.as. White names, yellow for member-only. Opening it used to black out the game: the shell hid the game for any open popup; now only for one over the game. |
| Skills editor | ✅ Yes | 👀 Opens | Write your own rotations and conditions. | VibeSkua's page (Menu → Combat → Skills). |
| Stats, Runtime helpers | ✅ Yes | 🖥️ Emulator | Kill and death counts; the drop pick-up list, registered quests and boosts. | VibeSkua's pages. On a narrow page Runtime and Skills now stack their columns and scroll instead of overlapping (`ResponsiveGrid`). |

## Tools and helpers

| Function | Ported | Tested | Description | Solution |
|---|---|---|---|---|
| Jump (cell and pad) | ✅ Yes | 👀 Opens | Move to a cell or pad. | MVS page combining VibeSkua's Jump with saved spots. |
| Saved spots / Fast Travel | ✅ Yes | 🖥️ Emulator | Tap Jump to do `/join map-room`; X deletes a spot. | One list shared by Jump and Menu → Fast Travel. Adding and deleting spots checked; an actual join not yet. |
| Loader | ✅ Yes | 👀 Opens | Load shops and quests by ID. | Layout fixed for phones. Loading isn't tested, by request. |
| Grabber | ✅ Yes | 👀 Opens | Read shop, quest, inventory and drop data. | Phone layout: category dropdown, details below the list. Grabbing from a live game not tried. |
| Current Drops, Notify Drop, Junk Items | ✅ Yes | 👀 Opens | Drop list, drop alerts, junk filter. | VibeSkua's pages. The alert beep uses Android's tone player. |
| Bank | ✅ Yes | ⬜ Not yet | Opens the in-game bank (Menu → Game → Open Bank). | Depends on the Ruffle draw fix above. |

## Options

| Function | Ported | Tested | Description | Solution |
|---|---|---|---|---|
| Game options | ✅ Yes | 📱 Phone | Skua's in-game toggles. | VibeSkua's page. |
| Application options | ✅ Yes | 👀 Opens | App settings. | VibeSkua's page minus 5 entries that do nothing on Android (below). |
| Application Themes | ✅ Yes | 👀 Opens | Colours, dark mode, accent. | VibeSkua's page minus custom backgrounds. |
| HotKeys | 🚫 Removed | — | Keyboard shortcuts and army hotkeys. | They hook the PC window and control other PC windows. |
| Custom backgrounds, title-bar accent | 🚫 Removed | — | Background art and title-bar colour. | Nothing on Android draws them; the folders open Windows Explorer. |
| Show Username in Title, Client Animation Frame-rate, Clear Flash Cache, Junk Items update toggles | 🚫 Removed | — | Desktop window and Flash Player settings. | No window title, Flash cache or matching feature on Android. |

## Diagnostics

| Function | Ported | Tested | Description | Solution |
|---|---|---|---|---|
| Console | ✅ Yes | 👀 Opens | Run C# snippets against the bot. | VibeSkua's page; compiles with the same on-phone Roslyn. |
| Packet Spammer, Packet Logger | ✅ Yes | 👀 Opens | Send and watch game packets. | VibeSkua's pages. Need a logged-in game to show anything. |
| Packet Interceptor | 🚫 Removed | — | Edits packets through a local proxy. | The in-app game only connects to AQW's servers, so it can't go through the proxy. |

## Accounts and multiple clients (VibeSkua's Manager app)

| Function | Ported | Tested | Description | Solution |
|---|---|---|---|---|
| Login with relogin | ✅ Yes | ⬜ Not yet | Log in to a server; log back in after a disconnect or kick. | MVS Login page; remembers one account per client. Your first try got stuck, so it now shows each step and has a Cancel button. Not tried since; logging in through the game's own box works. |
| Army (several clients) | ✅ Yes | 🖥️ Emulator | Run several accounts at once, like the Manager's launcher. | Each client is its own Android process with its own game and login, up to 12. Hidden clients keep playing but draw nothing (Ruffle patch). Three clients ran on the emulator; a real army script not yet. |
| Account manager (saved accounts, groups, tags) | ⏳ Not yet | — | Pick from saved accounts when launching. | Not built; log in on each client yourself. |
| App updater | ⏳ Not yet | — | Updates the app from inside it. | Updates are APKs from the GitHub releases page. An in-app updater would need Android's install permission. |
| Script updater | ✅ Yes | 👀 Opens | Update every downloaded script. | Script Repo's Update All / Download All. |

## Plugins

| Function | Ported | Tested | Description | Solution |
|---|---|---|---|---|
| Plugin manager | ✅ Yes | 👀 Opens | Load and unload plugins. | VibeSkua's page (Menu → Plugins → Manage Plugins). |
| Bundled plugins (Daily Tracker, ButlerV5, Cosmetics) | ✅ Yes | 🖥️ Emulator | The plugins that ship with VibeSkua on PC, under Menu → Plugins. | Packed in the APK and loaded on start. Android can't create their windows, so they now open through `PluginWindow`: a window on PC and a page in MVS (one build of each plugin runs on both). A page shorter than the window was scrolls instead of squeezing. All three pages open; Cosmetics downloads its 67,000-item catalog (fixed: it read the download on the UI thread, which Android refuses). Nothing tried with a character yet. |

## New in MVS

| Function | Tested | Description |
|---|---|---|
| Phone layout | 📱 Phone | Tool rails on both edges in landscape, bars in portrait; pages slide over the game and can be dragged wider. Starts in landscape. The last 12 pages stay built, so reopening one is instant (Options: 1 s before, 5 ms now on the emulator). |
| Streamer Mode (eye button) | 🖥️ Emulator | Shows "MVS User" and guild "MoVibeSkua" in the status line and in game. Checked against a stand-in character. |
| Army lag killer | 🖥️ Emulator | Hidden army clients skip drawing and are muted. |
| Graphics & Performance (Menu → Game) | 🖥️ Emulator | Presets (Battery, Balanced, Quality, Ultra); resolution from 1x up to 20% above the screen; anti-aliasing off to 8x; colour styles (Vivid, Cinematic, Bright); an FPS meter; shortcuts to Lag killer, Hide/Optimize players and Disable skill effects. Saved and re-applied on start and after login. AQW runs at 30 fps by design, so the meter's goal is a steady 30. |
| Shorter menu | 🖥️ Emulator | Pages already on the rails are gone from the menu; the Account group became Game (Open Bank, Graphics & Performance). |
| AQW look and MVS icon | 📱 Phone | AQW-styled rails and the crest icon. |
| Usage stats | 🖥️ Emulator | Anonymous counts to the `movibeskua` Firebase project ([details](telemetry/USAGE.md)). |

## Needs a check on the phone

These are ported but only a logged-in phone can confirm them:

1. In-game Options and the quality switch, skill cooldowns, drops, bank, shop and inventory (the Ruffle draw fix).
2. A fight with a class that has a premade rotation, to see the conditions fire.
3. Select Class / Select Mode on the Auto page now list your classes, and Auto Attack with no target picked fights the monsters in your cell.
4. SevenCircles War XP + Gold, or another CoreBots script, run to the end.
5. A relogin after a disconnect.
6. Army with two real accounts and an army script.
7. The plugins with a character: Daily Tracker's quest list, a Cosmetics outfit, ButlerV5 following between two accounts.
8. Auto Hunt moves between cells without standing in mid-air, and stopping Auto leaves combat.
9. Game FPS in a busy fight with the Quality and Ultra presets.
