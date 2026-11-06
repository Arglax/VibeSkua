# Ruffle patches

`Skua.Flash.Ruffle/web/ruffle` is Ruffle `nightly-2026-10-29` built from source with these patches.

- `webgl-offscreen-draw.patch`: the WebGL renderer gains `render_offscreen` and `resolve_sync_handle`, so
  `BitmapData.draw` works instead of raising an error that aborts the calling AS3 script. AQW relies on
  draw for the Options window, visual skill cooldowns, the drops UI and shop/inventory/bank rows. It also
  makes `set_quality` rebuild the MSAA buffers, so the game's quality setting changes anti-aliasing.
- `background-skip-render.patch`: while the page is hidden (`background_tick_mode`, an army client that
  is not on screen) the player keeps ticking but skips `core.render()`, so a hidden client draws nothing.
  `needs_render` stays set, so the first tick back in the foreground redraws.
- `json-whole-numbers.patch`: AS3 `JSON.stringify` writes a whole Number the way Flash does (`1354390967`,
  not `1354390967.0`). Numbers too big for Ruffle's small-integer form (inventory `CharItemID`s, boss HP) came
  out with `.0`, and Skua's integer fields refused the whole list: the Auto page had no classes.

## Building

Needs Rust stable with the `wasm32-unknown-unknown` target, `wasm-bindgen-cli` at the version in Ruffle's
`Cargo.lock` (0.2.127), binaryen's `wasm-opt` (version_129) and Node.js.

```bash
git clone --depth 1 --branch nightly-2026-10-29 https://github.com/ruffle-rs/ruffle.git
cd ruffle && git apply /path/to/webgl-offscreen-draw.patch /path/to/background-skip-render.patch /path/to/json-whole-numbers.patch
cd web && npm ci --ignore-scripts
CARGO_FEATURES=lzma npm run build --workspace=ruffle-core
npm run build --workspace=ruffle-selfhosted
```

Copy `ruffle.js`, `ruffle_web_bg.*.wasm` and the licenses from `web/packages/selfhosted/dist` into
`Skua.Flash.Ruffle/web/ruffle`. Only the WebAssembly-extensions build is made; Android WebView supports it.
`jpegxr` is left out because it needs clang.
