package com.fox.MFAAvalonia;

import android.app.ActivityManager;
import android.app.ActivityOptions;
import android.content.AttributionSource;
import android.content.ComponentName;
import android.content.Context;
import android.content.ContextWrapper;
import android.content.Intent;
import android.content.pm.PackageManager;
import android.hardware.display.DisplayManager;
import android.hardware.display.VirtualDisplay;
import android.os.Binder;
import android.os.Build;
import android.os.Bundle;
import android.os.IBinder;
import android.os.IInterface;
import android.os.Parcel;
import android.os.Process;
import android.os.RemoteException;
import android.os.SystemClock;
import android.system.ErrnoException;
import android.system.Os;
import android.system.OsConstants;
import android.util.Log;
import android.view.InputDevice;
import android.view.InputEvent;
import android.view.KeyEvent;
import android.view.MotionEvent;
import android.view.Surface;

import java.io.DataInputStream;
import java.io.DataOutputStream;
import java.io.IOException;
import java.io.InputStream;
import java.io.ByteArrayOutputStream;
import java.io.File;
import java.net.InetAddress;
import java.net.ServerSocket;
import java.net.Socket;
import java.nio.charset.StandardCharsets;
import java.lang.reflect.Constructor;
import java.lang.reflect.Method;
import java.util.List;
import java.util.concurrent.Executors;
import java.util.concurrent.ScheduledExecutorService;
import java.util.concurrent.ScheduledFuture;
import java.util.concurrent.TimeUnit;
import java.util.regex.Matcher;
import java.util.regex.Pattern;

public final class MfaShizukuUserService extends Binder {
    private static final String TAG = "MFAAvalonia";
    private static final int HEALTH_TRANSACTION = 1;
    private static final int CREATE_DISPLAY_TRANSACTION = 2;
    private static final int RELEASE_DISPLAY_TRANSACTION = 3;
    private static final int START_APP_TRANSACTION = 4;
    private static final int DESTROY_TRANSACTION = 16777115;

    private final Context context;
    private final InputServer inputServer;
    private VirtualDisplay virtualDisplay;
    private Surface virtualDisplaySurface;
    private volatile int clientPid = -1;
    private volatile boolean userActivityKeepAlive;
    private Thread userActivityThread;

    public MfaShizukuUserService(Context context) throws IOException {
        ensureShellIdentity();
        this.context = context;
        inputServer = new InputServer(context);
        inputServer.start();
        startClientWatchdog();
        Log.i(TAG, "Shizuku UserService started, uid=" + Process.myUid()
                + ", port=" + inputServer.getPort());
    }

    private static void ensureShellIdentity() throws IOException {
        int uid = Process.myUid();
        if (uid == Process.SHELL_UID)
            return;
        if (uid != Process.ROOT_UID) {
            throw new IOException("Shizuku UserService must run as shell or root, actual uid="
                    + uid + ".");
        }

        try {
            // Root-mode Shizuku starts UserService as uid 0. Android's display,
            // input and activity services validate that com.android.shell belongs
            // to the Binder calling uid, so retaining root makes every call fail
            // with "packageName must match the calling uid". This controller only
            // needs shell capabilities; drop gid before uid while still privileged.
            Os.setgid(Process.SHELL_UID);
            Os.setuid(Process.SHELL_UID);
        } catch (ErrnoException exception) {
            throw new IOException("Unable to drop root Shizuku UserService to shell uid.",
                    exception);
        }

        if (Process.myUid() != Process.SHELL_UID) {
            throw new IOException("Shizuku UserService identity drop did not take effect; uid="
                    + Process.myUid() + ".");
        }
        Log.i(TAG, "Root-mode Shizuku UserService dropped to shell uid="
                + Process.myUid());
    }

    @Override
    protected boolean onTransact(int code, Parcel data, Parcel reply, int flags)
            throws RemoteException {
        if (code == HEALTH_TRANSACTION) {
            if (data != null && data.dataAvail() >= 4) {
                clientPid = data.readInt();
            }
            if (reply != null) {
                reply.writeInt(Process.myUid());
                reply.writeInt(inputServer.getPort());
            }
            return true;
        }
        if (code == CREATE_DISPLAY_TRANSACTION) {
            int width = data.readInt();
            int height = data.readInt();
            int dpi = data.readInt();
            Surface surface = Surface.CREATOR.createFromParcel(data);
            DisplayCreationResult result = createVirtualDisplay(width, height, dpi, surface);
            if (reply != null) {
                reply.writeInt(result.displayId);
                reply.writeString(result.error);
                reply.writeInt(result.flags);
            }
            return true;
        }
        if (code == RELEASE_DISPLAY_TRANSACTION) {
            releaseVirtualDisplay();
            return true;
        }
        if (code == START_APP_TRANSACTION) {
            int displayId = data.readInt();
            boolean forceStop = data.readInt() != 0;
            String target = data.readString();
            int result = inputServer.startApp(
                    displayId, target == null ? "" : target, forceStop);
            if (reply != null) {
                reply.writeInt(result);
            }
            return true;
        }
        if (code == DESTROY_TRANSACTION) {
            Log.i(TAG, "Destroy transaction received.");
            releaseVirtualDisplay();
            inputServer.shutdown();
            System.exit(0);
            return true;
        }
        return super.onTransact(code, data, reply, flags);
    }

