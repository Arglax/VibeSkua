using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Android.Runtime;
using Avalonia.Controls;
using Avalonia.Threading;
using MoVibeSkua.Android.Views;

namespace MoVibeSkua.Android.Platform;

/// <summary>
/// Shows a card on the shell's dialog layer and waits for it to close. Off the UI thread the caller
/// simply blocks; on the UI thread a nested Looper keeps the app responsive while it waits.
/// </summary>
internal static class Modal
{
    private static IntPtr _loopClass;
    private static IntPtr _runMethod;
    private static IntPtr _quitMethod;

    public static T Run<T>(Func<Action<T>, Control> build, T fallback)
    {
        if (Dispatcher.UIThread.CheckAccess())
        {
            MobileShellView? shell = MobileShellView.Instance;
            if (shell is null)
                return fallback;

            T result = fallback;
            bool closed = false;
            Control? card = null;
            card = build(value =>
            {
                if (closed)
                    return;
                closed = true;
                result = value;
                shell.RemoveDialog(card!);
                QuitLoop();
            });
            shell.PushDialog(card);
            if (!closed)
                RunLoop();
            return result;
        }

        TaskCompletionSource<T> tcs = new(TaskCreationOptions.RunContinuationsAsynchronously);
        Dispatcher.UIThread.Post(() =>
        {
            MobileShellView? shell = MobileShellView.Instance;
            if (shell is null)
            {
                tcs.TrySetResult(fallback);
                return;
            }

            bool closed = false;
            Control? card = null;
            card = build(value =>
            {
                if (closed)
                    return;
                closed = true;
                shell.RemoveDialog(card!);
                tcs.TrySetResult(value);
            });
            shell.PushDialog(card);
        });
        return tcs.Task.GetAwaiter().GetResult();
    }

    /// <summary>
    /// Shows a card without waiting for it; for messages nobody needs an answer to.
    /// </summary>
    public static void Show(Func<Action<string>, Control> build)
    {
        Dispatcher.UIThread.Post(() =>
        {
            MobileShellView? shell = MobileShellView.Instance;
            if (shell is null)
                return;
            Control? card = null;
            card = build(_ =>
            {
                if (card is not null)
                    shell.RemoveDialog(card);
            });
            shell.PushDialog(card);
        });
    }

    /// <summary>
    /// Waits for UI-thread async work (pickers and the like) from synchronous code.
    /// </summary>
    public static T? Await<T>(Func<Task<T?>> work)
    {
        if (!Dispatcher.UIThread.CheckAccess())
            return Dispatcher.UIThread.InvokeAsync(work).GetAwaiter().GetResult();

        Task<T?> task = work();
        if (!task.IsCompleted)
        {
            task.ContinueWith(_ => Dispatcher.UIThread.Post(QuitLoop), TaskScheduler.Default);
            RunLoop();
        }
        return task.IsCompletedSuccessfully ? task.Result : default;
    }

    private static void EnsureJni()
    {
        if (_loopClass != IntPtr.Zero)
            return;
        _loopClass = JNIEnv.FindClass("com/arglax/movibeskua/ModalLoop");
        _runMethod = JNIEnv.GetStaticMethodID(_loopClass, "run", "()V");
        _quitMethod = JNIEnv.GetStaticMethodID(_loopClass, "quit", "()V");
    }

    private static void RunLoop()
    {
        EnsureJni();
        JNIEnv.CallStaticVoidMethod(_loopClass, _runMethod);
    }

    private static void QuitLoop()
    {
        EnsureJni();
        JNIEnv.CallStaticVoidMethod(_loopClass, _quitMethod);
    }
}
