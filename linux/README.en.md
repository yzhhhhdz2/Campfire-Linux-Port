<p align="center">
  <a href="README.md">简体中文</a> | <a href="README.zh-Hant.md">繁體中文</a> | <b>English</b>
</p>

# Campfire — Linux Port

A community port of [Xocio/Campfire](https://github.com/Xocio/Campfire) **v1.1.32** (GPL-3.0) to Linux, built on .NET 8 and Avalonia 11.

The upstream Windows project in `../src/Campfire/` is **never modified**. It is compiled into this port through shared-source links and serves as the reference baseline. Everything added by this port lives under `linux/` — remove that directory and you are back to a pristine upstream tree.

> This is an unofficial community port. It is not an official release and does not contact the original author's lobby service.

## Status: all milestones complete ✅

| Milestone | Result |
|---|---|
| Phase 1 — invite-code interoperability | Bidirectional between the Linux CLI/GUI and the official Windows `Campfire.exe` v1.1.32; both directions reached `Ready / 2 players / direct` |
| Phase 2 batch 1 — Avalonia GUI shell | 4 capsule tabs, custom title bar, pixel campfire logo, faithful colours and layout |
| Phase 2 batch 2 — dynamic theme + real features | OKLCH palettes, 9 seed colours, 6 styles, 400 ms transitions, gsettings dark/light, SkiaSharp wallpaper sampling; read-only world scan, component self-test, lobby control |
| GUI bidirectional regression | Both directions verified in the VM on 2026-11-19 |
| Minecraft 26.3 end-to-end | On 2026-11-25 the Java Agent auto-opened a real 26.3 world to LAN; the official Windows client joined with `0.8 ms · direct TCP · 2 players` |
| Polish | 13 UI animations, and a three-language UI (简体 / 繁體 / English) added on 2026-11-26 |

| Directory | Status |
|---|---|
| `Campfire.Cli/` | Complete `Link/` protocol layer + `LinkSession` orchestration + Linux adapters + CLI sessions + lobby client; `selftest` **11/11 pass** |
| `Campfire.Lobby/` | ASP.NET Core Minimal API self-hosted lobby; `verify-lobby.sh` **42/42 pass** |
| `Campfire.Avalonia/` | Builds with **0 warnings / 0 errors**; full dynamic theme, animations and three-language UI |

## What this port keeps, and what it drops

Kept: invite-code rooms (EasyTier peer-to-peer), the room protocol byte-for-byte, the CLI, the GUI, the self-hosted lobby, dynamic theming, and the Java Agent that opens a Minecraft world to LAN.

Dropped on purpose: mod sync, voice chat, world backups, online map download/search, online updates, telemetry, notifications, and the official public lobby. Invite-code multiplayer **does not depend on any lobby** — do not set `IsAvailable=true`, do not contact `api.mccampfire.top`, and do not click "publish to lobby".

Protocol files (`RoomCode`, `RoomProtocol`, `RoomFraming`, `EasyTierConfig`, …) are **linked, never copied or edited**; otherwise the official Windows client would reject our rooms. The single physical copy-and-edit is `Campfire.Cli/Link/LinkSession.cs`, where only the `SkipProbe` switch was added.

## Deploying to a Linux machine

The working environment for the real-machine test was **Bazzite (immutable, Wayland) in VMware**. Drag-and-drop, shared folders **and clipboard sharing are unavailable** there, so every operation is packaged as "a script served over HTTP + one line of `curl` you type by hand".

### Step 0 — serve `linux/` over HTTP on the Windows host

The HTTP root is the `linux/` directory itself. Any script placed in that directory is immediately downloadable. Use the IP printed by the server window; DHCP changes it between sessions.

Three rules for hand-typed commands: no `<...>` placeholders, no combined `-flags`, no `~/` paths.

### One-line install flow

```bash
curl 192.168.1.5:8000/step2.sh | bash     # EasyTier 2.6.4
curl 192.168.1.5:8000/step3.sh | bash     # loopback self-test
curl 192.168.1.5:8000/step4.sh | bash     # bidirectional interop (two-line form)
curl 192.168.1.5:8000/step5.sh | bash     # build the Avalonia GUI
curl 192.168.1.5:8000/step6.sh | bash     # Temurin JDK 17 (older MC versions)
curl 192.168.1.5:8000/step7.sh | bash     # MC 26.3 offline bundle + Temurin 25 + GUI entry
```

`step5.sh` reads from `/dev/tty`, so it is also provided as a two-line form:

```bash
curl 192.168.1.5:8000/step5.sh > step5.sh
bash step5.sh
```

GUI entry points: `bash ~/campfire-gui.sh` (self-test mode), `campfire-gui-autolan.sh`, and `campfire-gui-autolan-26.sh` (JDK 25, for MC 26.3).

### Why copying only `linux/` fails

`Campfire.Cli.csproj` links `../../src/Campfire/Link/**/*.cs`. Without the upstream tree the build cannot resolve those sources. Ship the whole repository (or the `src/` + `linux/` pair), then run:

```bash
cd linux && bash build.sh
```

The Windows host needs .NET 8 SDK; in Git Bash, `export PATH="$HOME/.dotnet:$PATH"` may be required. On Linux, `build.sh` restores from `offline-nuget.tar.gz` if present.

### Large artifacts are downloaded, never committed

| Artifact | Size | Purpose |
|---|---|---|
| `easytier-linux-2.6.4.zip` | 25 MB | EasyTier component |
| `offline-nuget.tar.gz` | 167 MB | 25 NuGet packages for offline restore |
| `OpenJDK17U-...17.0.21_9.tar.gz` | 185 MB | JDK 17 for older Minecraft |
| `mc-26.3-offline.tar.gz` | 497 MB | MC 26.3 client.jar, 66 Linux libraries, 12 natives, asset index 34, 5149 asset objects |
| `OpenJDK25U-...25.0.5_8.tar.gz` | 135 MB | JDK 25 (game JVM **and** attach JVM) |

`pack-for-linux.sh` excludes all of them; the source bundle is only ~600 KB. Minecraft client and assets remain the property of Mojang / Microsoft and are **not distributed** — `fetch-mc263.py` downloads them on the user's own machine into their own VM.

## CLI commands

```bash
campfire selftest                 # 11 protocol checks
campfire doctor                   # runtime / EasyTier / display diagnostics
campfire config list              # dump settings.ini
campfire easytier install         # install EasyTier component
campfire lobby list               # query the self-hosted lobby
campfire host --port 43210        # create a room
campfire join U/XXXX-XXXX-XXXX-XXXX
campfire stop
```

## Self-hosted lobby

`Campfire.Lobby/` is an ASP.NET Core Minimal API with an in-memory store, heartbeat expiry and password/IP digests. Start it with `./run-lobby.sh` (default `http://127.0.0.1:5080`) and validate with `./verify-lobby.sh` (42 contract checks). A lobby is optional: invite-code connections never traverse a server, and a self-hosted lobby is invisible to the official one.

## Dynamic theme

Ported verbatim from upstream `ColorMath.cs` (sRGB ↔ Oklab, gamma, gamut bisection) and `ThemeManager.cs` (9 seed colours, 6 styles, `FillLight` / `FillDark`). Windows-only APIs were replaced as follows:

| Windows | Linux |
|---|---|
| `BeginAnimation` (WPF) | `DispatcherTimer` 20 ms frame interpolation, 400 ms CubicEaseInOut |
| `SystemEvents` / registry | 3-second `gsettings color-scheme` polling |
| `DwmGetColorizationColor` | GNOME 9-colour accent enum |
| GDI+ wallpaper scaling | SkiaSharp reading `picture-uri(-dark)`, 128 px sample |

Without GNOME it degrades to `gtk-theme` / `GTK_THEME` / light. `{DynamicResource}` picks up the overrides from the top level of `Application.Resources`.

## Java Agent and auto-open LAN

`Linux/GameAgent.cs` + `GameMappings.cs` (BMCLAPI first, SHA-1 cache), 40 legacy mapping tables and 3 embedded agent jars. Opening a world to LAN requires a **real Minecraft** plus a **full JDK with `jdk.attach`** — a launcher-bundled JRE is not enough. If auto-open fails, the fallback is to click "Open to LAN" in game, copy the port, uncheck auto-open in the GUI and enter it manually.

## Minecraft 26.3 end-to-end (verified 2026-11-25)

26.3 is a release from 2026-09-15; since 2026 Mojang versions by year. Two changes matter here:

| Change | Consequence |
|---|---|
| 26.1 dropped obfuscation | The version JSON has no `client_mappings`; no mapping table is needed |
| 26.1 requires Java 25 | class file 69 — JDK 17 cannot load it; use Temurin 25 for both the game and attach |

Static pre-check with `javap` confirmed `IntegratedServer.publishServer(MultiplayerScope, int)` still calls `startTcpServerListener` and writes `publishedPort`, so **no C# or Java changes were needed**; ASM 9.11.1 reads major 69 fine.

Verified result: the Minecraft title bar showed `Minecraft 26.3 - Multiplayer (LAN)` with the chat message that Campfire had opened the world automatically; the Linux GUI showed `Ready / 2 players / 1 direct / 4 nodes`; the Windows client showed `0.8 ms · direct TCP · 2 players`. The previously unknown `ClientPacketListener.prepareKeyPair()` also works with **offline accounts**.

### Getting 26.3 to render in VMware

Wayland + SDL3 + software rendering showed only the panorama with no UI. The working configuration, now baked into the default `minecraft-26.3.sh` generated by `step7.sh`, is:

`SDL_VIDEODRIVER=x11` + `LIBGL_ALWAYS_SOFTWARE=1` + llvmpipe + 800×600 + `-Xmx1G` + a low-quality `options.txt`. Vulkan is **not** required. `step7-gfx.sh` remains as a diagnostic / Vulkan fallback, and `MC_QUICK_WORLD=CampfireTest` can skip the menu entirely.

### Two real-machine gotchas (both fixed)

- `classpath.txt` was generated with `pathlib.Path` on Windows, producing 352 backslashes and a CRLF — Linux `-cp` could not resolve any jar, giving `ClassNotFoundException`. Fixed by joining POSIX strings and writing bytes with LF; `step7-fix.sh` repairs an already-installed VM without re-downloading 497 MB.
- `.sha256` files had the same CRLF problem, making `sha256sum -c` treat `\r` as part of the filename. All generated text files for Linux consumption are now written as binary LF.

## UI animations

13 effects driven by one shared 30 fps `DispatcherTimer`, composed through independent channels (enter / hover / press / pulse) so they never overwrite each other: window intro, staggered page and card entrances with directional slides, content follow-up, state-dot breathing with a Ready halo, button three-state feedback, canvas ripples, value pulses, swatch pop, invite-code typewriter and campfire sway.

Hard rule: **only `Opacity` and `RenderTransform` are animated** — never a theme-dependent brush, because the theme manager replaces `Application.Resources` every 20 ms. No layout-triggering property is touched.

| Variable | Effect |
|---|---|
| `CAMPFIRE_ANIM=0` | Everything static, including the campfire frame animation |
| `CAMPFIRE_ANIM=1` | Force on (overrides the system "reduce animations" setting) |
| unset on Linux | Respects GNOME `org.gnome.desktop.interface enable-animations` |
| `CAMPFIRE_ANIM_FPS=15..60` | Shared timer frame rate (default 30) |

## UI localization (added 2026-11-26)

The UI and the `linux/` documentation are available in **Simplified Chinese / Traditional Chinese / English**.

| Layer | Implementation |
|---|---|
| UI strings | `Campfire.Avalonia/Localization/Loc.cs` + `Strings.cs` (188 keys × 3 languages); XAML uses `{Binding L[Key]}`, ViewModels use `Loc.Get` / `Loc.Format` |
| Language button | The original "繁" round button position in the title bar is restored as a **简 → 繁 → EN** cycle with a tooltip |
| Persistence | `ui.language` in `settings.ini` (`zh-Hans` / `zh-Hant` / `en`); when unset it follows `CultureInfo.CurrentUICulture` |
| Instant switching | `LocSource` raises `Item[]` for static strings; `ViewModelBase` subscribes weakly and calls `RaiseAll()` for dynamic strings; sub-page titles store a key, not a translated string |
| Docs | `README.md`, `README.zh-Hant.md`, `README.en.md`, cross-linked at the top |

The design mirrors upstream `src/Campfire/Localization/Text.cs` (plain C# tables + `LanguageChanged` + indexer binding). Upstream's `TextExtension.cs` depends on WPF `System.Windows.Data` / `Markup`, so the Avalonia port supplies its own `LocSource`. Terminology follows the upstream four-language tables; Cantonese is intentionally omitted here.

**For translators:** all visible strings live in the three `Table(...)` blocks of `Strings.cs`. Keep the key sets identical and preserve `{0}` placeholders in the same order. A missing translation falls back to Simplified Chinese, then to the key name (so gaps are obvious).

## Notes for publishing to GitHub

- `.gitattributes` pins `*.sh` and `*.py` to `eol=lf` — CRLF would break every deployment script on Linux.
- `.gitignore` excludes build output, logs, and all large binaries listed above. Check `git status --short` before the first commit; no tarball, `.nupkg`, JDK or Minecraft client jar should be staged.
- `LICENSE` is GPL-3.0 from upstream. Modified files carry a "modified from Xocio/Campfire v1.1.32" header; linked-but-unmodified files do not.
- `src/java-agent/` sources and `Mappings/*.txt` must ship with any binary distribution, as must `THIRD-PARTY-NOTICES.md`.
- Suggested repository description: *Community Linux port of Campfire 1.1.32 (.NET 8 / Avalonia 11), with invite-code multiplayer and a working Minecraft 26.3 LAN auto-open agent.* Suggested topics: `minecraft`, `linux`, `avalonia`, `dotnet`, `easytier`, `lan`.

## Distributing installable builds (added 2026-11-26)

`campfire-linux.tar.gz` is the **source** archive (for building and deploying). This section covers **end-user installers** published on GitHub Releases: no .NET SDK, no compiling, no root.

### Available formats

| Format | Release asset | After download | Notes |
|---|---|---|---|
| **AppImage** (recommended) | `Campfire-1.1.32-x86_64.AppImage` | `chmod +x`, then run | Single file, works across distros, no sandbox — EasyTier can create its TUN device |
| **tar.gz user install** | `campfire-1.1.32-linux-x64.tar.gz` | extract, `bash install.sh` | Adds a desktop entry, icon, `campfire` command and `uninstall.sh` |
| Flatpak (experimental) | `release/flatpak/campfire.yml.in` | build with `flatpak-builder` | Sandbox denies `/dev/net/tun`, so multiplayer likely does not work; see the manifest comments |

No `.deb` / `.rpm`: Bazzite is an immutable rpm-ostree system (`rpm-ostree install` mutates the image and needs a reboot), and dpkg packages do not apply at all. Both recommended formats only write inside `$HOME`.

### Installing

AppImage (replace OWNER/REPO with your repository, or simply click the asset on the Releases page):

```bash
curl -LO https://github.com/OWNER/REPO/releases/download/v1.1.32/Campfire-1.1.32-x86_64.AppImage
chmod +x Campfire-1.1.32-x86_64.AppImage
./Campfire-1.1.32-x86_64.AppImage
```

User-level tarball:

```bash
curl -LO https://github.com/OWNER/REPO/releases/download/v1.1.32/campfire-1.1.32-linux-x64.tar.gz
tar -xzf campfire-1.1.32-linux-x64.tar.gz
cd campfire-1.1.32-linux-x64
bash install.sh
```

Then find Campfire in the application menu or run `~/.local/bin/campfire`. `install.sh` writes only to `~/.local` — no sudo, compatible with immutable systems. `bash uninstall.sh` removes the app but keeps settings, logs and EasyTier. Verify downloads with `sha256sum -c SHA256SUMS`.

### Building the assets yourself

**Windows works** — the .NET 8 SDK cross-compiles for `linux-x64`, no WSL or Docker needed:

```bash
cd linux
bash pack-release.sh
```

Output: `linux/dist/campfire-1.1.32-linux-x64.tar.gz` (~64 MB, self-contained runtime plus an offline EasyTier archive). `INCLUDE_EASYTIER=0` produces a smaller archive; the app then downloads the component on first multiplayer use. The script verifies the ELF header, writes Unix permission bits, substitutes AppStream placeholders, scans for forbidden content and emits an LF sha256 file.

**AppImage needs Linux** (squashfs / `appimagetool`). `.github/workflows/release.yml` does this on an `ubuntu-22.04` runner: push a `v1.1.32` tag or trigger the workflow manually, and CI attaches the AppImage, both tarballs and `SHA256SUMS` to the Release. To build one locally on Linux:

```bash
bash release/build-appimage.sh dist/campfire-1.1.32-linux-x64.tar.gz Campfire-1.1.32-x86_64.AppImage
```

### Licensing boundary

- **Never bundled**: Minecraft client, Mojang assets, Temurin JDK (copyright and size — see `THIRD-PARTY-NOTICES.md`). Users install the game and a JDK themselves.
- Bundled EasyTier 2.6.4 is an **unmodified LGPL-3.0 standalone program**; `docs/LGPL-3.0.txt` ships with it. The app uses `CAMPFIRE_EASYTIER_ZIP` to extract it offline on first use.
- The port is GPL-3.0-or-later. `LICENSE`, `THIRD-PARTY-NOTICES.md` and `docs/SOURCE.txt` are inside the archive; the complete corresponding source (`campfire-linux.tar.gz`, including all of `src/java-agent/` and the 40 Mappings tables) is attached to every Release.

### Known limits

- linux-x64 only; arm64 needs `-r linux-arm64` and an aarch64 EasyTier asset.
- ELF files cross-compiled on Windows have no Unix execute bit; `install.sh` / `build-appimage.sh` add it (the packaging script writes 0755 via Python tarfile).
- AppImage needs FUSE (present on Bazzite); use `--appimage-extract-and-run` otherwise.
- Flatpak multiplayer is sandboxed as described above — AppImage is the recommended format.

## Constraints

- Do not touch `../src/android/`.
- Do not copy or edit protocol files; link them.
- A lobby is not required for multiplayer; game data never traverses a server.
- No Wine / Mono: the original WPF exe has no Linux runtime.
- The original Windows project stays untouched as the comparison and rollback baseline.

## GPL-3.0 compliance

Every new or rewritten file carries a header naming `Xocio/Campfire v1.1.32 (GPL-3.0)`; linked, byte-identical upstream files carry no such header. Any binary distribution must include the **complete corresponding source** of this fork (including the rewritten `Campfire.Cli/Link/LinkSession.cs`, all of `Campfire.Lobby/`, and every linked `Link\` source), together with `../LICENSE` and `../THIRD-PARTY-NOTICES.md`. EasyTier (LGPL-3.0) runs as an unmodified separate process, downloaded on demand; dnlib (MIT) and ASM (BSD-3-Clause) are covered by the upstream notices.
