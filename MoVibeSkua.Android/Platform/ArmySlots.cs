using Android.App;
using Android.Content.PM;
using Android.OS;
using Android.Views;

namespace MoVibeSkua.Android.Platform;

// Android fixes an app's processes in its manifest, so each army client slot has an activity and a
// keep-alive service of its own, declared in that client's process. They add nothing to their bases.

/// <summary>Bound by the first client to keep an army client's process running (see KeepAliveService).</summary>
public abstract class ArmySlotService : Service
{
    public override IBinder? OnBind(global::Android.Content.Intent? intent) => new Binder();
}

internal static class ArmySlot
{
    public const string Theme = "@style/MyTheme.NoActionBar";

    /// <summary>Same as the first client: landscape, either way up.</summary>
    public const ScreenOrientation Orientation = ScreenOrientation.SensorLandscape;

    public const ConfigChanges Changes = ConfigChanges.Orientation | ConfigChanges.ScreenSize | ConfigChanges.UiMode | ConfigChanges.ScreenLayout | ConfigChanges.SmallestScreenSize | ConfigChanges.Keyboard | ConfigChanges.KeyboardHidden | ConfigChanges.Density;
}

[Activity(Name = "com.arglax.movibeskua.Army2Activity", Label = "MVS 2", Process = ":army2", TaskAffinity = "com.arglax.movibeskua.army2",
    Theme = ArmySlot.Theme, Icon = "@mipmap/ic_launcher", Exported = false, LaunchMode = LaunchMode.SingleTask,
    WindowSoftInputMode = SoftInput.AdjustResize, ConfigurationChanges = ArmySlot.Changes, ScreenOrientation = ArmySlot.Orientation)]
public sealed class Army2Activity : MainActivity { }

[Service(Name = "com.arglax.movibeskua.Army2Service", Process = ":army2", Exported = false)]
public sealed class Army2Service : ArmySlotService { }

[Activity(Name = "com.arglax.movibeskua.Army3Activity", Label = "MVS 3", Process = ":army3", TaskAffinity = "com.arglax.movibeskua.army3",
    Theme = ArmySlot.Theme, Icon = "@mipmap/ic_launcher", Exported = false, LaunchMode = LaunchMode.SingleTask,
    WindowSoftInputMode = SoftInput.AdjustResize, ConfigurationChanges = ArmySlot.Changes, ScreenOrientation = ArmySlot.Orientation)]
public sealed class Army3Activity : MainActivity { }

[Service(Name = "com.arglax.movibeskua.Army3Service", Process = ":army3", Exported = false)]
public sealed class Army3Service : ArmySlotService { }

[Activity(Name = "com.arglax.movibeskua.Army4Activity", Label = "MVS 4", Process = ":army4", TaskAffinity = "com.arglax.movibeskua.army4",
    Theme = ArmySlot.Theme, Icon = "@mipmap/ic_launcher", Exported = false, LaunchMode = LaunchMode.SingleTask,
    WindowSoftInputMode = SoftInput.AdjustResize, ConfigurationChanges = ArmySlot.Changes, ScreenOrientation = ArmySlot.Orientation)]
public sealed class Army4Activity : MainActivity { }

[Service(Name = "com.arglax.movibeskua.Army4Service", Process = ":army4", Exported = false)]
public sealed class Army4Service : ArmySlotService { }

[Activity(Name = "com.arglax.movibeskua.Army5Activity", Label = "MVS 5", Process = ":army5", TaskAffinity = "com.arglax.movibeskua.army5",
    Theme = ArmySlot.Theme, Icon = "@mipmap/ic_launcher", Exported = false, LaunchMode = LaunchMode.SingleTask,
    WindowSoftInputMode = SoftInput.AdjustResize, ConfigurationChanges = ArmySlot.Changes, ScreenOrientation = ArmySlot.Orientation)]
public sealed class Army5Activity : MainActivity { }

[Service(Name = "com.arglax.movibeskua.Army5Service", Process = ":army5", Exported = false)]
public sealed class Army5Service : ArmySlotService { }

[Activity(Name = "com.arglax.movibeskua.Army6Activity", Label = "MVS 6", Process = ":army6", TaskAffinity = "com.arglax.movibeskua.army6",
    Theme = ArmySlot.Theme, Icon = "@mipmap/ic_launcher", Exported = false, LaunchMode = LaunchMode.SingleTask,
    WindowSoftInputMode = SoftInput.AdjustResize, ConfigurationChanges = ArmySlot.Changes, ScreenOrientation = ArmySlot.Orientation)]
