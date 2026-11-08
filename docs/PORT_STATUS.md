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
| Game buttons on the rails | ✅ Yes | 📱 Phone | The side rails show the game's own buttons by default: Quests, Temp Inventory, Map, Lore, Menu, House, Settings on the left; Bag, Bank, Rest, Char and Guide on the right. Two quick taps on the MVS crest switch to the bot tools and back; the choice is remembered. | Each button taps the game's own button inside the game (skua.swf mvsGameButton; Temp Inventory uses the bar's toggleTempInventory, Bank the bot's Bank.Open). One tap on the crest opens Support MVS (it waits 0.4 s for a second tap). Portrait: one bottom bar with all the buttons and Guide, and a small crest in the top bar. Leaving bot mode closes the bot's pages. **Measured 2026-11-06:** Map, Lore, Settings, Bag, Temp Inv, Menu open the game's window (popup label checked) and a second tap closes it; Rest switches the avatar to its Rest animation; Quests and Char open their lists as big menus (2 and 8 entries), and a big-menu entry opens the game's window (Reputations → Faction). Bank opened but its list stayed on "Loading bank…" (fixed below). |
| How-to guide, bot guide and support | ✅ Yes | 🖥️ Emulator | Guide: the screen, the crest, in-game features. Bot tools guide: a 4-step quick start, then every tool by what you want to do, in plain words, with a glossary. Support MVS: a note from Arglax, MVS and AUQW Discords, Buy Me a Coffee, credits (Skua team from the Skua donations page, VibeSkua, ArrowDev, Ruffle, the bosses-guide sheet). | GuideView, BotGuideView, SupportView; the bot guide is also in the bot Menu. Telemetry counts them as "other" screens, so no rules change. An InstaPay card is ready but hidden until its details are filled in (SupportViewModel). |
| One Streamer Mode | ✅ Yes | 📱 Phone | The Streamer button and Skua's Options > Streamer Mode are one switch: stand-in name "MVS User" and guild "MVS" on your name tag and portrait, other players, gold, coins and chat hidden, room name tidied. | ShellViewModel keeps IScriptOption.StreamerMode and its own flag in step both ways. The page draws the name tag and portrait (keeping the AFK prefix and guild brackets); ScriptOption.NameTagsDrawnByHost stops Skua's loop from writing them too, so they don't flicker. **Measured:** on, name tag "MVS User", guild "< MVS >", portrait "MVS USER", chat hidden; 40 samples over 4 s, no flicker. Three bugs found and fixed: turning it off blanked the real name (Skua's CustomName also writes objData.strUsername; MVS now leaves both to the page); gold stayed visible (current AQW uses mcGold); the room line read "\"battleon\"" with quotes. **Rechecked after the fixes:** on hides name, guild, portrait, gold and chat, room line "Battleon"; off restores name, guild, portrait and username (held 5 s later). The room line keeps "Battleon" until the game redraws it. |
| Inventory sorted by stars | ✅ Yes | 📱 Phone | With the inventory chip on a use case (Farming, Solo, Bossing, Ultra, PvP) the class list reads best first by its stars, favourites still on top; A-Z is the game's own order. | The game keeps its list order private, so ClassRatingsUI models it (classes matching the search; name, level, id; favourites first), checks it against every row the game bound itself, and only then rebinds the visible rows in star order. A filter it doesn't model (equipped, favourites only) leaves the list as the game drew it. **Measured:** Farming put the 5★ classes first (ArchMage, Blaze Binder, Dragon of Time…) against the game's A-Z (Alpha DOOMmega…); order held through swipes and a scrollbar drag, and A-Z restored the game's order exactly. Classes with long names cut short ("Chaos Slayer Mys…") showed no stars: fixed to match the shown part. **Rechecked:** cut-off names (Darkblood Storm…, Dragonslayer Gen…, Elemental Draco…, Evolved Lepreча…) now show stars; all 12 visible rows had them. |

