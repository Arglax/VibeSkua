namespace Skua.Flash.Ruffle;

public sealed class RuffleHostOptions
{
    /// <summary>
    /// Port to listen on. 0 picks a free port. The page's saved data (the game's settings, which
    /// Ruffle keeps in the page's local storage, and MVS's overlay settings) belongs to its origin,
    /// port included, so a fixed port keeps it from one launch to the next. When the port is taken,
    /// the host falls back to a free one (that launch starts with empty storage).
    /// </summary>
    public int Port { get; set; }

    /// <summary>
    /// Optional override for the loader SWF. When null the embedded skua.swf is used.
    /// </summary>
    public Func<byte[]>? SwfProvider { get; set; }

    /// <summary>
    /// Endpoint that lists the game socket servers. Each (sIP, iPort) pair gets a socket proxy entry.
    /// </summary>
    public string ServerListUrl { get; set; } = "https://game.aq.com/game/api/data/servers";

    /// <summary>
    /// Ruffle render quality: low, medium, high, best.
    /// </summary>
    public string Quality { get; set; } = "medium";

    /// <summary>
    /// Frame rate cap passed to the page. 0 leaves the SWF frame rate alone.
    /// </summary>
    public int FrameRate { get; set; }

    /// <summary>
    /// Caps the device pixel ratio the game canvas renders at; 0 leaves it alone. High-density phone
    /// screens otherwise make Ruffle fill 9x the pixels of a desktop window.
    /// </summary>
    public double MaxPixelRatio { get; set; }

    /// <summary>
    /// Ruffle renderer to try first ("webgl", "wgpu-webgl", "webgpu", "canvas"); empty uses Ruffle's default.
    /// </summary>
    public string PreferredRenderer { get; set; } = string.Empty;

    /// <summary>
    /// Row height for the game's item lists (inventory, shops, bank, merge and enhancement lists) and
    /// a larger, scrolling column of category icons; 0 keeps AQW's own 30-pixel rows. Phones use 60
    /// (twice the tap area, fewer rows on screen).
    /// </summary>
    public int TouchListRowHeight { get; set; }

    public string UserAgent { get; set; } = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/130.0 Safari/537.36";
}
