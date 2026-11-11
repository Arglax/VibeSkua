# MoVibeSkua (MVS) vs VibeSkua: what's different

MoVibeSkua is VibeSkua running on Android phones. It isn't a rewrite: it runs the same Skua / VibeSkua
C# code (Skua.Core, the view models, the script engine, CoreBots support) and the same community scripts
from the Auto-Users-Quest-Worlds repo, compiled on the phone. What changed is everything around it: the
game player, the window layout, touch controls, and the parts of .NET that behave differently on Android.

- Base: VibeSkua `main` at commit 389b0c9, plus the MVS commits on branch `mvs-0.2.0`.
- Tested mostly on a Poco X6 Pro (Android 16). Busy Battleon runs at about 37-41 fps; farming at about 48 fps.
- Made by Arglax. Script problems go to the script team; problems with MVS itself go to Arglax. The app
  says which is which when something fails.

---

## 1. Ported (works on the phone)

Mostly the same pages, rearranged for a phone screen:

- **Scripts:** Script Loader, Script Repo (search, download, update all; all ~1,950 scripts), Scheduler,
  script options (now with item dropdowns, see section 3), script logs.
- **Bot tools:** Auto (attack / hunt), Jump, Fast Travel, Loader, Grabber, Current Drops, Junk Items,
  Loadouts, Stats, Runtime, Skills (Advanced Skills editor), Bank.
- **Options:** Game options, Application options, CoreBots options (per account), Themes.
- **Auto re-login** and the relogin watchdog.
- **Streamer mode.**
- **Plugins** that ship with VibeSkua (Daily Tracker, ButlerV5, Cosmetics). They open as pages instead of
  windows, and Butler includes VibeSkua's latest features (`Send to player` with recent target history,
  `Follow master's quests`, `Copy master's walking`, and responsive mobile layouts).
- **Discord / Nexus webhooks.** Options kept; screenshots are taken in an Android way.
- **Console, Spammer, Logger:** behind a developer mode.

Ported but not fully tested with a real character yet: Scheduler, Loadouts, long CoreBots runs, Army
with real accounts.

## 2. Not ported (and why)

| Feature | Why not |
|---|---|
| HotKeys | Global hotkeys hook desktop windows. A phone has no keyboard shortcuts to bind. |
| Packet Interceptor | It's a local capture proxy. The game on the phone talks to AQW directly through MVS's own bridge. |
| Tabbed / grid multi-client window, tray icon | One screen shows one client at a time on a phone. |
| Army Control broadcast menu, Army Scheduler | They lived in the tabbed desktop window. Army itself exists (see below), the broadcast menu doesn't yet. |
| Skua Manager app (launcher, army queue, account groups and tags, client updates) | A separate desktop program. MVS has its own simpler Account Manager. |
| Velopack app updater | Windows-only. MVS updates are APKs. |
| Flash Player options (clear Flash cache, WMode, preload) | There's no Flash Player on Android (see section 4). |
| Custom window backgrounds and title-bar art | Nothing on Android draws a window frame. |

## 3. New in MVS (not in VibeSkua)

**Phone app**
- A single screen with side rails (landscape) or a bottom bar (portrait). Pages slide over the game and
  can be dragged wider.
- Double-tap the MVS crest to swap the rails between the game's own buttons (quests, bag, map, house...)
  and the bot tools.
- Account Manager: saved accounts encrypted with the Android Keystore. Tap to log in, add or edit accounts.
- Runs in the background as a foreground service, with notification controls (start, stop, pause, a farm
  summary). A bot-only mode stops drawing the game to save battery.
- Graphics & Performance page: presets, resolution, anti-aliasing, colour styles, fps meter, smooth
  motion. Starting values follow the phone's hardware.
- Heat handling: reads Android's thermal status and switches to a cooler mode before the phone throttles.
- Guides, a Bot tools guide and a Support page. Problem reports with an automatic "script or app?"
  verdict. Anonymous usage counts (which screens and features get used) for a dashboard. The Skua /
  VibeSkua client has no telemetry of its own; CoreBots' optional data collection is separate and
  unchanged.

