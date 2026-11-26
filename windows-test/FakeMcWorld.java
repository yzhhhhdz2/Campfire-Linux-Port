/*
 * FakeMcWorld.java
 * Campfire Linux port - stage 1 acceptance helper (Windows side, ZERO downloads).
 *
 * WHY: The official Windows Campfire.exe refuses to HOST a room unless it finds a
 *      local "Open to LAN" Minecraft world. Installing a launcher + the game is
 *      slow on a school network. This tiny Java process stands in for that world.
 *
 * HOW IT WORKS (matches Campfire v1.1.32 source exactly):
 *   1. LocalWorldFinder.GetJavaListeningPorts() scans listening TCP ports owned by
 *      a process whose name starts with "java". Running this with java.exe satisfies it.
 *   2. MinecraftProbe.IsServerAsync(port) connects to 127.0.0.1:port, writes one
 *      byte 0xFE, and considers it a game ONLY if the first reply byte is 0xFF.
 *   3. MinecraftProbe.GetStatusAsync(port) writes 0xFE 0x01 and parses the legacy
 *      1.4 ping: 0xFF + 2-byte big-endian length + UTF-16BE text split by NUL,
 *      requiring parts[0] == "§1" and parts[5] == max players.
 *
 * This is a PROTOCOL STUB for room-layer interop testing. It is NOT a playable
 * Minecraft server - no one can actually join a world through it. Stage 1 only
 * verifies invite code / EasyTier / room protocol, which this is enough for.
 *
 * Build : C:\jdk-17\bin\javac.exe -encoding UTF-8 -d . FakeMcWorld.java
 * Run   : C:\jdk-17\bin\java.exe  -cp . FakeMcWorld 25565
 * Stop  : Ctrl+C in that window (the room will then time out after ~10 s).
 *
 * GPL-3.0 project context: this is an independent test helper, not derived from
 * Campfire source code. No Campfire code is copied here.
 */

import java.io.IOException;
import java.io.InputStream;
import java.io.OutputStream;
import java.net.InetAddress;
import java.net.ServerSocket;
import java.net.Socket;
import java.net.SocketTimeoutException;
import java.nio.charset.StandardCharsets;

public final class FakeMcWorld {

    private static final int DEFAULT_PORT = 25565;

    /* "§1", protocol, version name, MOTD, online players, max players - split by NUL. */
    private static final String STATUS =
            "\u00A7" + "1" + "\0" + "47" + "\0" + "1.20.1" + "\0"
                    + "Campfire Linux Port Test" + "\0" + "0" + "\0" + "20";

    public static void main(String[] args) throws Exception {
        int port = args.length > 0 ? Integer.parseInt(args[0]) : DEFAULT_PORT;
        ServerSocket server = new ServerSocket(port, 64, InetAddress.getByName("127.0.0.1"));
        System.out.println("Fake MC world listening on 127.0.0.1:" + port);
        System.out.println("Keep this window OPEN while Campfire hosts a room.");
        System.out.println("Press Ctrl+C to stop (the room will then close after ~10 s).");
        while (true) {
            final Socket client = server.accept();
            Thread worker = new Thread(() -> handle(client), "fake-mc-client");
            worker.setDaemon(true);
            worker.start();
        }
    }

    private static void handle(Socket socket) {
        try (Socket s = socket) {
            s.setSoTimeout(250);
            InputStream in = s.getInputStream();
            OutputStream out = s.getOutputStream();

            int first = in.read();
            if (first < 0) {
                return;
            }
            int second = -1;
            try {
                second = in.read();
            } catch (SocketTimeoutException ignored) {
                /* IsServerAsync sends only 0xFE. */
            }

            if (first == 0xFE && second == 0x01) {
                byte[] text = STATUS.getBytes(StandardCharsets.UTF_16BE);
                int units = text.length / 2;
                out.write(0xFF);
                out.write((units >>> 8) & 0xFF);
                out.write(units & 0xFF);
                out.write(text);
            } else {
                out.write(0xFF);
            }
            out.flush();
        } catch (IOException ignored) {
            /* One probe finished; Campfire opens a fresh connection each time. */
        }
    }
}
