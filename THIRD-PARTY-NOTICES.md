# Third-party notices

Campfire includes or downloads the following components.

## dnlib 4.4.0

Compiled into Campfire.exe. MIT License. https://github.com/0xd4d/dnlib

```
Copyright (C) 2012-present de4dot@gmail.com

Permission is hereby granted, free of charge, to any person obtaining
a copy of this software and associated documentation files (the
"Software"), to deal in the Software without restriction, including
without limitation the rights to use, copy, modify, merge, publish,
distribute, sublicense, and/or sell copies of the Software, and to
permit persons to whom the Software is furnished to do so, subject to
the following conditions:

The above copyright notice and this permission notice shall be
included in all copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND,
EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF
MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND
NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT HOLDERS BE
LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY, WHETHER IN AN ACTION
OF CONTRACT, TORT OR OTHERWISE, ARISING FROM, OUT OF OR IN CONNECTION
WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE SOFTWARE.
```

## ASM 9.11.1

Packaged inside the Java agent embedded in Campfire.exe. BSD 3-Clause License. https://asm.ow2.io

```
ASM: a very small and fast Java bytecode manipulation framework
Copyright (c) 2000-2011 INRIA, France Telecom
All rights reserved.

Redistribution and use in source and binary forms, with or without
modification, are permitted provided that the following conditions
are met:
1. Redistributions of source code must retain the above copyright
   notice, this list of conditions and the following disclaimer.
2. Redistributions in binary form must reproduce the above copyright
   notice, this list of conditions and the following disclaimer in the
   documentation and/or other materials provided with the distribution.
3. Neither the name of the copyright holders nor the names of its
   contributors may be used to endorse or promote products derived from
   this software without specific prior written permission.

THIS SOFTWARE IS PROVIDED BY THE COPYRIGHT HOLDERS AND CONTRIBUTORS "AS IS"
AND ANY EXPRESS OR IMPLIED WARRANTIES, INCLUDING, BUT NOT LIMITED TO, THE
IMPLIED WARRANTIES OF MERCHANTABILITY AND FITNESS FOR A PARTICULAR PURPOSE
ARE DISCLAIMED. IN NO EVENT SHALL THE COPYRIGHT OWNER OR CONTRIBUTORS BE
LIABLE FOR ANY DIRECT, INDIRECT, INCIDENTAL, SPECIAL, EXEMPLARY, OR
CONSEQUENTIAL DAMAGES (INCLUDING, BUT NOT LIMITED TO, PROCUREMENT OF
SUBSTITUTE GOODS OR SERVICES; LOSS OF USE, DATA, OR PROFITS; OR BUSINESS
INTERRUPTION) HOWEVER CAUSED AND ON ANY THEORY OF LIABILITY, WHETHER IN
CONTRACT, STRICT LIABILITY, OR TORT (INCLUDING NEGLIGENCE OR OTHERWISE)
ARISING IN ANY WAY OUT OF THE USE OF THIS SOFTWARE, EVEN IF ADVISED OF
THE POSSIBILITY OF SUCH DAMAGE.
```

## EasyTier

Not included in Campfire.exe. Downloaded on first use of multiplayer and run unmodified as a separate program. LGPL-3.0. https://github.com/EasyTier/EasyTier

The license text and source code are available at https://github.com/EasyTier/EasyTier/blob/main/LICENSE.

## Redistributable Linux release archives (added 2026-11-26)

`linux/pack-release.sh` produces a self-contained linux-x64 binary archive, and
`.github/workflows/release.yml` additionally produces an AppImage. These releases contain:

- The Campfire Linux port (GPL-3.0-or-later), plus the unmodified .NET 8 runtime and
  Avalonia 11 / SkiaSharp assemblies taken from their NuGet packages.
- **EasyTier 2.6.4 (LGPL-3.0)**, when bundled: the unmodified `easytier-core` /
  `easytier-cli` binaries inside the official upstream zip, launched as separate
  processes and never linked. The full LGPL-3.0 text ships as `docs/LGPL-3.0.txt`,
  and the source is at https://github.com/EasyTier/EasyTier. Set
  `INCLUDE_EASYTIER=0` to omit it entirely; the application then downloads the
  component on first multiplayer use, matching upstream behaviour.
