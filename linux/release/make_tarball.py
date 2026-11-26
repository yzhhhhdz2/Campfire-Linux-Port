# Campfire Linux port — 新增（非改写）：把组装好的 dist 目录打成可分发的 tar.gz。
# 用 Python tarfile 而不是系统 tar，是为了精确设置 Unix 权限位：
# Windows 侧交叉编译出的 ELF 主程序没有执行位，直接打包用户解压后跑不起来。
import hashlib
import os
import stat
import sys
import tarfile


def mode_for(name, rel):
    base = os.path.basename(name)
    if base in ("AppRun", "install.sh", "uninstall.sh", "campfire",
                "Campfire.Avalonia", "build-appimage.sh"):
        return 0o755
    if base.endswith(".sh") or base.endswith(".so") or base.startswith("lib") and ".so" in base:
        return 0o755
    if base.endswith(".desktop"):
        return 0o644
    return 0o644


def main():
    if len(sys.argv) != 4:
        print("usage: make_tarball.py <stage_dir> <output.tar.gz> <version>", file=sys.stderr)
        return 2
    stage, output, version = sys.argv[1], sys.argv[2], sys.argv[3]
    stage = os.path.abspath(stage)
    output = os.path.abspath(output)
    arcroot = "campfire-%s-linux-x64" % version

    files = []
    for dirpath, dirnames, filenames in os.walk(stage):
        dirnames.sort()
        for fn in sorted(filenames):
            full = os.path.join(dirpath, fn)
            rel = os.path.relpath(full, stage).replace(os.sep, "/")
            files.append((full, rel))
    if not files:
        print("stage 目录为空，拒绝打包", file=sys.stderr)
        return 1

    os.makedirs(os.path.dirname(output), exist_ok=True)
    with tarfile.open(output, "w:gz", format=tarfile.GNU_FORMAT) as tar:
        # 根目录本身
        info = tarfile.TarInfo(arcroot)
        info.type = tarfile.DIRTYPE
        info.mode = 0o755
        info.mtime = 0
        tar.addfile(info)
        for full, rel in files:
            info = tar.gettarinfo(full, arcname=arcroot + "/" + rel)
            info.mode = mode_for(full, rel)
            info.uid = info.gid = 0
            info.uname = info.gname = "root"
            info.mtime = 0  # 可复现构建：同一份输入得到同一个 sha256
            with open(full, "rb") as fh:
                tar.addfile(info, fh)

    digest = hashlib.sha256(open(output, "rb").read()).hexdigest()
    sha_path = output + ".sha256"
    # 二进制写、LF 行尾：CRLF 会让 Linux 上 sha256sum -c 把 \r 当文件名。
    with open(sha_path, "wb") as fh:
        fh.write((digest + "  " + os.path.basename(output) + "\n").encode("ascii"))

    print("  包文件   : %s" % output)
    print("  文件数   : %d" % len(files))
    print("  体积     : %.1f MB" % (os.path.getsize(output) / 1048576.0))
    print("  sha256   : %s" % digest)
    return 0


if __name__ == "__main__":
    sys.exit(main())