**In the game (skua.swf mobile modules)**
- Touch HUD: bigger skill cluster around auto attack, potion slot, portraits, drops tray in the skill bar.
- Dynamic joystick on the left 35% of the screen, swipe scrolling in lists, big touch versions of the
  game's small menus.
- Ten "MVS" rows at the top of the game's own Options > Controls: Improve FPS, Mobile Layout, Dynamic
  Joystick, Long Dash, Swipe Scrolling, Improved Auras, Inventory UI, Inventory Filter, Class Rating,
  MVS Drops.
- Touch inventory (taller rows, gear sort by damage type or boost) and class star ratings.
- Aura overlay: your auras and your target's, with stacks and 0.1 s timers.

**Bot features**
- **Auto Enhance:** before a script starts, checks the equipped class, weapon, helm and cape against the
  recommended set for your class (AUQW's SmartEnhance lists from CoreAdvanced.cs; Ultra sets from the
  r/AQW Simplified Bosses Guide) and fixes what's off. You choose the use case (farming, bossing, ultra)
  and up to 3 pieces it may change. It says so when an enhancement is locked or too expensive.
- **CoreBots import:** copy your `CBO_Storage(name).txt` from PC and import it on the phone.
- **Item dropdowns in script options:** an option like "insert Name of your Absolution Cape" lists the
  matching items you own (inventory, and bank once loaded). Typing a name still works.
- **Built-in "core" rotations:** class rotations checked against the class's actual mechanics. They're
  used only for modes the player never customised. First one: ArchMage (see section 5).
- **Combat tweaks:** heals with an "HP below" rule jump the queue; re-entering the cell when attacks keep
  failing.

## 4. How we solved the hard parts

**No Flash Player on Android.** The game runs in **Ruffle** (the Flash emulator, as WebAssembly) inside an
Android WebView. A small local server inside the app (RuffleHost) serves Ruffle, proxies the game's HTTP
requests, and bridges the game's SmartFox socket over a WebSocket. Skua's `IFlashUtil` is implemented
over a control channel to the page, and the ActiveX-style call and return format is reproduced, so
Skua.Core didn't need to change. Each call to the game costs 10-30 ms instead of under 1 ms, so we added
a read cache, batched reads and a slower watch loop.

**Ruffle bugs and speed.** AQW didn't fully work in stock Ruffle. We patched it, then moved the patches
into a public fork, Arglax/ruffle:
- `BitmapData.draw` was missing in WebGL, so AQW's scripts aborted and the player never loaded.
- Some blend modes were missing.
- `JSON.stringify` wrote whole numbers as "100.0", which emptied Skua's inventory and class lists.
- Overridden class constants weren't applied, so Long Dash had no effect.
- Plus a frame engine (in-between frames, even pacing, culling) and memory work, because AQW loads every
  player's gear as a separate file and filled memory in minutes.

**.NET on Android (Mono).** Android runs .NET on Mono, not the desktop runtime:
- **JIT only:** no AOT and no trimming, because scripts are compiled at runtime and use reflection.
- **Roslyn on the phone:** the reference assemblies a script compiles against are shipped inside the APK
  and unpacked on first run. A big CoreBots script compiles in about 20-40 s the first time, then comes
  from the cache.
- **Windows-only script calls:** a small compatibility layer stubs the WinForms bits scripts touch
  (MessageBox, Clipboard...) and makes CoreBots' Windows version check pass.
- **Script unloading:** on Mono, unloading a finished script's compiled code later crashed the garbage
  collector ("sgen-scan-object.h:93"). On Android, script code now stays loaded until the app closes
  (a few MB per script run).
- **Modal dialogs:** the desktop code expects a dialog call to block until closed. On Android that
  froze the app, so modals run a nested Android message loop.

**Case-sensitive file names (the "works on PC, not on the phone" errors).** Windows ignores letter case in
file names; Android doesn't. This broke things in two places:
- **CoreBots options:** CoreBots reads `options/CBO_Storage(<server name, lower case>).txt`, but the
  options page saved it under the name as typed at login ("Arglax"). Scripts didn't find it and ran on
  Generic classes. Now it saves under the name CoreBots reads, and moves files saved under another case.