    private synchronized DisplayCreationResult createVirtualDisplay(
            int width, int height, int dpi, Surface surface) {
        releaseVirtualDisplay();
        String lastError = "No compatible virtual display configuration was accepted.";
        int lastFlags = 0;
        try {
            Context shellBaseContext;
            try {
                shellBaseContext = context.createPackageContext(
                        "com.android.shell", Context.CONTEXT_IGNORE_SECURITY);
            } catch (Throwable exception) {
                Log.w(TAG, "Shell package context is unavailable; using UserService context", exception);
                shellBaseContext = context;
            }
            Context shellContext = new ShellContext(shellBaseContext);
            int basicFlags = DisplayManager.VIRTUAL_DISPLAY_FLAG_PUBLIC
                    | DisplayManager.VIRTUAL_DISPLAY_FLAG_PRESENTATION
                    | DisplayManager.VIRTUAL_DISPLAY_FLAG_OWN_CONTENT_ONLY
                    | (1 << 6); // SUPPORTS_TOUCH
            int destroyFlags = basicFlags | (1 << 8); // DESTROY_CONTENT_ON_REMOVAL
            int android13Flags = destroyFlags;
            if (Build.VERSION.SDK_INT >= 33) {
                android13Flags |= (1 << 10)  // TRUSTED
                        | (1 << 11) // OWN_DISPLAY_GROUP
                        | (1 << 12) // ALWAYS_UNLOCKED
                        | (1 << 13); // TOUCH_FEEDBACK_DISABLED
            }
            int fullFlags = android13Flags;
            if (Build.VERSION.SDK_INT >= 34) {
                fullFlags |= (1 << 14)  // OWN_FOCUS
                        | (1 << 15) // DEVICE_DISPLAY_GROUP
                        | (1 << 16); // STEAL_TOP_FOCUS_DISABLED
            }
            int focusFlags = destroyFlags;
            if (Build.VERSION.SDK_INT >= 34) {
                focusFlags |= (1 << 14) // OWN_FOCUS
                        | (1 << 15) // DEVICE_DISPLAY_GROUP
                        | (1 << 16); // STEAL_TOP_FOCUS_DISABLED
            }

            // OEM DisplayManager implementations differ in which privileged flags
            // they accept for a Shizuku shell UserService. Start with the complete
            // MAA-Meow set, then progressively remove only the sensitive flags.
            int[] flagCandidates = new int[] {
                    fullFlags,
                    focusFlags,
                    android13Flags,
                    destroyFlags,
                    basicFlags
            };
            Context[] contextCandidates = shellContext == context
                    ? new Context[] { context }
                    : new Context[] { shellContext, context };
            int previousFlags = Integer.MIN_VALUE;
            for (Context candidateContext : contextCandidates) {
                DisplayManager displayManager = createDisplayManager(candidateContext);
                if (displayManager == null) {
                    lastError = "DisplayManager is unavailable for "
                            + candidateContext.getPackageName() + ".";
                    continue;
                }
                previousFlags = Integer.MIN_VALUE;
                for (int candidateFlags : flagCandidates) {
                    if (candidateFlags == previousFlags)
                        continue;
                    previousFlags = candidateFlags;
                    lastFlags = candidateFlags;
                    try {
                        Log.i(TAG, "Creating Shizuku virtual display: context="
                                + candidateContext.getPackageName() + ", flags=0x"
                                + Integer.toHexString(candidateFlags));
                        VirtualDisplay display = displayManager.createVirtualDisplay(
                                "MFA_VIRTUAL_DISPLAY", width, height, dpi, surface,
                                candidateFlags);
                        if (display == null || display.getDisplay() == null) {
                            if (display != null)
                                display.release();
                            lastError = "DisplayManager returned an empty VirtualDisplay"
                                    + " (context=" + candidateContext.getPackageName()
                                    + ", flags=0x" + Integer.toHexString(candidateFlags) + ").";
                            Log.w(TAG, lastError);
                            continue;
                        }

                        virtualDisplay = display;
                        virtualDisplaySurface = surface;
                        int displayId = display.getDisplay().getDisplayId();
                        startUserActivityKeepAlive(displayId);
                        Log.i(TAG, "Shizuku virtual display created: " + width + "x" + height
                                + ", dpi=" + dpi + ", display=" + displayId
                                + ", context=" + candidateContext.getPackageName()
                                + ", flags=0x" + Integer.toHexString(candidateFlags));
                        return new DisplayCreationResult(displayId, null, candidateFlags);
                    } catch (Throwable exception) {
                        lastError = describeException(exception)
                                + " (context=" + candidateContext.getPackageName()
                                + ", flags=0x" + Integer.toHexString(candidateFlags) + ")";
                        Log.w(TAG, "Virtual display attempt failed: " + lastError, exception);
                    }
                }
            }
        } catch (Throwable exception) {
            lastError = describeException(exception);
            Log.e(TAG, "Shizuku virtual display setup failed", exception);
        }
        surface.release();
        return new DisplayCreationResult(-1, lastError, lastFlags);
    }

