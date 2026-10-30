namespace Skua.Flash.Ruffle;

public sealed class RuffleHostOptions
{
    /// <summary>
    /// Port to listen on. 0 picks a free port.
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

    public string UserAgent { get; set; } = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/130.0 Safari/537.36";
}
