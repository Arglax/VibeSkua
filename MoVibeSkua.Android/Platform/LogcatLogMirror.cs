using CommunityToolkit.Mvvm.Messaging;
using Skua.Core.Messaging;
using Skua.Core.Models;

namespace MoVibeSkua.Android.Platform;

/// <summary>
/// Copies script and flash log lines to logcat under the MoVibeSkua tag. Skua keeps them only in
/// memory for the Logs page, so a script that stops on a phone otherwise leaves nothing to read.
/// </summary>
public sealed class LogcatLogMirror : IRecipient<AddLogMessage>
{
    private const string Tag = "MoVibeSkua";
    private static readonly LogcatLogMirror Instance = new();

    public static void Start()
    {
        if (!WeakReferenceMessenger.Default.IsRegistered<AddLogMessage>(Instance))
            WeakReferenceMessenger.Default.Register(Instance);
    }

    public void Receive(AddLogMessage message)
    {
        // Debug lines come from Trace, which LogcatTraceListener already sends.
        if (message.LogType != LogType.Debug && !string.IsNullOrEmpty(message.Text))
        {
            global::Android.Util.Log.Info(Tag, $"[{message.LogType}] {message.Text}");
            // Script and Flash lines go into the app log too, as context for problem reports.
            Diagnostics.AppLog.Add($"[{message.LogType}] {message.Text}");
        }
    }
}