    private static String describeException(Throwable exception) {
        Throwable current = exception;
        while (current.getCause() != null && current.getCause() != current)
            current = current.getCause();
        String message = current.getMessage();
        return current.getClass().getName()
                + (message == null || message.isEmpty() ? "" : ": " + message);
    }

    private static DisplayManager createDisplayManager(Context displayContext) {
        try {
            // Match MAA-Meow: force DisplayManager to retain the shell-attributed
            // context instead of accepting a manager cached by an app context.
            Constructor<DisplayManager> constructor = DisplayManager.class
                    .getDeclaredConstructor(Context.class);
            constructor.setAccessible(true);
            return constructor.newInstance(displayContext);
        } catch (Throwable exception) {
            Log.w(TAG, "Hidden DisplayManager(Context) is unavailable; using system service",
                    exception);
            return (DisplayManager) displayContext.getSystemService(Context.DISPLAY_SERVICE);
        }
    }

    private static final class ShellContext extends ContextWrapper {
        ShellContext(Context base) {
            super(base);
        }

        @Override
        public String getPackageName() {
            return "com.android.shell";
        }

        @Override
        public String getOpPackageName() {
            return "com.android.shell";
        }

        @Override
        public Context getApplicationContext() {
            return this;
        }

        @Override
        public int checkCallingPermission(String permission) {
            return PackageManager.PERMISSION_GRANTED;
        }

        @Override
        public AttributionSource getAttributionSource() {
            if (Build.VERSION.SDK_INT >= 31) {
                return new AttributionSource.Builder(Process.SHELL_UID)
                        .setPackageName("com.android.shell")
                        .build();
            }
            return super.getAttributionSource();
        }

        @SuppressWarnings("unused")
        public int getDeviceId() {
            return 0;
        }
    }

    private static final class DisplayCreationResult {
        final int displayId;
        final String error;
        final int flags;

        DisplayCreationResult(int displayId, String error, int flags) {
            this.displayId = displayId;
            this.error = error;
            this.flags = flags;
        }
    }

    private synchronized void releaseVirtualDisplay() {
        stopUserActivityKeepAlive();
        if (virtualDisplay != null) {
            virtualDisplay.release();
            virtualDisplay = null;
        }
        if (virtualDisplaySurface != null) {
            virtualDisplaySurface.release();
            virtualDisplaySurface = null;
        }
    }

    private void startClientWatchdog() {
        Thread watchdog = new Thread(() -> {
            while (true) {
                try {
                    Thread.sleep(5000);
                } catch (InterruptedException exception) {
                    Thread.currentThread().interrupt();
                    return;
                }
                int pid = clientPid;
                if (pid > 0 && !isProcessAlive(pid)) {
                    Log.i(TAG, "MFA process " + pid + " exited; stopping UserService.");
                    releaseVirtualDisplay();
                    inputServer.shutdown();
                    System.exit(0);
                    return;
                }
            }
        }, "MfaClientWatchdog");
        watchdog.setDaemon(true);
        watchdog.start();
    }

    private static boolean isProcessAlive(int pid) {
        if (new File("/proc/" + pid).exists())
            return true;
        try {
            // Signal 0 performs existence/permission checking without delivering a
            // signal. After dropping a root Shizuku service to shell, MuMu SELinux
            // hides app /proc entries and returns EPERM here even while the app is
            // alive. Only ESRCH is proof that the process is gone.
            Os.kill(pid, 0);
            return true;
        } catch (ErrnoException exception) {
            return exception.errno != OsConstants.ESRCH;
        }
    }

