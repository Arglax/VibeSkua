using System.Threading.Tasks;
using System.Threading;
using System.Collections.Generic;
using CommunityToolkit.Mvvm.DependencyInjection;
using Skua.Core.Interfaces;

namespace System.Windows.Forms;

public enum DialogResult
{
    None = 0,
    OK = 1,
    Cancel = 2,
    Abort = 3,
    Retry = 4,
    Ignore = 5,
    Yes = 6,
    No = 7,
    TryAgain = 10,
    Continue = 11
}

public enum MessageBoxButtons
{
    OK = 0,
    OKCancel = 1,
    AbortRetryIgnore = 2,
    YesNoCancel = 3,
    YesNo = 4,
    RetryCancel = 5,
    CancelTryContinue = 6
}

public enum MessageBoxIcon
{
    None = 0,
    Hand = 16,
    Stop = 16,
    Error = 16,
    Question = 32,
    Exclamation = 48,
    Warning = 48,
    Asterisk = 64,
    Information = 64
}

public enum MessageBoxDefaultButton
{
    Button1 = 0,
    Button2 = 256,
    Button3 = 512,
    Button4 = 768
}

public enum MessageBoxOptions
{
    DefaultDesktopOnly = 131072,
    RightAlign = 524288,
    RtlReading = 1048576,
    ServiceNotification = 2097152
}

public interface IWin32Window
{
    IntPtr Handle { get; }
}

/// <summary>
/// Shows Skua dialogs in place of Win32 message boxes.
/// </summary>
public static class MessageBox
{
    public static DialogResult Show(string? text) => Show(text, string.Empty);

    public static DialogResult Show(string? text, string? caption) => Show(text, caption, MessageBoxButtons.OK);

    public static DialogResult Show(string? text, string? caption, MessageBoxButtons buttons) => ShowCore(text, caption, buttons);

    public static DialogResult Show(string? text, string? caption, MessageBoxButtons buttons, MessageBoxIcon icon) => ShowCore(text, caption, buttons);

    public static DialogResult Show(string? text, string? caption, MessageBoxButtons buttons, MessageBoxIcon icon, MessageBoxDefaultButton defaultButton) => ShowCore(text, caption, buttons);

    public static DialogResult Show(string? text, string? caption, MessageBoxButtons buttons, MessageBoxIcon icon, MessageBoxDefaultButton defaultButton, MessageBoxOptions options) => ShowCore(text, caption, buttons);

    public static DialogResult Show(IWin32Window? owner, string? text) => Show(text);

    public static DialogResult Show(IWin32Window? owner, string? text, string? caption) => Show(text, caption);

    public static DialogResult Show(IWin32Window? owner, string? text, string? caption, MessageBoxButtons buttons) => ShowCore(text, caption, buttons);

    public static DialogResult Show(IWin32Window? owner, string? text, string? caption, MessageBoxButtons buttons, MessageBoxIcon icon) => ShowCore(text, caption, buttons);

    private static DialogResult ShowCore(string? text, string? caption, MessageBoxButtons buttons)
    {
        IDialogService? dialogs = Ioc.Default.GetService<IDialogService>();
        if (dialogs is null)
            return DialogResult.None;

        (string Label, DialogResult Result)[] options = buttons switch
        {
            MessageBoxButtons.OKCancel => new[] { ("OK", DialogResult.OK), ("Cancel", DialogResult.Cancel) },
            MessageBoxButtons.AbortRetryIgnore => new[] { ("Abort", DialogResult.Abort), ("Retry", DialogResult.Retry), ("Ignore", DialogResult.Ignore) },
            MessageBoxButtons.YesNoCancel => new[] { ("Yes", DialogResult.Yes), ("No", DialogResult.No), ("Cancel", DialogResult.Cancel) },
            MessageBoxButtons.YesNo => new[] { ("Yes", DialogResult.Yes), ("No", DialogResult.No) },
            MessageBoxButtons.RetryCancel => new[] { ("Retry", DialogResult.Retry), ("Cancel", DialogResult.Cancel) },
            MessageBoxButtons.CancelTryContinue => new[] { ("Cancel", DialogResult.Cancel), ("Try Again", DialogResult.TryAgain), ("Continue", DialogResult.Continue) },
            _ => new[] { ("OK", DialogResult.OK) }
        };

        Skua.Core.Models.DialogResult result = dialogs.ShowMessageBox(text ?? string.Empty, caption ?? string.Empty, options.Select(o => o.Label).ToArray());
        foreach ((string label, DialogResult value) in options)
        {
            if (string.Equals(result.Text, label, StringComparison.OrdinalIgnoreCase))
                return value;
        }
        return buttons == MessageBoxButtons.OK ? DialogResult.OK : DialogResult.Cancel;
    }
}

public static class Clipboard
{
    public static void SetText(string text) => Ioc.Default.GetService<IClipboardService>()?.SetText(text);

