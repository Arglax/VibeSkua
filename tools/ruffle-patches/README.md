# Ruffle patches

Since 2026-11-08, `Skua.Flash.Ruffle/web/ruffle` is a build of the shared fork https://github.com/Arglax/ruffle
(branch `arglax`; the commit is in `web/ruffle/VERSION`), which DreamQuest also ships. The fork's base commit
is exactly the patched tree described below (`mvs-all.patch`); on top it has the AVM1 library fix (AVM2
games such as AQW are unaffected), the class-constant fix (private constants set in a class initializer,
such as `MoveInput.DASH_PX`, are now replaced too) and WebGL draw-call batching. Batching neither helped
nor hurt AQW in a busy room (paired A/B, 37.3 vs 37.2 fps; the first version cost about 25%), so
`bridge.js` turns it off with `window.__arglaxNoBatch = true`. Builds come from the fork's GitHub Actions
(`gh run download <run> --repo Arglax/ruffle`). Ruffle changes for MVS go through the fork: agree with the
DreamQuest session before editing shared files (`library.rs`, `player.rs`, the WebGL renderer).

Before that, it was Ruffle `nightly-2026-10-29` built from source with these patches.

- `webgl-offscreen-draw.patch`: the WebGL renderer gains `render_offscreen` and `resolve_sync_handle`, so
  `BitmapData.draw` works instead of raising an error that aborts the calling AS3 script. AQW relies on
  draw for the Options window, visual skill cooldowns, the drops UI and shop/inventory/bank rows. It also
  makes `set_quality` rebuild the MSAA buffers, so the game's quality setting changes anti-aliasing.
- `background-skip-render.patch`: while the page is hidden (`background_tick_mode`, an army client that
  is not on screen) the player keeps ticking but skips `core.render()`, so a hidden client draws nothing.
  `needs_render` stays set, so the first tick back in the foreground redraws.
- `webgl-blend-modes.patch` (on top of `webgl-offscreen-draw.patch`; touches core too): the WebGL renderer only
  knew Normal, Add and Subtract; every other blend mode drew as Normal (badges such as Honorable Hero 2018 came
  out flat purple, avatar armour shading too). Screen, Multiply and Lighten now use GL blend functions. Overlay,
  Hard Light, Difference, Invert and Darken need the pixels behind them: the core caches the object that holds
  them as a bitmap (cacheAsBitmap, now supported by this renderer, up to 1024x1024 px), and inside that small
  image the renderer draws each such child into a layer and a shader mixes it in (the wgpu formulas). On the
  stage itself they still draw as Normal: a stage-sized layer per piece dropped the game to 3 fps (an avatar
  has ~29 Overlay pieces). Offscreen buffers are pooled across frames. Filters alone no longer make an object
  cached (this renderer cannot draw filters). `create_empty_texture` now allocates storage.
- `json-whole-numbers.patch`: AS3 `JSON.stringify` writes a whole Number the way Flash does (`1354390967`,
  not `1354390967.0`). Numbers too big for Ruffle's small-integer form (inventory `CharItemID`s, boss HP) came
  out with `.0`, and Skua's integer fields refused the whole list: the Auto page had no classes.

- `mvs-all.patch`: every change above in one file, plus the **MVS frame engine** (only in this file):
  - *In-betweening*: each display object keeps its matrix from before its first change in the current game
    frame; when the page sets `window.__mvsInterp` (pictures a second, 0 = off) the player also draws between
    game frames, placing moved objects part of the way along (`display_object::mvs_interp`). Jumps over
    120 px, mirror flips, big turns and objects that just appeared or changed parent are drawn as they are.
    Cached bitmaps are drawn without it. AQW still runs its logic at 30 frames a second.
  - *Even pacing*: a frame due within half a display refresh runs now instead of one refresh late, so 30 fps on
    a 60/120 Hz screen stays evenly spaced.
  - `window.__mvsFrames` counts game frames run, for the page's FPS meter and frame governor.
  - *Culling* (`display_object::mvs_cull`): shapes, text and bitmaps whose box lies wholly outside the
    screen, or outside the mask or scroll rect they are drawn under, are not drawn. Ruffle drew all of
    them (5,200 draw calls a picture in AQW's badge book).
  - *Fewer GPU calls*: a pooled offscreen target's `checkFramebufferStatus` (which waits for the GPU,
    about 3 ms on a phone) runs once, when it is first used; the world matrix and gradient uniforms are
    only sent when they differ from the last draw's; in-between pictures reuse bitmap caches and skip
    the blend-cache check.

  Apply it alone on a clean checkout instead of the separate patches.

## Building

Needs Rust stable with the `wasm32-unknown-unknown` target, `wasm-bindgen-cli` at the version in Ruffle's
`Cargo.lock` (0.2.127), binaryen's `wasm-opt` (version_129) and Node.js.

```bash
git clone --depth 1 --branch nightly-2026-10-29 https://github.com/ruffle-rs/ruffle.git
cd ruffle && git apply /path/to/mvs-all.patch
cd web && npm ci --ignore-scripts
CARGO_FEATURES=lzma npm run build --workspace=ruffle-core
npm run build --workspace=ruffle-selfhosted
```

Copy `ruffle.js`, `ruffle_web_bg.*.wasm` and the licenses from `web/packages/selfhosted/dist` into
`Skua.Flash.Ruffle/web/ruffle`. Only the WebAssembly-extensions build is made; Android WebView supports it.
`jpegxr` is left out because it needs clang.