    private synchronized void startUserActivityKeepAlive(final int displayId) {
        stopUserActivityKeepAlive();
        userActivityKeepAlive = true;
        userActivityThread = new Thread(() -> {
            Log.i(TAG, "Starting display userActivity keep-alive for display " + displayId);
            while (userActivityKeepAlive) {
                try {
                    Thread.sleep(4000L);
                } catch (InterruptedException exception) {
                    Thread.currentThread().interrupt();
                    break;
                }
                if (userActivityKeepAlive)
                    sendUserActivity(displayId);
            }
            Log.i(TAG, "Stopped display userActivity keep-alive for display " + displayId);
        }, "mfa-display-user-activity");
        userActivityThread.setDaemon(true);
        userActivityThread.start();
    }

    private synchronized void stopUserActivityKeepAlive() {
        userActivityKeepAlive = false;
        if (userActivityThread != null) {
            userActivityThread.interrupt();
            userActivityThread = null;
        }
    }

    private void sendUserActivity(int displayId) {
        try {
            Class<?> serviceManager = Class.forName("android.os.ServiceManager");
            IBinder binder = (IBinder) serviceManager
                    .getDeclaredMethod("getService", String.class)
                    .invoke(null, "power");
            if (binder == null)
                return;
            Class<?> stub = Class.forName("android.os.IPowerManager$Stub");
            IInterface manager = (IInterface) stub.getMethod("asInterface", IBinder.class)
                    .invoke(null, binder);
            if (manager == null)
                return;
            long now = SystemClock.uptimeMillis();
            try {
                Method userActivity = manager.getClass().getMethod(
                        "userActivity", int.class, long.class, int.class, int.class);
                userActivity.invoke(manager, displayId, now, 0, 0);
            } catch (NoSuchMethodException ignored) {
                Method userActivity = manager.getClass().getMethod(
                        "userActivity", long.class, int.class, int.class);
                userActivity.invoke(manager, now, 0, 0);
            }
        } catch (Throwable exception) {
            Log.d(TAG, "Display userActivity keep-alive failed", exception);
        }
    }

    private static final class InputServer extends Thread {
        private static final int MAGIC = 0x4d464142;
        private static final Pattern DISPLAY_HEADER = Pattern.compile(
                "^\\s*Display\\s+#?(\\d+)(?:\\s|\\(|:|$)", Pattern.CASE_INSENSITIVE);
        private static final Pattern DISPLAY_ID = Pattern.compile(
                "(?:displayId|mDisplayId|display-id)\\s*[=:]\\s*(\\d+)", Pattern.CASE_INSENSITIVE);
        private final Context context;
        private final ServerSocket server;
        private final InputController inputController;
        private final ScheduledExecutorService focusExecutor;
        private ScheduledFuture<?> pendingFocusRestore;

        InputServer(Context context) throws IOException {
            super("MfaBridgeInput");
            this.context = context;
            inputController = new InputController(new ShellContext(context));
            focusExecutor = Executors.newSingleThreadScheduledExecutor(task -> {
                Thread thread = new Thread(task, "mfa-display-focus");
                thread.setDaemon(true);
                return thread;
            });
            server = new ServerSocket(0, 4, InetAddress.getByName("127.0.0.1"));
            setDaemon(true);
        }

        int getPort() {
            return server.getLocalPort();
        }

        void shutdown() {
            focusExecutor.shutdownNow();
            try {
                server.close();
            } catch (IOException exception) {
                Log.w(TAG, "Input server close failed", exception);
            }
        }

        @Override
        public void run() {
            while (!server.isClosed()) {
                try (Socket socket = server.accept();
                     DataInputStream input = new DataInputStream(socket.getInputStream());
                     DataOutputStream output = new DataOutputStream(socket.getOutputStream())) {
                    // Keep one bridge connection alive for an entire gesture. The native
                    // side reuses this socket, avoiding a TCP handshake for every MOVE.
                    socket.setTcpNoDelay(true);
                    while (!server.isClosed()) {
                    if (input.readInt() != MAGIC) {
                        break;
                    }

                    int displayId = input.readInt();
                    int method = input.readInt();
                    int x = input.readInt();
                    int y = input.readInt();
                    int key = input.readInt();
                    int textLength = input.readInt();
                    String text = "";
                    if (textLength > 0 && textLength <= 4096) {
                        byte[] bytes = new byte[textLength];
                        input.readFully(bytes);
                        text = new String(bytes, StandardCharsets.UTF_8);
                    }
                    int result = execute(displayId, method, x, y, key, text);
                    output.writeInt(result);
                    output.flush();
                    }
                } catch (IOException exception) {
                    if (!server.isClosed()) {
                        Log.w(TAG, "Input server error", exception);
                    }
                }
            }
        }

