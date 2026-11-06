# Usage stats: context for humans and LLMs

MoVibeSkua reports anonymous usage stats, the same way the Config Patcher and the Metadata Generator
do: counting starts with the app, with no prompt and no switch. This file is the whole contract.
Read it before changing anything under `MoVibeSkua.Android/Usage/`.

## What it measures

- **Functions used:** counters keyed by `Signal` (`Usage/Signal.cs`): app starts, crashes, game
  renderer losses, logins and relogins (kicked or not), script starts, stops and errors, how pages
  are opened (side rail or menu), layout controls, Streamer mode, and the Army (add, switch, close,
  full, low memory, and how many clients ran at once: `ARMY_SIZE_2` … `ARMY_SIZE_8_PLUS`).
- **Time per screen:** per `Screen`, only while the app is in the foreground. `GAME` is the game
  with no page over it; plugin pages and any page not in the list count as `OTHER`. Stored as
  aggregates: `visits`, `ms`, a dwell histogram (`d5` under 5 s, `d15`, `d60`, `d180`, `dmax`),
  `quick` (left under 4 s with nothing used) and `idle` (open 20 s or more with nothing used). A visit
  counts at most 30 minutes.
- **Time per day:** `days.dYYYYMMDD.{fg,run,bot}` in ms, by UTC day (`DayClock` in `UsageCore.cs`,
  split at midnight): `fg` the app on screen (no 30-minute cap), `run` a client process alive (army
  clients and screen-off botting included), `bot` a script or Auto Attack/Hunt running. The dashboard
  turns these into time in the app per day, active users per day, average per active user, and the
  running total. Added in the build after 0.1.1; older installs only have screen time.
- **Context:** `appVersion`, `appVersionCode`, `country` (two letters, from the mobile network, the
  SIM, then the language settings), `lastSeen`, `firstSeen`.

It never collects character or account names, passwords, script names or contents, maps, plugin
names, anything typed, the device model, or location. The identity is an anonymous Firebase Auth
uid with no link to the person.

## How it works

- `UsageTracker` counts in memory (`SignalCounters`: repeats of one signal within 250 ms count once)
  and times screens (`VisitClock`). Counts are saved to `files/usage/pending-<client>.json` every
  few signals and when the app goes to the background, so they survive the process being killed.
- Every army client is its own process and counts on its own, but they are one install: they share
  the sign-in in `files/usage/auth.json`, guarded by a file lock (`auth.lock`).
- `UsageSync` uploads over Firestore's REST API (no Firebase SDK): anonymous sign-up through
  Identity Toolkit, token refresh through Secure Token, then one `documents:commit` per upload that
  sets the plain fields by mask and adds every count as an `increment` transform, so uploads from
  several clients add up. Uploads run 30 s after start, every 30 minutes, and when the app goes to
  the background (at most every 5 minutes, so switching army clients does not upload each time).
- A failed upload keeps the counts for the next try. Counts under a name the rules no longer accept
  are dropped before upload, or the rules would refuse every write.
- Document: `movibeskua_installs/{uid}` in the `movibeskua` project, fields `schema` (1), `app`
  (`movibeskua`), `appVersion`, `appVersionCode`, `country`, `lastSeen`, `firstSeen`,
  `features.<SIGNAL>`, `screens.<SCREEN>.<field>`, `days.dYYYYMMDD.<kind>`.
- The rules let an install create, update or delete only its own document, only with known names;
  nobody can read documents from the app. Reporting runs through the Admin SDK. Rules cannot loop
  over map keys, so `days` is only checked to be a map of at most 2000 entries.

## Changing what is measured

1. Add the name to `Signal` or `Screen` (one per line) and call `UsageTracker.Track` or map the page
   key in `Screens.OfPage`.
2. Regenerate the rules and deploy them **before** shipping the APK:
   `python tools/generate_firestore_rules.py` (writes `firebase/firestore.rules`), then
   `firebase deploy --only firestore:rules` from `firebase/`. An APK that sends a name the live rules
   do not know gets every upload refused.
3. Give the new name a label in `tools/dashboard/index.html` (`LABELS` or `SCREEN_LABELS`).

## Setup (per machine)

1. Firebase project `movibeskua`, MoVibeSkua's own (Android app `com.arglax.movibeskua`, Firestore
   in `nam5`). `firebase/` holds its CLI config and rules. Anonymous Auth must stay enabled.
2. App config: `MoVibeSkua.Android/firebase.json` (gitignored), shaped like
   `firebase.example.json`: the project's web API key and id. The build bundles it as an asset when
   it exists; without it the app builds, runs and sends nothing.
3. Dashboard: `pip install firebase-admin`, save a `movibeskua` service account key (Firebase
   console > Project settings > Service accounts > Generate new private key) as `serviceAccount.json`
   at the repo root (gitignored), then `python tools/dashboard/serve_dashboard.py` (port 8768). The
   aggregator refuses a key for another project. Open
   `tools/dashboard/index.html` directly and press **Show demo data** to preview the layout without
   any credentials. ArgSense on the PC has a tile for it.

## Known limits

- Counts from one client process can sit on the phone until that client runs again (each client
  uploads its own file).
- A visit longer than 30 minutes counts as 30, so `GAME` time understates long bot sessions.
- `ARMY_SIZE_*` counts the client count when each army client starts, not how long it lasted.