public sealed class Army6Activity : MainActivity { }

[Service(Name = "com.arglax.movibeskua.Army6Service", Process = ":army6", Exported = false)]
public sealed class Army6Service : ArmySlotService { }

[Activity(Name = "com.arglax.movibeskua.Army7Activity", Label = "MVS 7", Process = ":army7", TaskAffinity = "com.arglax.movibeskua.army7",
    Theme = ArmySlot.Theme, Icon = "@mipmap/ic_launcher", Exported = false, LaunchMode = LaunchMode.SingleTask,
    WindowSoftInputMode = SoftInput.AdjustResize, ConfigurationChanges = ArmySlot.Changes, ScreenOrientation = ArmySlot.Orientation)]
public sealed class Army7Activity : MainActivity { }

[Service(Name = "com.arglax.movibeskua.Army7Service", Process = ":army7", Exported = false)]
public sealed class Army7Service : ArmySlotService { }

[Activity(Name = "com.arglax.movibeskua.Army8Activity", Label = "MVS 8", Process = ":army8", TaskAffinity = "com.arglax.movibeskua.army8",
    Theme = ArmySlot.Theme, Icon = "@mipmap/ic_launcher", Exported = false, LaunchMode = LaunchMode.SingleTask,
    WindowSoftInputMode = SoftInput.AdjustResize, ConfigurationChanges = ArmySlot.Changes, ScreenOrientation = ArmySlot.Orientation)]
public sealed class Army8Activity : MainActivity { }

[Service(Name = "com.arglax.movibeskua.Army8Service", Process = ":army8", Exported = false)]
public sealed class Army8Service : ArmySlotService { }

[Activity(Name = "com.arglax.movibeskua.Army9Activity", Label = "MVS 9", Process = ":army9", TaskAffinity = "com.arglax.movibeskua.army9",
    Theme = ArmySlot.Theme, Icon = "@mipmap/ic_launcher", Exported = false, LaunchMode = LaunchMode.SingleTask,
    WindowSoftInputMode = SoftInput.AdjustResize, ConfigurationChanges = ArmySlot.Changes, ScreenOrientation = ArmySlot.Orientation)]
public sealed class Army9Activity : MainActivity { }

[Service(Name = "com.arglax.movibeskua.Army9Service", Process = ":army9", Exported = false)]
public sealed class Army9Service : ArmySlotService { }

[Activity(Name = "com.arglax.movibeskua.Army10Activity", Label = "MVS 10", Process = ":army10", TaskAffinity = "com.arglax.movibeskua.army10",
    Theme = ArmySlot.Theme, Icon = "@mipmap/ic_launcher", Exported = false, LaunchMode = LaunchMode.SingleTask,
    WindowSoftInputMode = SoftInput.AdjustResize, ConfigurationChanges = ArmySlot.Changes, ScreenOrientation = ArmySlot.Orientation)]
public sealed class Army10Activity : MainActivity { }

[Service(Name = "com.arglax.movibeskua.Army10Service", Process = ":army10", Exported = false)]
public sealed class Army10Service : ArmySlotService { }

[Activity(Name = "com.arglax.movibeskua.Army11Activity", Label = "MVS 11", Process = ":army11", TaskAffinity = "com.arglax.movibeskua.army11",
    Theme = ArmySlot.Theme, Icon = "@mipmap/ic_launcher", Exported = false, LaunchMode = LaunchMode.SingleTask,
    WindowSoftInputMode = SoftInput.AdjustResize, ConfigurationChanges = ArmySlot.Changes, ScreenOrientation = ArmySlot.Orientation)]
public sealed class Army11Activity : MainActivity { }

[Service(Name = "com.arglax.movibeskua.Army11Service", Process = ":army11", Exported = false)]
public sealed class Army11Service : ArmySlotService { }

[Activity(Name = "com.arglax.movibeskua.Army12Activity", Label = "MVS 12", Process = ":army12", TaskAffinity = "com.arglax.movibeskua.army12",
    Theme = ArmySlot.Theme, Icon = "@mipmap/ic_launcher", Exported = false, LaunchMode = LaunchMode.SingleTask,
    WindowSoftInputMode = SoftInput.AdjustResize, ConfigurationChanges = ArmySlot.Changes, ScreenOrientation = ArmySlot.Orientation)]
public sealed class Army12Activity : MainActivity { }

[Service(Name = "com.arglax.movibeskua.Army12Service", Process = ":army12", Exported = false)]
public sealed class Army12Service : ArmySlotService { }