        private int execute(int displayId, int method, int x, int y, int key, String text) {
            String display = displayId >= 0 ? "-d " + displayId + " " : "";
            switch (method) {
                case 1:
                    return startApp(displayId, text, key != 0);
                case 2:
                    return forceStopPackage(text) ? 0 : -1;
                case 4:
                    // MAA-Meow does not implement Maa input-text either. Keep the
                    // Android shell route for now and report its real exit status.
                    return runShell("input " + display + "text " + shell(text)).exitCode;
                case 6:
                    if (inputController.down(x, y, displayId))
                        return 0;
                    return runShell("input " + display + "motionevent DOWN " + x + " " + y).exitCode;
                case 7:
                    if (inputController.hasActiveGesture())
                        return inputController.move(x, y, displayId) ? 0 : -4;
                    return runShell("input " + display + "motionevent MOVE " + x + " " + y).exitCode;
                case 8:
                    boolean released;
                    if (inputController.hasActiveGesture())
                        released = inputController.up(x, y, displayId);
                    else
                        released = runShell("input " + display + "motionevent UP " + x + " " + y).exitCode == 0;
                    if (released && displayId != 0)
                        scheduleClientFocusRestore();
                    return released ? 0 : -4;
                case 9:
                    return inputController.key(key, KeyEvent.ACTION_DOWN, displayId) ? 0 : -4;
                case 10:
                    return inputController.key(key, KeyEvent.ACTION_UP, displayId) ? 0 : -4;
                default:
                    return -2;
            }
        }

        private synchronized void scheduleClientFocusRestore() {
            if (pendingFocusRestore != null)
                pendingFocusRestore.cancel(false);
            // Input dispatch focuses the virtual-display task after ACTION_UP. Restore
            // the UI task shortly afterwards so emulator tab managers (notably MuMu)
            // keep presenting MFA, while the game task itself remains on the virtual
            // display. This deliberately does not move either task between displays.
            pendingFocusRestore = focusExecutor.schedule(
                    this::restoreClientTaskFocus, 80, TimeUnit.MILLISECONDS);
        }

        private void restoreClientTaskFocus() {
            try {
                TaskPlacement client = findPackageTask(context.getPackageName());
                if (client == null || client.taskId < 0 || client.displayId != 0) {
                    Log.w(TAG, "Cannot restore MFA focus: client task is unavailable or not on display 0.");
                    return;
                }

                Class<?> serviceManager = Class.forName("android.os.ServiceManager");
                IBinder binder = (IBinder) serviceManager
                        .getDeclaredMethod("getService", String.class)
                        .invoke(null, "activity_task");
                Class<?> stub = Class.forName("android.app.IActivityTaskManager$Stub");
                Object manager = stub.getMethod("asInterface", IBinder.class).invoke(null, binder);
                Method setFocusedTask = manager.getClass().getMethod("setFocusedTask", int.class);
                setFocusedTask.invoke(manager, client.taskId);
                Log.d(TAG, "Restored MFA task focus after virtual-display input: task="
                        + client.taskId);
            } catch (Throwable exception) {
                Log.w(TAG, "Unable to restore MFA task focus after virtual-display input", exception);
            }
        }

        private int startApp(int displayId, String target, boolean forceStop) {
            if (displayId < 0) {
                Log.w(TAG, "StartApp rejected because no virtual display is available.");
                return -3;
            }

            Intent intent = buildLaunchIntent(target);
            ComponentName componentName = intent == null ? null : intent.getComponent();
            if (intent == null || componentName == null) {
                Log.w(TAG, "StartApp could not resolve a launcher activity for " + target
                        + ".");
                return 2;
            }

            String packageName = componentName.getPackageName();
            if (forceStop && !forceStopPackage(packageName))
                return -1;

            intent.addFlags(Intent.FLAG_ACTIVITY_NEW_TASK | Intent.FLAG_ACTIVITY_EXCLUDE_FROM_RECENTS);
            boolean started = startActivityAsShell(intent, displayId, false);
            if (!started) {
                ShellResult fallback = runShell("am start -W --display " + displayId
                        + " -n " + shell(componentName.flattenToShortString()));
                if (fallback.exitCode != 0)
                    return fallback.exitCode;
            }

            TaskPlacement placement = null;
            for (int attempt = 0; attempt < 20; attempt++) {
                placement = findPackageTask(packageName);
                if (placement != null) {
                    if (placement.displayId == displayId)
                        return 0;
                    Log.w(TAG, "StartApp placed " + packageName + " on display "
                            + placement.displayId + " instead of " + displayId
                            + "; attempting to move task " + placement.taskId + " back.");
                    if (repinTask(intent, placement.taskId, displayId))
                        return 0;
                    break;
                }
                try {
                    Thread.sleep(500);
                } catch (InterruptedException exception) {
                    Thread.currentThread().interrupt();
                    return -1;
                }
            }

            Log.e(TAG, "StartApp did not place " + packageName + " on virtual display "
                    + displayId + "; placement=" + (placement == null
                    ? "not-found"
                    : "display-" + placement.displayId) + ". Stopping the misplaced app.");
            forceStopPackage(packageName);
            return placement == null ? 4 : 3;
        }

