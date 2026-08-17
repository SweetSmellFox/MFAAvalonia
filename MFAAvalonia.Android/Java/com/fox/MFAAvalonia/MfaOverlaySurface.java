package com.fox.MFAAvalonia;

import android.os.Build;
import android.util.Log;
import android.view.SurfaceControl;
import android.view.View;

import java.lang.reflect.Method;

/** Applies the SurfaceFlinger skip-screenshot bit to MFA's owned overlay surface. */
public final class MfaOverlaySurface {
    private static final String TAG = "MfaCurrentScreenOverlay";

    private MfaOverlaySurface() {
    }

    public static boolean excludeFromScreenshots(View view) {
        if (view == null || Build.VERSION.SDK_INT < 31)
            return false;
        try {
            Method getViewRoot = View.class.getDeclaredMethod("getViewRootImpl");
            getViewRoot.setAccessible(true);
            Object viewRoot = getViewRoot.invoke(view);
            if (viewRoot == null)
                return false;

            Method getSurfaceControl = viewRoot.getClass().getMethod("getSurfaceControl");
            Object surface = getSurfaceControl.invoke(viewRoot);
            if (!(surface instanceof SurfaceControl) || !((SurfaceControl) surface).isValid())
                return false;

            SurfaceControl.Transaction transaction = new SurfaceControl.Transaction();
            try {
                Method setSkipScreenshot = SurfaceControl.Transaction.class.getMethod(
                        "setSkipScreenshot", SurfaceControl.class, boolean.class);
                setSkipScreenshot.invoke(transaction, surface, true);
                transaction.apply();
            } finally {
                transaction.close();
            }
            Log.i(TAG, "Overlay surface excluded from screenshots.");
            return true;
        } catch (Throwable exception) {
            Log.w(TAG, "Unable to exclude overlay surface from screenshots", exception);
            return false;
        }
    }
}
