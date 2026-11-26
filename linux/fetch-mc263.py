#!/usr/bin/env python3
# Campfire Linux port — 基于 Xocio/Campfire v1.1.32 (GPL-3.0)，新增文件（非改写）。
# 作用：在 Windows 侧预下 Minecraft Java 26.3 的离线运行环境。
#
# 背景：虚拟机 Bazzite 没有剪贴板/共享文件夹，且校园网访问外网不稳；沿用 EasyTier / JDK
#       的「Windows 侧预下 + serve-for-vm.py HTTP 通道」套路，把游戏文件打成一个 tar.gz。
#       26.3（2026-09-15 正式版）要求 Java 25；26.1 起 Mojang 取消混淆，
#       所以 26.3 的版本 json 里没有 client_mappings，Java Agent 也不需要映射表。
#
# 用法（Windows，Git Bash 或 CMD 均可）：
#   python linux/fetch-mc263.py
# 可选： --output <包路径>  --stage <缓存目录>  --workers 24
#
# 断点续传：缓存目录里已存在且 SHA-1 正确的文件会跳过；中断后重跑即可续传。
# 只挑 Linux 适用的库（按 version json 的 rules 过滤），Windows/macOS 的 natives 不下。

import argparse
import concurrent.futures
import hashlib
import json
import os
import shutil
import sys
import tarfile
import tempfile
import threading
import time
import urllib.request
import zipfile
from pathlib import Path

VERSION = "26.3"
VERSION_JSON_URL = (
    "https://piston-meta.mojang.com/v1/packages/"
    "ead839cbccf0ddf013c3f935f2840080b5cb0d26/26.3.json"
)
ASSETS_HOST = "https://resources.download.minecraft.net"

_print_lock = threading.Lock()


def log(message):
    with _print_lock:
        print(message, flush=True)


def download(url, destination, sha1=None, expected_size=None, retries=4):
    """下载到 destination；已存在且校验通过就跳过。返回 'skip' / 'ok'。"""
    destination = Path(destination)
    if destination.exists():
        data = destination.read_bytes()
        if (sha1 is None or hashlib.sha1(data).hexdigest() == sha1) and (
            expected_size is None or len(data) == expected_size
        ):
            return "skip"
        destination.unlink()

    last = None
    for attempt in range(retries):
        try:
            request = urllib.request.Request(url, headers={"User-Agent": "Campfire-Linux-Port/1.0"})
            with urllib.request.urlopen(request, timeout=60) as response:
                data = response.read()
            if sha1 is not None and hashlib.sha1(data).hexdigest() != sha1:
                raise ValueError("SHA-1 mismatch for " + url)
            if expected_size is not None and len(data) != expected_size:
                raise ValueError("size mismatch for " + url)
            destination.parent.mkdir(parents=True, exist_ok=True)
            temporary = destination.with_name(destination.name + ".part")
            temporary.write_bytes(data)
            temporary.replace(destination)
            return "ok"
        except Exception as error:  # 网络抖动、限速、超时都重试
            last = error
            time.sleep(0.6 * (attempt + 1))
    raise RuntimeError("failed after %d tries: %s (%s)" % (retries, url, last))


def linux_allows(library):
    """Mojang version json 的 rules：默认 disallow，按顺序覆盖；只判 os.name / os.arch。"""
    rules = library.get("rules")
    if not rules:
        return True
    allowed = False
    for rule in rules:
        condition = rule.get("os", {})
        name = condition.get("name")
        arch = condition.get("arch")
        matches = (name in (None, "linux")) and (arch in (None, "x86_64", "amd64"))
        if matches:
            allowed = rule.get("action") == "allow"
    return allowed