        private boolean forceStopPackage(String packageName) {
            try {
                Class<?> activityManagerNative = Class.forName("android.app.ActivityManagerNative");
                IInterface manager = (IInterface) activityManagerNative
                        .getDeclaredMethod("getDefault")
                        .invoke(null);
                Method forceStop = manager.getClass().getMethod(
                        "forceStopPackage", String.class, int.class);
                forceStop.invoke(manager, packageName, -2);
                Log.i(TAG, "forceStopPackage succeeded for " + packageName);
                return true;
            } catch (Throwable exception) {
                Log.w(TAG, "Hidden forceStopPackage failed; using shell fallback", exception);
                return runShell("am force-stop " + shell(packageName)).exitCode == 0;
            }
        }

        private Intent buildLaunchIntent(String target) {
            if (target.contains("/")) {
                ComponentName component = ComponentName.unflattenFromString(target);
                return component == null ? null : new Intent(Intent.ACTION_MAIN).setComponent(component);
            }
            Intent intent = context.getPackageManager().getLaunchIntentForPackage(target);
            return intent != null ? intent : context.getPackageManager().getLeanbackLaunchIntentForPackage(target);
        }

        private boolean startActivityAsShell(Intent intent, int displayId, boolean forceFullscreen) {
            try {
                ActivityOptions options = ActivityOptions.makeBasic();
                options.setLaunchDisplayId(displayId);
                if (forceFullscreen) {
                    Method setWindowingMode = ActivityOptions.class.getDeclaredMethod(
                            "setLaunchWindowingMode", int.class);
                    setWindowingMode.setAccessible(true);
                    setWindowingMode.invoke(options, 1);
                }

                Class<?> activityManagerNative = Class.forName("android.app.ActivityManagerNative");
                IInterface manager = (IInterface) activityManagerNative
                        .getDeclaredMethod("getDefault")
                        .invoke(null);
                Class<?> applicationThread = Class.forName("android.app.IApplicationThread");
                Class<?> profilerInfo = Class.forName("android.app.ProfilerInfo");
                Method startActivity = manager.getClass().getMethod(
                        "startActivityAsUser",
                        applicationThread, String.class, Intent.class, String.class,
                        IBinder.class, String.class, int.class, int.class,
                        profilerInfo, Bundle.class, int.class);
                int result = (int) startActivity.invoke(
                        manager, null, "com.android.shell", intent, null, null, null,
                        0, 0, null, options.toBundle(), -2);
                Log.i(TAG, "startActivityAsUser result=" + result + ", display=" + displayId
                        + ", component=" + intent.getComponent());
                return result >= 0;
            } catch (Throwable exception) {
                Log.w(TAG, "startActivityAsUser failed", exception);
                return false;
            }
        }

        @SuppressWarnings("deprecation")
        private TaskPlacement findPackageTask(String packageName) {
            try {
                ActivityManager activityManager = context.getSystemService(ActivityManager.class);
                if (activityManager == null)
                    return findPackageTaskFromDump(packageName);
                List<ActivityManager.RunningTaskInfo> tasks = activityManager.getRunningTasks(100);
                for (ActivityManager.RunningTaskInfo task : tasks) {
                    ComponentName top = task.topActivity;
                    ComponentName base = task.baseActivity;
                    if ((top != null && packageName.equals(top.getPackageName()))
                            || (base != null && packageName.equals(base.getPackageName()))) {
                        return new TaskPlacement(task.id, readTaskDisplayId(task));
                    }
                }
            } catch (Throwable exception) {
                Log.w(TAG, "getRunningTasks failed; using dumpsys fallback", exception);
            }
            return findPackageTaskFromDump(packageName);
        }

        private int readTaskDisplayId(ActivityManager.RunningTaskInfo task) {
            try {
                Class<?> type = task.getClass();
                while (type != null) {
                    try {
                        java.lang.reflect.Field field = type.getDeclaredField("displayId");
                        field.setAccessible(true);
                        return field.getInt(task);
                    } catch (NoSuchFieldException ignored) {
                        type = type.getSuperclass();
                    }
                }
            } catch (Throwable exception) {
                Log.w(TAG, "RunningTaskInfo.displayId is unavailable", exception);
            }
            return -1;
        }