- **Script includes:** 6 include paths in the scripts repo differ from the real file only by case
  (list in section 5). Scripts using them failed with "The type or namespace name 'CoreQOM' could not be
  found". MVS now matches include paths ignoring case.

**One window instead of many.** Desktop windows (tools, plugins, dialogs) become stacked pages. The game
view is clipped instead of resized when a page covers it. Touch input was adjusted so the Flash text
fields keep focus (otherwise the password couldn't be typed).

**Army on Android.** Each extra client runs in its own Android process with its own WebView data folder,
because one WebView data folder can't be shared between processes. The first client's service keeps the
others alive. For Butler's cross-process sync files, Android truncates `/proc/<pid>/stat` process names to
15 characters (`com.arglax.movi`), so `SyncFile.IsProcessAlive` checks `/proc/<pid>` and `/proc/<pid>/cmdline`
directly on Android so Army clients detect each other as online.

**Re-login on a bad connection.** When the login server stopped answering, the relogin watchdog started a
new round 1.5 s after the last one gave up, every ~20 s indefinitely. It now pauses for 2, 5, 15, then
30 minutes, and resets once you're in game. Fallback servers put full and canned-chat servers last.

## 5. Things we found that matter upstream too

These came up while porting. Most affect PC users too, or will on Linux and Mac:

1. **Wrong-case script includes** (scripts repo). They work on Windows only:
   - `Story/QueenofMonsters/CoreQoM.cs`: the file is `CoreQOM.cs` (28 scripts)
   - `Good/BLOD/CoreBLOD.cs`: the folder is `Good/BLoD` (26 scripts)
   - `Story/FireIsland/CoreFireIsland.cs`: the file is `Story/Fireisland/CoreFireisland.cs` (12 scripts)
   - `Story/AgeofRuin/CoreAOR.cs`: the folder is `Story/AgeOfRuin` (8 scripts)
   - `Tools/ForDevelopers/GeneratorHelpers/GenerateSupportUtils.cs`: the file is `Generatesupportutils.cs` (5)
   - `Story/Nukemichi[Mem].cs`: the file is `Nukemichi[mem].cs` (1)
2. **ArchMage advanced skills.** An older published `AdvancedSkills.json` spelled the aura
   "Corporeal **Ascenion**". That rule never matched, so Arcane Sigil also fired in Corporeal Ascension
   and flipped the player out of the heal stance. The current file has the spelling fixed, but the Base
   rotation still only allows Arcane Sigil above 70% HP and below 70% mana. Corporeal Ascension's heal
   over time scales with mana when cast and matters most at low HP, so ArchMage dies farming. Glacial
   Impact (foes deal 30% less damage) only runs below 30% mana. MVS's core rotation instead casts Sigil
   whenever Arcane Flux is up and the player isn't already in Corporeal.
3. **Expired auras count as active.** The game keeps an aura in its list for a moment after it ends, with
   0 s left. Aura rules counted it, so Arcane Sigil was cast on an expired Arcane Flux. MVS ignores auras
   with no time left (`ScriptSelfAuras` / `ScriptTargetAuras.GetAuraSnapshot`).
4. **The "skip" rule path casts the next skill unchecked.** In `ScriptSkill`, when a rule says "skip",
   the loop casts the next skill in the rotation without checking that skill's own rules. MVS checks
   them.
5. **`CurrentWeaponSpecial()` in CoreAdvanced.cs** reads the weapon's `EnhancementPatternID` instead of
   its `ProcID`, so it reports the wrong weapon special (seen with Auto Enhance).
6. **Re-login loop** (section 4): no back-off after a failed round, so a server that refuses logins gets
   hit every ~20 s for as long as it refuses.

## 6. Known issues (MVS)

- Ruffle can crash ("memory access out of bounds") while big game files download over a very slow
  connection. It's being traced in the fork's memory code; a normal connection doesn't trigger it.
- After a second engine crash in a row the game page doesn't reload by itself; restart the app.
- Army is tested on the emulator only, not with real accounts yet.