- The application icon `linux/release/assets/campfire.png` is extracted from upstream
  `src/Campfire/Assets/Campfire.ico` (256x256 PNG frame). As noted above, the campfire
  artwork is Minecraft content copyright Mojang Studios; this fork adds no new artwork.
- The **complete corresponding source** is published alongside every binary release as
  `campfire-linux.tar.gz`, which includes all `linux/` sources, the linked unmodified
  protocol layer, the full `src/java-agent/` sources with the three embedded jars, and
  all 40 `Mappings/*.txt` tables. See `docs/SOURCE.txt` inside the binary archive.

**Not distributed**: the Minecraft client jar, its libraries, asset objects, any other
Mojang content, and the Eclipse Temurin JDK tarballs. Users obtain those separately and
remain bound by the Minecraft EULA (https://www.minecraft.net/en-us/eula).

## Android app

The Android app in `src/android` has its own notices in `src/android/THIRD_PARTY_NOTICES.md`.

## Minecraft assets

The campfire icon comes from Minecraft and is copyright Mojang Studios. Skin faces of premium players are loaded through a public Mojang service.

The animated badge `linux/Campfire.Avalonia/Assets/CampfireFrames.png` is a derived asset: a Minecraft campfire animated GIF supplied by the user (40 frames at 100 ms each) was decoded with the Python standard library, each frame resized to 96x96 and assembled into a horizontal frame strip. The underlying campfire artwork remains Minecraft content copyright Mojang Studios; this fork adds no new artwork.

### Minecraft Java 26.3 client (local test fixture, not distributed)

`linux/fetch-mc263.py` downloads the **unmodified** Minecraft Java 26.3 client jar, its libraries,
native artifacts and asset objects from Mojang's official distribution endpoints. Its only purpose is
to let the user run a real game inside their own virtual machine and verify this fork's Java Agent
"open to LAN" hook. The resulting `linux/mc-26.3-offline.tar.gz` is a local artifact created on the
user's machine; it is excluded from `campfire-linux.tar.gz` and is not redistributed by this fork.
Minecraft and its assets remain copyright Mojang Studios / Microsoft and are subject to the Minecraft
EULA (https://www.minecraft.net/en-us/eula). No Mojang file is modified.

### Eclipse Temurin JDK (17 and 25)

`fetch-jdk.sh` / `step6.sh` and `step7.sh` install unmodified Eclipse Temurin JDK tarballs for Java 17
and Java 25 respectively, downloaded by the user from the TUNA Adoptium mirror. Full JDKs are needed
for the `jdk.attach` module (launcher-bundled runtimes are usually JREs). Temurin is GPL-2.0 with the
Classpath Exception; both tarballs are excluded from the source package and are not redistributed here.
See https://adoptium.net/ and https://github.com/adoptium/temurin-build/blob/master/LICENSE.

## Avalonia UI (stage 2 Linux GUI)

The GUI in `linux/Campfire.Avalonia` uses Avalonia 11 (MIT) and its transitive dependencies
SkiaSharp and HarfBuzzSharp (MIT). These packages are restored by `dotnet restore` and are
**not** bundled into `campfire-linux.tar.gz`; an offline NuGet mirror (`offline-nuget.tar.gz`)
is provided separately so the virtual machine can restore without internet access. The mirror
contains unmodified `.nupkg` files copied from the local NuGet cache.

Upstream license texts:
- https://github.com/AvaloniaUI/Avalonia/blob/master/licence.md
- https://github.com/mono/SkiaSharp/blob/main/LICENSE.md
- https://github.com/mono/SkiaSharp/tree/main/externals/harfbuzz (HarfBuzz, MIT)

All original Campfire source files linked via `<Compile Include>` remain under GPL-3.0;
the Avalonia-specific files carry a "modified from Xocio/Campfire v1.1.32 (GPL-3.0)" header.