        private TaskPlacement findPackageTaskFromDump(String packageName) {
            ShellResult dump = runShell("dumpsys activity activities");
            if (dump.exitCode != 0)
                return null;
            String[] lines = dump.output.split("\\r?\\n");
            int currentDisplayId = -1;
            for (int index = 0; index < lines.length; index++) {
                Matcher header = DISPLAY_HEADER.matcher(lines[index]);
                if (header.find())
                    currentDisplayId = Integer.parseInt(header.group(1));
                if (!lines[index].contains(packageName))
                    continue;
                int displayId = findNearbyInt(lines, index, DISPLAY_ID, currentDisplayId);
                Matcher taskId = Pattern.compile("(?:taskId|Task|task)\\s*[#=]?\\s*(\\d+)")
                        .matcher(lines[index]);
                return new TaskPlacement(taskId.find() ? Integer.parseInt(taskId.group(1)) : -1, displayId);
            }
            return null;
        }

        private static int findNearbyInt(
                String[] lines, int center, Pattern pattern, int fallback) {
            int first = Math.max(0, center - 3);
            int last = Math.min(lines.length - 1, center + 3);
            for (int index = first; index <= last; index++) {
                Matcher matcher = pattern.matcher(lines[index]);
                if (matcher.find())
                    return Integer.parseInt(matcher.group(1));
            }
            return fallback;
        }

        private boolean repinTask(Intent intent, int taskId, int displayId) {
            if (taskId >= 0 && moveTaskToDisplay(taskId, displayId)) {
                sleepForTaskMove();
                TaskPlacement moved = findPackageTask(intent.getComponent().getPackageName());
                if (moved != null && moved.displayId == displayId)
                    return true;
            }
            if (startActivityAsShell(intent, displayId, true)) {
                sleepForTaskMove();
                TaskPlacement relaunched = findPackageTask(intent.getComponent().getPackageName());
                return relaunched != null && relaunched.displayId == displayId;
            }
            return false;
        }

        private boolean moveTaskToDisplay(int taskId, int displayId) {
            try {
                Class<?> serviceManager = Class.forName("android.os.ServiceManager");
                IBinder binder = (IBinder) serviceManager
                        .getDeclaredMethod("getService", String.class)
                        .invoke(null, "activity_task");
                Class<?> stub = Class.forName("android.app.IActivityTaskManager$Stub");
                Object manager = stub.getMethod("asInterface", IBinder.class).invoke(null, binder);
                for (String name : new String[] { "moveRootTaskToDisplay", "moveStackToDisplay" }) {
                    try {
                        Method move = manager.getClass().getMethod(name, int.class, int.class);
                        move.invoke(manager, taskId, displayId);
                        Log.i(TAG, name + "(" + taskId + ", " + displayId + ") succeeded.");
                        return true;
                    } catch (NoSuchMethodException ignored) {
                    }
                }
            } catch (Throwable exception) {
                Log.w(TAG, "Hidden task move API failed", exception);
            }
            ShellResult fallback = runShell(
                    "am display move-stack " + taskId + " " + displayId);
            return fallback.exitCode == 0;
        }

        private static void sleepForTaskMove() {
            try {
                Thread.sleep(1000);
            } catch (InterruptedException exception) {
                Thread.currentThread().interrupt();
            }
        }

        private static ShellResult runShell(String command) {
            Log.d(TAG, "shell input: " + command);
            try {
                java.lang.Process process = new ProcessBuilder("sh", "-c", command)
                        .redirectErrorStream(true)
                        .start();
                String commandOutput = readOutput(process.getInputStream());
                int exitCode = process.waitFor();
                Log.i(TAG, "Input command finished, exit=" + exitCode
                        + (commandOutput.isEmpty() ? "" : ", output=" + commandOutput));
                if (exitCode != 0) {
                    Log.w(TAG, "Input command exited with code " + exitCode);
                }
                return new ShellResult(exitCode, commandOutput);
            } catch (IOException | InterruptedException exception) {
                if (exception instanceof InterruptedException) {
                    Thread.currentThread().interrupt();
                }
                Log.w(TAG, "Input command failed", exception);
                return new ShellResult(-1, exception.getMessage() == null
                        ? exception.getClass().getSimpleName()
                        : exception.getMessage());
            }
        }

        private static final class ShellResult {
            final int exitCode;
            final String output;

            ShellResult(int exitCode, String output) {
                this.exitCode = exitCode;
                this.output = output;
            }
        }

        private static final class TaskPlacement {
            final int taskId;
            final int displayId;

            TaskPlacement(int taskId, int displayId) {
                this.taskId = taskId;
                this.displayId = displayId;
            }
        }

        private static final class InputController {
            private static final int INJECT_ASYNC = 0;
            private static final int INJECT_WAIT_FOR_FINISH = 2;
            private static final int DEVICE_ID = 0;
            private static final int SOURCE = InputDevice.SOURCE_TOUCHSCREEN;

            private final Object manager;
            private Method injectInputEvent;
            private Method setDisplayId;
            private long currentDownTime;
            private int currentDisplayId = -1;

            InputController(Context context) {
                manager = context.getSystemService(Context.INPUT_SERVICE);
            }

