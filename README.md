# MoVibeSkua

**[VibeSkua](https://github.com/NinjaXz/VibeSkua) on Android.** The Skua bot client for AdventureQuest Worlds, running on your phone. Same scripts, same tools, no PC needed.

> Vibe coded, like the project it ports.

---

## Install

1. Download `MoVibeSkua.apk` from [**Releases**](../../releases).
2. Open it on your phone and allow "Install unknown apps" when Android asks.
3. Launch **MoVibeSkua**. Android 8.0 or newer, 64-bit (arm64) phone.

## Use it

Turn your phone sideways. The game fills the middle; your controls sit on both edges.

The side buttons start as the game's own buttons, made big: **Quests, Temp Inventory, Map, Lore, Menu, House, Settings** on the left; **Bag, Bank, Rest, Char** on the right, then **Guide** (how the screen works, plus a plain-words guide to the bot tools). The **MVS crest** at the bottom right:

- **1 tap**: Support MVS (Discord help, a note from Arglax, credits).
- **2 quick taps**: swap the side buttons to the bot tools below (2 taps again to go back).

Bot tools:

| Left side | Right side |
| :--- | :--- |
| **Menu**: every Skua tool (options, bank, packets, plugins…) | **Auto**: auto attack / hunt |
| **Login**: log in, pick a server, auto-relogin | **Jump**: move between cells and pads |
| **Scripts**: load, start, pause, edit options | **Loader / Grabber**: quests, shops, items |
| **Repo**: browse and download community scripts | **Options**: game options (lag killer, etc.) |
| **Logs**, **Army** | **Streamer**: hide your name, guild, gold and chat |
| **▶ Start / ■ Stop**: run the loaded script | |

**First run:** the app offers to download the community scripts. Say yes, then open **Repo** and hit **Start** on any script.

**Background botting:** a notification keeps the bot running while the screen is off or you use other apps. Pull it down to see it's alive.

**Tips**
- Tap a side button again to close its panel.
- **⇔** on a panel's header widens it; the game shrinks to a small tile and keeps running.
- Use **Login** instead of typing into the game; it's faster and handles reconnects.
- Back button closes panels; on the game screen it sends the app to the background without stopping your script.
- A hardware keyboard enables Skua's hotkeys.

## Differences from desktop

- One client per app (no tabbed army host yet).
- Runs the game through [Ruffle](https://ruffle.rs) (Flash emulator) instead of Adobe Flash.
- **Edit Script** / **Open VSCode** open the script in any code editor installed on your phone (e.g. Acode); saving there updates the script.

## Build it yourself

Requires the .NET 10 SDK with the `android` workload, JDK 21 and the Android SDK.

```bash
./build-android.sh Release
```

The APK lands in `MoVibeSkua.Android/bin/Release/net10.0-android/`.

---

## Credits

MoVibeSkua is a side project by Arglax: I couldn't find a bot that runs on phones, so I made one. The phone side (running the game on Android, the touch controls and side rails, the class stars, the big in-game menus, the script window) was put together for this app; everything it stands on is other people's work.

Built on the work of:

- **Skua** ([auqw/skua](https://github.com/auqw/skua)): Breno_Henrike (creator, 0.1 to 1.2.3), Purple (maintainer, 1.2.4 to 1.4.3.0), Tato (script team manager), Bogali (secondary script manager), Vanilla99 (Ultras v1), Crow (script dev, Ultra v3), Lonewolf_12 (script dev), and the auqw script community (scripts and skill rotations). Support them: [Skua donations](https://auqw.github.io/Skua-and-Grim-Donations/).
- **VibeSkua** ([NinjaXz/VibeSkua](https://github.com/NinjaXz/VibeSkua)): Renoir Dessendre, creator of the VibeSkua branch this app builds on.
- **[ArrowDev](https://github.com/ArrowDev123/Skua)** (BlackArrow on Discord): early Avalonia port.
- **[Ruffle](https://github.com/ruffle-rs/ruffle)**: Flash emulation (MIT / Apache-2.0).
- **r/AQW Simplified Bosses Guide** (community sheet): the class star ratings.

Help and questions: [MVS Discord](https://discord.gg/Ybsy7KWE4).

## Disclaimer

**Educational and personal use only.** MoVibeSkua is a derivative of auqw/skua and VibeSkua, provided as-is under the same terms VibeSkua states (MIT). No ownership is claimed over the game, its assets or its data.

Botting may break AdventureQuest Worlds' Terms of Service and can get accounts banned. You use this entirely at your own risk.