## Combat

| Function | Ported | Tested | Description | Solution |
|---|---|---|---|---|
| Auto Attack / Auto Hunt | ✅ Yes | 📱 Phone | Fights or hunts monsters automatically. | Checked on the phone (icestormarena, private room): Auto Attack kills the cell's monsters, and Stop leaves combat. Hunt goes after the monsters of the cell it started in (or Manual MapIDs), moving to whichever cell has them alive through Skua's pad-corrected jump (checked: r4 has no Left pad, so it lands on Right). Each new target is logged as name, MapID, MonID and cell, shown on the page, and saved to monster-ids.json. If attacks keep failing against live monsters in the cell (standing "flat" on an invalid pad), it re-enters the cell at a valid pad. "Monsters in this map" lists every monster with its IDs; tapping one targets only it. **Measured 2026-11-06:** Attack killed a FrogZard (670 HP) within ~2 s of each respawn; Hunt walked from bs2 to the FrogZard's cell and kept killing for 30 s, avatar y constant (on the ground); Stop left combat (monster untouched for 20 s). Picking a monster fills its MapID, but the target line kept showing the old monster until Hunt started. |
| Smart skill rotations | ✅ Yes | 📱 Phone | Premade per-class rotations with conditions, e.g. "use the heal when HP is below 70%". | `AdvancedSkills.json` is the community's current file (268 classes, from auqw/Scripts) packed in the APK; updates keep your own edits and replace rotations you never changed. Auto uses the chosen class, or the equipped one, with all rule types: HP, mana, auras, party HP and waits. A heal whose HP rule is met now goes before the rest of the rotation (checked every 0.3 s). Not yet watched in a real fight. **Measured:** Kathool Adept, Farm mode, 30 s on ChickenCow: skills aa×21, 1×13, 3×2; 2 and 4 never (correct for Farm). Skill 3 fired each time the Feeding aura reached 10 (stacks 3→10, then 6→10). No "too soon" server warnings in a 30 s count. |
| Select Class / Select Mode (Auto page) | ✅ Yes | 📱 Phone | Pick which class and rotation Auto uses. | The list was empty on the phone: Ruffle's `JSON.stringify` wrote big whole numbers as `1354390967.0` and Skua refused the whole inventory. Fixed in Ruffle (`tools/ruffle-patches/json-whole-numbers.patch`). The list has a search box (no longer opens the keyboard by itself), stars to pin classes to the top (auto-pinned-classes.json), and use-case ranking: A–Z, Farming, Solo, Bossing, Ultra and PvP, each rated 0–5 stars like the r/AQW Simplified Bosses Guide sheet, with a one-line description, plus every use case's stars and the class's ultra role under each class (Skua.App.Avalonia/Assets/class-ratings.json: Ultra from that sheet's per-fight stars for its 42 classes, and on Ultra a picker ranks by one of its 22 fights; PvE anchored to the AQWG ultra-boss guide and AQWHub; farm/solo/boss from the aqwxcommunity class-setups tags, else estimated; most PvP ratings are estimates). Picking a use case also picks the class's matching mode (Farm, Solo, Atk, Supp…), and a line under the pickers says when to use each mode. Starting Auto Attack or Auto Hunt first picks up the class equipped in the game. The monster list refreshes by itself on each map join. The game's own inventory shows the same grades, drawn locally by skua.swf (nothing sent to the server): stars beside each class (drawn as shapes; the game fonts have no star glyph), Farm/Solo/Boss/Ultra/PvP stars and the ultra role in the Preview panel, and an "MVS: <use case>" chip on the list header that cycles the use case for both the inventory and the Auto page. skua.swf was patched with FFDec (no mxmlc here), see ClassRatingsUI.as. White names, yellow for member-only. Opening it used to black out the game: the shell hid the game for any open popup; now only for one over the game. |
| Skills editor | ✅ Yes | 👀 Opens | Write your own rotations and conditions. | VibeSkua's page (Menu → Combat → Skills). |
| Stats, Runtime helpers | ✅ Yes | 🖥️ Emulator | Kill and death counts; the drop pick-up list, registered quests and boosts. | VibeSkua's pages. On a narrow page Runtime and Skills now stack their columns and scroll instead of overlapping (`ResponsiveGrid`). |

