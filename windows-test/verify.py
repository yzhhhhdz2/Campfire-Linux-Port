#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
Campfire Linux port - verifies FakeMcWorld answers exactly like the real
Minecraft probe in src/Campfire/Link/Minecraft/MinecraftProbe.cs expects.

  IsServerAsync  : send 0xFE        -> first reply byte must be 0xFF
  GetStatusAsync : send 0xFE 0x01   -> 0xFF + 2-byte BE length + UTF-16BE text
                                       split by NUL; parts[0]=="§1", parts[5]=max players

Usage (with start-fake-world.bat already running):
    python verify.py [port]
"""

import socket
import sys

HOST = "127.0.0.1"
PORT = int(sys.argv[1]) if len(sys.argv) > 1 else 25565


def read_exactly(sock, count):
    data = b""
    while len(data) < count:
        chunk = sock.recv(count - len(data))
        if not chunk:
            break
        data += chunk
    return data


def connect():
    sock = socket.create_connection((HOST, PORT), 2)
    sock.settimeout(2)
    return sock


def main():
    ok = True

    # Probe 1: MinecraftProbe.IsServerAsync
    try:
        s = connect()
        s.sendall(b"\xFE")
        reply = read_exactly(s, 1)
        s.close()
        good = reply == b"\xFF"
        print("IsServerAsync  0xFE      -> %-6s  reply=%s" % ("OK" if good else "FAIL", reply.hex()))
        ok &= good
    except OSError as exc:
        print("IsServerAsync            -> FAIL  (%s)" % exc)
        ok = False

    # Probe 2: MinecraftProbe.GetStatusAsync
    try:
        s = connect()
        s.sendall(b"\xFE\x01")
        head = read_exactly(s, 3)
        if len(head) < 3 or head[0] != 0xFF:
            raise ValueError("bad header: %s" % head.hex())
        units = (head[1] << 8) | head[2]
        body = read_exactly(s, units * 2)
        s.close()
        parts = body.decode("utf-16-be").split("\0")
        good = len(parts) >= 6 and parts[0] == "\u00A71" and parts[5].isdigit()
        print("GetStatusAsync 0xFE 0x01 -> %-6s  parts=%s" % ("OK" if good else "FAIL", parts))
        print("  version=%s  motd=%s  maxPlayers=%s" %
              (parts[2] if len(parts) > 2 else "?",
               parts[3] if len(parts) > 3 else "?",
               parts[5] if len(parts) > 5 else "?"))
        ok &= good
    except (OSError, ValueError, UnicodeError) as exc:
        print("GetStatusAsync           -> FAIL  (%s)" % exc)
        ok = False

    print()
    if ok:
        print("PASS - Campfire will detect this as an 'Open to LAN' world on port %d." % PORT)
        print("       Now run Campfire.exe -> Diagnostics -> 'World' should turn green.")
        return 0
    print("FAIL - check that start-fake-world.bat is still running.")
    return 1


if __name__ == "__main__":
    sys.exit(main())
