using CommunityToolkit.Mvvm.Messaging;
using Skua.Core.Flash;
using Skua.Core.Interfaces;
using Skua.Core.Messaging;
using Skua.Core.Utils;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Xml.Linq;

namespace Skua.Flash.Ruffle;

/// <summary>
/// <see cref="IFlashUtil"/> backed by Ruffle running in a web view, talking over <see cref="RuffleHost"/>.
/// </summary>
public class RuffleFlashUtil : IFlashUtil
{
    private readonly IMessenger _messenger;
    private readonly Lazy<IScriptManager> _lazyManager;
    private readonly ConcurrentDictionary<string, long> _lastErrorTrace = new();

    public RuffleFlashUtil(IMessenger messenger, Lazy<IScriptManager> manager, RuffleHost host)
    {
        _messenger = messenger;
        _lazyManager = manager;
        Host = host;
        Host.Control.ExternalCall += OnExternalCall;
    }

    public RuffleHost Host { get; }

    public event FlashCallHandler? FlashCall;

    /// <summary>
    /// Raised when the page has to be (re)loaded. The platform view navigates its web view to the URL.
    /// </summary>
    public event Action<string>? PageRequested;

    public void InitializeFlash()
    {
        Host.Start();
        if (!Host.Control.Post("{\"t\":\"reload\"}"))
            PageRequested?.Invoke(Host.PageUrl);
    }

    private void OnExternalCall(string name, string argsJson)
    {
        try
        {
            FlashCall?.Invoke(name, FlashValues.ExternalArgs(argsJson));
        }
        catch (Exception ex)
        {
            System.Diagnostics.Trace.WriteLine($"Flash CallHandler exception: {ex.Message}");
        }
    }

    public string? Call(string function, params object[] args)
    {
        return Call<string>(function, args);
    }

    public T? Call<T>(string function, params object[] args)
    {
        try
        {
            object? o = Call(function, typeof(T), args);
            return o is T value ? value : GetDefaultValue<T>();
        }
        catch
        {
            return GetDefaultValue<T>();
        }
    }

    private static T GetDefaultValue<T>()
    {
        object? defaultValue = DefaultProvider.GetDefault<T>(typeof(T));
        return defaultValue is T typed ? typed : default!;
    }

    public object? Call(string function, Type type, params object[] args)
    {
        if (_lazyManager.Value.ShouldExit && Thread.CurrentThread.Name == "Script Thread")
            _lazyManager.Value.ScriptCts?.Token.ThrowIfCancellationRequested();

        try
        {
            string? raw = Host.Control.Call(function, FlashValues.SerializeArgs(args));
            return FlashValues.ConvertResult(FlashValues.ResultToString(raw), type);
        }
        catch (Exception e)
        {
            if (function != "isKicked")
            {
                _messenger.Send<FlashErrorMessage>(new(e, function, args));
                TraceError(function, e);
            }
            return default;
        }
    }

    /// <summary>
    /// The Flash log only keeps the function name; trace why it failed, at most every 30s per function.
    /// </summary>
    private void TraceError(string function, Exception e)
    {
        long now = Environment.TickCount64;
        if (_lastErrorTrace.TryGetValue(function, out long last) && now - last < 30_000)
            return;
        _lastErrorTrace[function] = now;
        Trace.WriteLine($"[Flash] {function} failed: {e.GetType().Name}: {e.Message}");
    }

    public object FromFlashXml(XElement el) => FlashValues.FromFlashXml(el);

    public IFlashObject<T> CreateFlashObject<T>(string path)
    {
        return new FlashObject<T>(Call<int>("lnkCreate", path), this);
    }

    /// <summary>
    /// Runs JavaScript in the page (fire and forget). Used for player-level settings such as quality.
    /// </summary>
    public bool PostCommand(string json) => Host.Control.Post(json);

    public void Dispose()
    {
        Host.Control.ExternalCall -= OnExternalCall;
    }
}
