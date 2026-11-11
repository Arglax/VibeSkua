# Upstream catalog: VibeSkua v2.1.2 → v2.1.4

Checked 2026-11-11 against `NinjaXz/VibeSkua` `main` (= v2.1.4 + PR #66 release notes, no code after the tag).

## Where MVS stands

- MVS (`mvs-0.2.0`) branched from upstream at **389b0c9** (PR #28 merge, 2026-10-30) = `v2.1.1` + 93 commits.
- Already in our base from v2.1.2: PRs #25, #26, #27, #28, #29, #30, #32.
- Missing: **3 releases** (v2.1.2 rest, v2.1.3, v2.1.4), **33 PRs** (#31, #33–#47, #48 via c5bad94, #49–#66), 318 non-merge commits.
- A trial merge (`git merge-tree`) conflicts in 17 files (list at the end).

Relevance tags: **Port** = worth bringing to MVS; **Adapt** = needs a phone page or touch rework; **Merge** = core fix that comes with the merge; **Skip** = PC-only or already removed in MVS.

## v2.1.2 (2026-11-02), still missing

| PR | Change | Area | MVS |
|---|---|---|---|
| #33 | Class filter in Saved Advanced Skills | AdvancedSkillsView | Adapt (conflicts with our core-rotations work) |
| #34 | Relogin only to the chosen server unless the new **Relogin Fallback** option is on | ScriptServers, Options | Merge, but reconcile with MVS fallback order + 2/5/15/30 min back-off |
| #35 | UI polish and fixes | various views | Adapt |
| #36 | Hidden loading bar stops animating after load | LoaderView | Merge |
| #31 | Server latency measured with the SmartFox handshake | ScriptServers / server list | Port (better ping on phone server list) |
| #37 | Army Queue: account search, clearer role actions | Army Queue | Adapt (MVS army is per-process, own page) |

## v2.1.3 (2026-11-10), the big one: 289 commits

New features

| Feature | Files | MVS |
|---|---|---|
| **VibeSkua Remote**: separate Java Android app (`Skua.App.Android/`, `com.vibeskua.controller`, `skua-remote.apk`) that controls a PC client over LAN | `Skua.Core/Services/RemoteControlService.cs` (4,755 lines, HTTP API), `IRemoteControlService.cs` | Skip the Java app. The service is in Core and would start in MVS too: decide whether to disable on Android or use it to control MVS from a PC |
| **Calendar** (daily/weekly reset countdowns, current boosts) | `CalendarViewModel/View` | Port as a phone page |
| **Notification Center** (session feed of game/script events, category filters) | `NotificationCenterViewModel/View` | Port as a phone page; could feed the MVS notification |
| **Advanced Stats**: live gold/XP/item/CP rates, level/rank bars, rate units, rate history | `ScriptBotStats.cs` (+340), `IScriptBotStats.cs`, `StatsRateSnapshot.cs`, converters | Merge + Adapt. Conflicts with MVS's own Gold/Experience on `IScriptBotStats` |
| **Inventory**: quantity goals, highlights for equipped/stack/status items, filters | `IScriptInventory.cs` (+32), inventory views | Port the goals; MVS has its own touch inventory |
| **ButlerV5**: copy master's walking, quest sync incl. reward choices, Send All goto with target history, roster refresh, footer | `QuestSync.cs`, `PlayerTargetHistory.cs`, `FooterBar.cs`, `Follower.cs`, `RosterWindow.cs` | Merge (bundled plugin); re-check the shell-page layout |
| **Auto-jump** on map position changes | Application Options, `ScriptMap.cs` | Port |
| **Grandpa Mode**: bigger text in logs and packet output | `GrandpaModeState.cs` | Port (useful on phones) |
| Hotkeys: Pause Script action | `HotKeys.cs` | Skip (HotKeys removed on Android) |
| Army Control: Show FPS | Army Control | Adapt |
| Search Scripts order cycles name/date | ScriptRepo | Merge |
| Fast Travel defaults to private rooms | Fast Travel | Port (matches our private-room rule) |
| New monocle icons | Assets | Skip (MVS crest/icons) |
| Buttons shrink on press + hover glow | `HoverGlow.cs`, button styles | Port the press shrink only; skip hover glow |
| Scripts: favorite-aware inventory checks, safe `BankAll` | `ScriptInventory`, `ScriptBank` | Merge |
| Removed game size presets (#61) | Game options | Skip (MVS sizes the game itself) |
| `MaximizedResizeGuard`, `PointerCaptureReset` | Shared.Avalonia | `PointerCaptureReset` worth a look for touch; resize guard is desktop |

Fixes

| Fix | MVS |
|---|---|
| Quest Loader resumes after disconnect/relogin, scans more reliably (`QuestDataLoaderService` +279) | Merge |
| Relogin stops after an account lockout | Merge (fits `LastLoginError` stop logic) |
| Capture proxy / packet logger start/stop safer (`CaptureProxy.cs`) | Merge |
| Packet Interceptor filters match real packets | Skip (Interceptor removed on Android) |
| Hotkeys fire once per press | Skip |
| Scripts start unpaused; start notice before the script runs | Merge |
| Script options: removed enum choices reset to default instead of crashing | Merge (crash fix) |
| CoreBots presets/class picks kept across tab switches | Merge; check against our `CoreBotsViewModel` CBO case fix |
| Fast Travel editor dialog | Check with our Jump page |
| Settings kept if a reload fails (`UnifiedSettingsService`) | Merge |
| Dialogs from menus take the first click; tool windows stay on screen | Mostly desktop |
| Bad saved map cache recovers (`MapService`) | Merge |
| Boost search in inventory/bank | Merge |
| Initial gold-rate spike at login (#62) | Merge |
| `killLoginModals` ReferenceError #1069 (`Server.as`, try/catch, `"ModalStack" in mcO`) | **Port into skua.swf by hand** (FFDec `-importScript`; our swf is binary-patched) |

## v2.1.4 (2026-11-11)

| PR | Change | MVS |
|---|---|---|
| #63 | Relogin: one relogin loop at a time, `Logout()` marked intentional, tighter relogin/logout coordination | Merge; overlaps MVS relogin back-off in `ScriptServers.cs` |
| #64 | Compatibility with new FlashUtil changes: `ScriptManager` (+110), `ScriptAuto` (rewritten loop), `IScriptManager`, ButlerV5 `Broadcaster` hang fix, `RemoteControlService` | Merge; conflicts with MVS `ScriptManager` (no unload, CBO case fix) and `ScriptAuto` |
| #65, #66 | Version bump, release notes | Skip |

## Trial-merge conflicts (17)

- Workflows we deleted: `.github/workflows/build.yml`, `release.yml` (keep deleted)
- Binary: `Skua.AS3/skua/bin/skua.swf` (keep ours, hand-port `Server.as`)
- `Skua.App.Avalonia/AppStartup/Options.cs`, `ViewModels/MainMenu/MainViewModel.cs`, `ViewModels/ScriptLoaderViewModel.cs`, `Views/LoaderView.axaml`
- `Skua.Core.Interfaces/Scripts/IScriptBotStats.cs`, `Skua.Core/Scripts/ScriptBotStats.cs`
- `Skua.Core/Scripts/ScriptAuto.cs`, `ScriptManager.cs`, `ScriptServers.cs`
- `Skua.Plugin.ButlerV5/AvaloniaUi.cs`, `RosterWindow.cs`
- `Skua.Shared.Avalonia/Controls/Options/OptionContainerUserControl.axaml`, `Controls/ScriptRepo/ScriptRepoControl.axaml`, `ViewModels/Options/OptionContainerViewModel.cs`

20 more files are changed on both sides but auto-merge cleanly.
