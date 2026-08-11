package com.fox.MFAAvalonia;

import android.content.Context;
import android.os.Binder;
import android.os.Parcel;
import android.os.Process;
import android.os.RemoteException;
import android.util.Log;

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

public final class MfaShizukuUserService extends Binder {
    private static final String TAG = "MFAAvalonia";
    private static final int HEALTH_TRANSACTION = 1;
    private static final int DESTROY_TRANSACTION = 16777115;

    private final InputServer inputServer;
    private volatile int clientPid = -1;

    public MfaShizukuUserService(Context context) throws IOException {
        inputServer = new InputServer();
        inputServer.start();
        startClientWatchdog();
        Log.i(TAG, "Shizuku UserService started, uid=" + Process.myUid()
                + ", port=" + inputServer.getPort());
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
        if (code == DESTROY_TRANSACTION) {
            Log.i(TAG, "Destroy transaction received.");
            inputServer.shutdown();
            System.exit(0);
            return true;
        }
        return super.onTransact(code, data, reply, flags);
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
                if (pid > 0 && !new File("/proc/" + pid).exists()) {
                    Log.i(TAG, "MFA process " + pid + " exited; stopping UserService.");
                    inputServer.shutdown();
                    System.exit(0);
                    return;
                }
            }
        }, "MfaClientWatchdog");
        watchdog.setDaemon(true);
        watchdog.start();
    }

    private static final class InputServer extends Thread {
        private static final int MAGIC = 0x4d464142;
        private final ServerSocket server;

        InputServer() throws IOException {
            super("MfaBridgeInput");
            server = new ServerSocket(0, 4, InetAddress.getByName("127.0.0.1"));
            setDaemon(true);
        }

        int getPort() {
            return server.getLocalPort();
        }

        void shutdown() {
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
                    if (input.readInt() != MAGIC) {
                        continue;
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
                } catch (IOException exception) {
                    if (!server.isClosed()) {
                        Log.w(TAG, "Input server error", exception);
                    }
                }
            }
        }

        private static int execute(int displayId, int method, int x, int y, int key, String text) {
            String display = displayId >= 0 ? "-d " + displayId + " " : "";
            String command;
            switch (method) {
                case 1:
                    String target = shell(text);
                    String displayOption = displayId >= 0 ? "--display " + displayId + " " : "";
                    String stop = key != 0 ? "am force-stop " + target + "; " : "";
                    // Android's monkey command has no --display option on many devices.
                    // Resolve a package to its launcher activity, then let ActivityManager
                    // launch that component directly on the controller's display.
                    command = stop
                            + "target=" + target + "; "
                            + "case \"$target\" in "
                            + "*/*) component=\"$target\" ;; "
                            + "*) component=$(cmd package resolve-activity --brief \"$target\" | tail -n 1) ;; "
                            + "esac; "
                            + "case \"$component\" in "
                            + "*/*) am start -W " + displayOption + "-n \"$component\" ;; "
                            + "*) echo \"No launcher activity for $target\" >&2; exit 2 ;; "
                            + "esac";
                    break;
                case 2:
                    command = "am force-stop " + shell(text);
                    break;
                case 4:
                    command = "input " + display + "text " + shell(text);
                    break;
                case 6:
                    command = "input " + display + "motionevent DOWN " + x + " " + y;
                    break;
                case 7:
                    command = "input " + display + "motionevent MOVE " + x + " " + y;
                    break;
                case 8:
                    command = "input " + display + "motionevent UP " + x + " " + y;
                    break;
                case 9:
                    command = "input " + display + "keyevent " + key;
                    break;
                default:
                    return -2;
            }

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
                return exitCode;
            } catch (IOException | InterruptedException exception) {
                if (exception instanceof InterruptedException) {
                    Thread.currentThread().interrupt();
                }
                Log.w(TAG, "Input command failed", exception);
                return -1;
            }
        }

        private static String readOutput(InputStream stream) throws IOException {
            byte[] buffer = new byte[1024];
            ByteArrayOutputStream output = new ByteArrayOutputStream();
            int read;
            while ((read = stream.read(buffer)) >= 0) {
                if (output.size() < 8192) {
                    output.write(buffer, 0, Math.min(read, 8192 - output.size()));
                }
            }
            return output.toString(StandardCharsets.UTF_8.name()).trim();
        }

        private static String shell(String value) {
            return "'" + value.replace("'", "'\\''") + "'";
        }
    }
}
