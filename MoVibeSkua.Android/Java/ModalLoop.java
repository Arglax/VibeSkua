package com.arglax.movibeskua;

import android.os.Handler;
import android.os.Looper;

/**
 * Runs a nested main-looper loop so a synchronous dialog can wait on the UI thread, the way the
 * desktop client nests the Avalonia dispatcher. quit() posts a message that unwinds the innermost loop.
 */
public final class ModalLoop {
    private static final class Quit extends RuntimeException {
        Quit() {
            super(null, null, false, false);
        }
    }

    private ModalLoop() {
    }

    public static void run() {
        try {
            Looper.loop();
        } catch (Quit ignored) {
        }
    }

    public static void quit() {
        new Handler(Looper.getMainLooper()).postAtFrontOfQueue(new Runnable() {
            @Override
            public void run() {
                throw new Quit();
            }
        });
    }
}