## Tools and helpers

| Function | Ported | Tested | Description | Solution |
|---|---|---|---|---|
| Jump (cell and pad) | ✅ Yes | 📱 Phone | Move to a cell or pad. | MVS page combining VibeSkua's Jump with saved spots. **Measured:** cell list read from the map; Jump to Enter from Enter2 moved there in the same private room. Saved Spots joined battleontown-57391. |
| Saved spots / Fast Travel | ✅ Yes | 🖥️ Emulator | Tap Jump to do `/join map-room`; X deletes a spot. | One list shared by Jump and Menu → Fast Travel. Adding and deleting spots checked; an actual join not yet. |
| Loader | ✅ Yes | 👀 Opens | Load shops and quests by ID. | Layout fixed for phones. Loading isn't tested, by request. |
| Grabber | ✅ Yes | 📱 Phone | Read shop, quest, inventory and drop data. | Phone layout: category dropdown, details below the list. Grabbing from a live game not tried. **Measured:** Inventory grab listed 306 items, the game's own count 306. |
| Current Drops, Notify Drop, Junk Items | ✅ Yes | 👀 Opens | Drop list, drop alerts, junk filter. | VibeSkua's pages. The alert beep uses Android's tone player. |
| Bank | ✅ Yes | 📱 Phone | Opens the in-game bank (Menu → Game → Open Bank). | Depends on the Ruffle draw fix above. **Measured:** the game's bank list never loaded ("Loading bank…", 0 items, 18 s+). Cause: AQW fetches it over HTTP (api/char/bank) with ccid and token headers, which the local proxy dropped. Fixed by forwarding them (also used by saving a house room). **Rechecked after the fix:** 4,105 items listed 2.7 s after opening. |

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
| Login with relogin | ✅ Yes | 📱 Phone | Log in to a server; log back in after a disconnect or kick. | MVS Login page; remembers one account per client. Your first try got stuck, so it now shows each step and has a Cancel button. Not tried since; logging in through the game's own box works. **Measured:** a forced disconnect with no script running stayed on the login screen for 2 min+. Cause: Skua turns Auto Relogin off whenever a script stops (CoreBots turns it on while it runs). Fixed: MVS sets ScriptOption.KeepLoggedIn so Auto Relogin stays on; in-game Logout / Change Server still disarm it. **Rechecked after the fix:** disconnect with no script running → back in the world 5.6 s later, first attempt. It relogged to Twilly (Skua's default) instead of the server in use; fixed so a login stores the joined server (objServerInfo.sName) as the relogin server. Relogin lands in a public battleon room, as on desktop. |
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
| Drops panel (right rail, under Bag) | 📱 Phone | Drops that need accepting go to the game's own drop store instead of popping up; the Drops button shows how many wait. The panel: Keep / Decline per item, Select for several at once, Keep all, Decline all (tap twice), an auto-decline list and "Always keep AC items". Keep and Decline run the game's own Yes / No (bag-space checks included). **Measured 2026-11-07:** two Doom Overlord drops (Undead Energy ×17, Dark Energy ×51) landed in the store, the button showed 2 and the panel listed both with the game's AC / Member tags; no popup. Keep and Decline were checked against a stand-in game only (no real drop was kept or declined). |
| Quests opens the quest log | 📱 Phone | The Quests button opens the quest log itself (the game's L key) instead of the bar's small Quests list. **Measured:** "Current Quests" opened and a second tap closed it. |
| Second tap closes | 📱 Phone | Every rail button closes what it opened: Quests, Temp Inv, Map, Lore, Menu, Settings, Bag, Char, Drops (measured), and Bank and Guide (fixed 2026-11-07, Bank was open-only). |
| Heat warning and cool-down | 📱 Phone | Full speed up to 47°C battery temperature, a warning from 45°C; at 47°C (or Android's severe status) a cooler mode (no in-betweening, lighter anti-aliasing, 80% resolution, Android's sustained-performance clocks) until it is back under 45°C. **Measured:** the warning shows on the phone. **Found:** MIUI throttles the chip at about 87°C, when the battery reads only 43°C and Android's public status still says "none" (big cores capped at 1.6-1.8 of 3.2-3.35 GHz). Apps cannot read the chip's temperature; Android's throttling forecast (headroom ≥ 1.0) is the only sign. |
| Frame engine | 📱 Phone | Even frame pacing, 60 fps in-betweening of movement, Auto smooth (steps quality down when the game drops below 30, undoes a step that gains nothing), Ruffle culling and fewer GPU calls (tools/ruffle-patches/mvs-all.patch), Android game mode and the screen's top refresh rate. **Measured 2026-11-07, cool phone (34°C):** town 30.0 fps with frames 33.3 ± 1.5 ms apart (was ± 5.1), in-betweening 59 pictures/s at ± 3.4 ms (was 47.5 at ± 7.5), 8.9 ms of work a picture (was 20.6); badge book 29 fps (was 16-17). **Throttled phone (chip ~87°C, big cores 1.6 GHz):** town and badge book 30 fps, in-betweening only ~32 pictures/s; Doom Overlord (666k HP) in a private room: Kathool 26.7-28.6 fps, Void Highlord 27.4-27.7, Arcana Invoker 27.6 without in-betweening and 23.9 with it. |
| Problem reports and app log | 🖥️ PC + emulator | Crashes, MVS errors, scripts that stopped because of MVS and "Report a problem" (Support page) go to the movibeskua Firebase project (`movibeskua_reports`), without account names or passwords, at most 20 a day per install; the dashboard lists them under "Problem reports". The app's own log is a file on the phone (files/logs), never on screen. **Checked:** the log file fills on the emulator; the dashboard card renders a sample report. Sending needs the Firestore rules deployed. |
| Who should fix a script | 🖥️ PC | A script that stops with an error gets a plain message saying whether it is the script (point to the AUQW Discord, with "Copy details") or MVS (reported automatically). **Checked** with tools/ScriptTriageCheck on real compile errors and crashes: a null bug, a syntax error and a missing key blamed the script; Windows Forms and a platform-only call blamed MVS. |
| Release text and developer mode | 🖥️ Emulator | No developer readouts or raw errors on screen: plain error messages, statuses without brackets, the FPS meter shows only fps. Console, Packet Spammer, Packet Logger and the Debug and Flash log tabs only show in developer mode (tap the version line on the Support page seven times, then restart), which also allows debugging the game page from a PC. |

## Needs a check on the phone

These are ported but only a logged-in phone can confirm them:

1. In-game Options and the quality switch, skill cooldowns, drops, bank, shop and inventory (the Ruffle draw fix).
2. A fight with a class that has a premade rotation, to see the conditions fire.
3. Select Class / Select Mode on the Auto page now list your classes, and Auto Attack with no target picked fights the monsters in your cell.
4. SevenCircles War XP + Gold, or another CoreBots script, run to the end. (Aegis REP, a CoreBots script, ran to the end on 2026-11-06: compile 22.4 s, run 7 s, no errors. A long farming run is still untested.)
5. A relogin after a disconnect.
6. Army with two real accounts and an army script.
7. The plugins with a character: Daily Tracker's quest list, a Cosmetics outfit, ButlerV5 following between two accounts.
8. ~~Auto Hunt moves between cells without standing in mid-air, and stopping Auto leaves combat.~~ Measured OK 2026-11-06.
9. ~~Game FPS in a busy fight.~~ Measured 2026-11-07 (Frame engine above).