    public static string GetText() => Ioc.Default.GetService<IClipboardService>()?.GetText() ?? string.Empty;

    public static bool ContainsText() => !string.IsNullOrEmpty(GetText());

    public static void Clear() => SetText(string.Empty);
}

public static class Application
{
    public static void DoEvents()
    {
    }

    public static string StartupPath => AppContext.BaseDirectory;

    /// <summary>
    /// There is no window to show on Android: raise the form's Shown event and wait until it is closed.
    /// </summary>
    public static void Run(Form form)
    {
        form.RunHeadless();
    }
}

public delegate void MethodInvoker();

public enum DockStyle
{
    None,
    Top,
    Bottom,
    Left,
    Right,
    Fill
}

public enum FormStartPosition
{
    Manual,
    CenterScreen,
    WindowsDefaultLocation,
    WindowsDefaultBounds,
    CenterParent
}

public enum FormBorderStyle
{
    None,
    FixedSingle,
    Fixed3D,
    FixedDialog,
    Sizable,
    FixedToolWindow,
    SizableToolWindow
}

public enum ProgressBarStyle
{
    Blocks,
    Continuous,
    Marquee
}

public class PaintEventArgs : EventArgs
{
}

public delegate void PaintEventHandler(object? sender, PaintEventArgs e);

/// <summary>
/// Invisible stand-in for WinForms controls so scripts that build little windows still compile and run.
/// </summary>
public class Control : IWin32Window, IDisposable
{
    public string Text { get; set; } = string.Empty;
    public System.Drawing.Size Size { get; set; }
    public System.Drawing.Point Location { get; set; }
    public int Width { get => Size.Width; set => Size = new(value, Size.Height); }
    public int Height { get => Size.Height; set => Size = new(Size.Width, value); }
    public DockStyle Dock { get; set; }
    public System.Drawing.Color ForeColor { get; set; }
    public System.Drawing.Color BackColor { get; set; }
    public bool Visible { get; set; } = true;
    public bool Enabled { get; set; } = true;
    public object? Tag { get; set; }
    public List<Control> Controls { get; } = new();
    public bool IsDisposed { get; private set; }
    public bool InvokeRequired => false;
    public IntPtr Handle => IntPtr.Zero;

    public event PaintEventHandler? Paint;
    public event EventHandler? Click;

    public object? Invoke(Delegate method) => method.DynamicInvoke();

    public object? Invoke(Delegate method, params object?[] args) => method.DynamicInvoke(args);

    public IAsyncResult BeginInvoke(Delegate method) => Task.Run(() => method.DynamicInvoke());

    public void Refresh() => Paint?.Invoke(this, new PaintEventArgs());

    public void Invalidate() => Refresh();

    public void PerformClick() => Click?.Invoke(this, EventArgs.Empty);

    public virtual void Dispose()
    {
        IsDisposed = true;
        GC.SuppressFinalize(this);
    }
}

public class ProgressBar : Control
{
    public int Minimum { get; set; }
    public int Maximum { get; set; } = 100;
    public int Value { get; set; }
    public int Step { get; set; } = 10;
    public ProgressBarStyle Style { get; set; }

    public void PerformStep() => Value = Math.Min(Maximum, Value + Step);
}

public class Label : Control
{
    public bool AutoSize { get; set; }
}

public class Button : Control
{
    public DialogResult DialogResult { get; set; }
}

public class Form : Control
{
    private readonly ManualResetEventSlim _closed = new(false);

    public FormStartPosition StartPosition { get; set; }
    public FormBorderStyle FormBorderStyle { get; set; }
    public bool MaximizeBox { get; set; }
    public bool MinimizeBox { get; set; }
    public bool TopMost { get; set; }
    public bool ShowInTaskbar { get; set; }
    public DialogResult DialogResult { get; set; }

    public event EventHandler? Shown;
    public event EventHandler? Load;
    public event EventHandler? FormClosed;

    public void Show()
    {
        Load?.Invoke(this, EventArgs.Empty);
        Shown?.Invoke(this, EventArgs.Empty);
    }

    public DialogResult ShowDialog()
    {
        RunHeadless();
        return DialogResult;
    }

    public DialogResult ShowDialog(IWin32Window? owner) => ShowDialog();

    public void Close()
    {
        if (_closed.IsSet)
            return;
        _closed.Set();
        FormClosed?.Invoke(this, EventArgs.Empty);
    }

    public void Hide() => Visible = false;

    public void Activate()
    {
    }

    public void BringToFront()
    {
    }

    internal void RunHeadless()
    {
        Show();
        // Forms normally close themselves from Shown or a timer; don't hang a script thread forever.
        _closed.Wait(TimeSpan.FromMinutes(5));
    }

    public override void Dispose()
    {
        Close();
        base.Dispose();
    }
}