            synchronized boolean down(int x, int y, int displayId) {
                if (currentDownTime != 0) {
                    MotionEvent cancel = obtainMotionEvent(
                            currentDownTime, SystemClock.uptimeMillis(),
                            MotionEvent.ACTION_CANCEL, x, y, 0f);
                    inject(cancel, currentDisplayId, INJECT_ASYNC);
                }
                currentDownTime = SystemClock.uptimeMillis();
                currentDisplayId = displayId;
                MotionEvent event = obtainMotionEvent(
                        currentDownTime, currentDownTime,
                        MotionEvent.ACTION_DOWN, x, y, 1f);
                boolean result = inject(event, displayId, INJECT_WAIT_FOR_FINISH);
                if (!result) {
                    currentDownTime = 0;
                    currentDisplayId = -1;
                }
                return result;
            }

            synchronized boolean move(int x, int y, int displayId) {
                if (currentDownTime == 0 || currentDisplayId != displayId)
                    return false;
                MotionEvent event = obtainMotionEvent(
                        currentDownTime, SystemClock.uptimeMillis(),
                        MotionEvent.ACTION_MOVE, x, y, 1f);
                return inject(event, displayId, INJECT_ASYNC);
            }

            synchronized boolean hasActiveGesture() {
                return currentDownTime != 0;
            }

            synchronized boolean up(int x, int y, int displayId) {
                if (currentDownTime == 0 || currentDisplayId != displayId)
                    return false;
                MotionEvent event = obtainMotionEvent(
                        currentDownTime, SystemClock.uptimeMillis(),
                        MotionEvent.ACTION_UP, x, y, 0f);
                boolean result = inject(event, displayId, INJECT_ASYNC);
                currentDownTime = 0;
                currentDisplayId = -1;
                return result;
            }

            boolean key(int keyCode, int action, int displayId) {
                long eventTime = SystemClock.uptimeMillis();
                KeyEvent event = new KeyEvent(
                        eventTime, eventTime, action, keyCode, 0);
                return inject(event, displayId,
                        action == KeyEvent.ACTION_DOWN
                                ? INJECT_WAIT_FOR_FINISH
                                : INJECT_ASYNC);
            }

            private MotionEvent obtainMotionEvent(
                    long downTime, long eventTime, int action,
                    float x, float y, float pressure) {
                MotionEvent.PointerProperties properties = new MotionEvent.PointerProperties();
                properties.id = 0;
                properties.toolType = MotionEvent.TOOL_TYPE_FINGER;
                MotionEvent.PointerCoords coordinates = new MotionEvent.PointerCoords();
                coordinates.x = Math.max(0, x);
                coordinates.y = Math.max(0, y);
                coordinates.pressure = pressure;
                coordinates.size = 1f;
                return MotionEvent.obtain(
                        downTime, eventTime, action,
                        1,
                        new MotionEvent.PointerProperties[] { properties },
                        new MotionEvent.PointerCoords[] { coordinates },
                        0, 0, 1f, 1f,
                        DEVICE_ID, 0, SOURCE, 0);
            }

            private boolean inject(InputEvent event, int displayId, int mode) {
                try {
                    if (manager == null)
                        return false;
                    if (displayId != 0) {
                        if (setDisplayId == null)
                            setDisplayId = InputEvent.class.getMethod("setDisplayId", int.class);
                        setDisplayId.invoke(event, displayId);
                    }
                    if (injectInputEvent == null) {
                        injectInputEvent = manager.getClass().getMethod(
                                "injectInputEvent", InputEvent.class, int.class);
                    }
                    Object result = injectInputEvent.invoke(manager, event, mode);
                    return result instanceof Boolean && (Boolean) result;
                } catch (Throwable exception) {
                    Log.w(TAG, "Direct input injection failed for display " + displayId,
                            exception);
                    return false;
                } finally {
                    if (event instanceof MotionEvent)
                        ((MotionEvent) event).recycle();
                }
            }
        }

        private static String readOutput(InputStream stream) throws IOException {
            byte[] buffer = new byte[1024];
            ByteArrayOutputStream output = new ByteArrayOutputStream();
            int read;
            while ((read = stream.read(buffer)) >= 0) {
                // ActivityManager dumps can easily exceed the old 8 KiB cap before
                // reaching a secondary display. Keep enough data to validate the
                // target task while still bounding memory in the privileged service.
                if (output.size() < 1024 * 1024) {
                    output.write(buffer, 0, Math.min(read, 1024 * 1024 - output.size()));
                }
            }
            return output.toString(StandardCharsets.UTF_8.name()).trim();
        }

        private static String shell(String value) {
            return "'" + value.replace("'", "'\\''") + "'";
        }
    }
}