def main():
    here = Path(__file__).resolve().parent
    parser = argparse.ArgumentParser(description="预下 Minecraft Java 26.3 离线运行包")
    parser.add_argument("--output", default=str(here / ("mc-" + VERSION + "-offline.tar.gz")))
    parser.add_argument("--stage", default=str(Path(tempfile.gettempdir()) / "cf-mc263-stage"))
    parser.add_argument("--workers", type=int, default=24)
    args = parser.parse_args()

    output = Path(args.output)
    stage = Path(args.stage)
    root = stage / "mc-26.3"          # 打进 tar 的根目录
    root.mkdir(parents=True, exist_ok=True)

    print("==== 预下 Minecraft Java %s 离线运行包 ====" % VERSION)
    print("  缓存目录 :", stage)
    print("  输出包   :", output)

    # ---- 1. 版本 json ----
    version_dir = root / "versions" / VERSION
    version_dir.mkdir(parents=True, exist_ok=True)
    version_json = version_dir / (VERSION + ".json")
    download(VERSION_JSON_URL, version_json)
    meta = json.loads(version_json.read_text(encoding="utf-8"))
    log("版本 json : javaVersion=%s  assets=%s  libraries=%d"
        % (meta.get("javaVersion", {}).get("majorVersion"), meta.get("assets"), len(meta.get("libraries", []))))
    if meta.get("javaVersion", {}).get("majorVersion") != 25:
        raise RuntimeError("26.3 应当要求 Java 25；元数据不符，拒绝打包。")
    if "client_mappings" in meta.get("downloads", {}):
        raise RuntimeError("26.3 竟然带 client_mappings（混淆版），请重新评估 Agent 兼容性。")

    # ---- 2. client.jar（版本目录里的名字要与 json 同名，GameMappings 才能按 classpath 找到 json）----
    client = meta["downloads"]["client"]
    client_jar = version_dir / (VERSION + ".jar")
    log("客户端 jar: %.1f MB" % (client["size"] / 1048576))
    download(client["url"], client_jar, client["sha1"], client["size"])

    # ---- 3. Linux 适用的 libraries ----
    libraries = [lib for lib in meta["libraries"] if linux_allows(lib)]
    log("依赖库    : %d 个（共 %d 个，按 rules 过滤掉其它平台）"
        % (len(libraries), len(meta["libraries"])))
    library_paths = []

    def fetch_library(library):
        artifact = library["downloads"]["artifact"]
        relative = artifact["path"]
        target = root / "libraries" / relative
        state = download(artifact["url"], target, artifact.get("sha1"), artifact.get("size"))
        return relative, state

    with concurrent.futures.ThreadPoolExecutor(max_workers=min(args.workers, 16)) as pool:
        for relative, state in pool.map(fetch_library, libraries):
            library_paths.append(relative)
    log("依赖库完成: %d 个" % len(library_paths))

    # ---- 4. 把 natives-linux jar 里的 .so 解到 natives/java，供 -Djava.library.path 使用 ----
    natives_dir = root / "natives" / "java"
    native_count = 0
    for relative in library_paths:
        if "natives-linux" not in relative:
            continue
        with zipfile.ZipFile(root / "libraries" / relative) as archive:
            for name in archive.namelist():
                if name.endswith("/") or name.startswith("META-INF/"):
                    continue
                target = natives_dir / name
                target.parent.mkdir(parents=True, exist_ok=True)
                with archive.open(name) as source, open(target, "wb") as sink:
                    shutil.copyfileobj(source, sink)
                native_count += 1
    log("native 文件: %d 个 -> natives/java" % native_count)

    # ---- 5. asset index ----
    asset_index = meta["assetIndex"]
    index_id = asset_index["id"]
    indexes_dir = root / "assets" / "indexes"
    indexes_dir.mkdir(parents=True, exist_ok=True)
    index_path = indexes_dir / (index_id + ".json")
    download(asset_index["url"], index_path, asset_index.get("sha1"), asset_index.get("size"))
    objects = json.loads(index_path.read_text(encoding="utf-8"))["objects"]
    log("资产索引  : %s → %d 个对象，%.1f MB"
        % (index_id, len(objects), sum(o["size"] for o in objects.values()) / 1048576))

    # ---- 6. assets objects（按 hash 前两位分目录，与原版启动器一致）----
    def fetch_object(item):
        name, info = item
        digest = info["hash"]
        target = root / "assets" / "objects" / digest[:2] / digest
        return download(ASSETS_HOST + "/" + digest[:2] + "/" + digest,
                        target, digest, info.get("size"))

    done = 0
    skipped = 0
    started = time.time()
    with concurrent.futures.ThreadPoolExecutor(max_workers=args.workers) as pool:
        futures = [pool.submit(fetch_object, item) for item in objects.items()]
        for future in concurrent.futures.as_completed(futures):
            state = future.result()
            done += 1
            if state == "skip":
                skipped += 1
            if done % 500 == 0 or done == len(objects):
                log("  资产 %d/%d（跳过 %d），用时 %.0fs" % (done, len(objects), skipped, time.time() - started))
    log("资产完成  : %d 个，耗时 %.0fs" % (done, time.time() - started))

    # ---- 7. classpath 清单与说明 ----
    # 两个 Windows 坑（2026-11-21 实测修）：
    #   1) 不能用 pathlib.Path 拼：Windows 会生成 "\"，Linux 的 -cp 不认，所有 jar 都找不到主类；
    #   2) 不能用 write_text：Windows 会把 \n 变成 \r\n，最后一个 jar 路径带 \r 同样失效。
    # 一律用正斜杠字符串拼接 + 二进制写 LF。
    classpath = ["libraries/" + p for p in library_paths] + ["versions/" + VERSION + "/" + VERSION + ".jar"]
    (root / "classpath.txt").write_bytes((":".join(classpath) + "\n").encode("utf-8"))
    (root / "MC-26.3-README.txt").write_bytes((
        "Minecraft Java %s 离线运行环境（Campfire Linux port 生成）\n"
        "Java 要求 : 25（class file 69）\n"
        "混淆      : 无（26.1 起 Mojang 取消混淆，本包不含 client_mappings）\n"
        "classpath : classpath.txt 里的路径相对本目录，用 ':' 连接\n"
        "native    : natives/java，启动时用 -Djava.library.path 指向它\n"
        "assets    : assets/indexes/%s.json + assets/objects/\n"
        "游戏 jar  : versions/%s/%s.jar + 同名 json\n"
        "账号      : 离线（--accessToken 0），仅供单机世界与自动开放局域网实测\n"
        "版权      : Minecraft 客户端与资源归 Mojang / Microsoft 所有，本包不修改、不另行分发，\n"
        "            仅由使用者本机下载后在自己的虚拟机内使用。\n"
        % (VERSION, index_id, VERSION, VERSION)).encode("utf-8"))

    # ---- 8. 打包（gzip level 1：PNG/JAR 本身已压缩，快速打包即可）----
    output.parent.mkdir(parents=True, exist_ok=True)
    log("打包中    : %s" % output)
    started = time.time()
    with tarfile.open(output, "w:gz", compresslevel=1) as archive:
        archive.add(root, arcname="mc-26.3")
    digest = hashlib.sha256(output.read_bytes()).hexdigest()
    # 必须用二进制写：Windows 上 open(..., "w") 会把 \n 变成 \r\n，
    # Linux 的 sha256sum -c 会把 \r 当成文件名的一部分而失败。
    (output.with_name(output.name + ".sha256")).write_bytes(
        (digest + "  " + output.name + "\n").encode("ascii"))
    log("完成      : %.1f MB  sha256=%s  用时 %.0fs"
        % (output.stat().st_size / 1048576, digest, time.time() - started))
    print("==== 完成 ====")
    return 0


if __name__ == "__main__":
    try:
        sys.exit(main())
    except KeyboardInterrupt:
        print("已中断；缓存仍在，重跑本脚本即可续传。", file=sys.stderr)
        sys.exit(130)
