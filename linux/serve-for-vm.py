#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
Campfire Linux port — 把自包含源码包传给 VMware 虚拟机（Bazzite 等）的临时 HTTP 服务。

用途：VMware 拖放 / 共享文件夹依赖 open-vm-tools，在 Bazzite（不可变系统 + Wayland）
上通常不可用。这个脚本在 Windows 主机上临时开一个只读 HTTP 服务，
虚拟机里用 curl 或浏览器就能把包下载下来，不需要装任何 VMware 工具、不需要改系统。

用法（在 Windows 的 Git Bash 或终端里）：
    python "C:/Users/yzhhh/Desktop/camp fire linux移植计划/Campfire-1.1.32/Campfire-1.1.32/linux/serve-for-vm.py"
    # 下载完成后按 Ctrl+C 关闭服务

可选环境变量：
    CF_PORT=8000        监听端口（默认 8000）
    CF_BIND=0.0.0.0     监听地址（默认所有网卡；只在本机自测时设 127.0.0.1）

本脚本只读、不写文件、不修改系统；停止后不留任何东西。
"""

import http.server
import os
import socket
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
PKG = "campfire-linux.tar.gz"
PORT = int(os.environ.get("CF_PORT", "8000"))
BIND = os.environ.get("CF_BIND", "0.0.0.0")

if hasattr(sys.stdout, "reconfigure"):
    sys.stdout.reconfigure(encoding="utf-8", errors="replace")

if not os.path.isfile(os.path.join(HERE, PKG)):
    sys.exit("找不到 %s\n请先在 linux/ 目录运行： bash pack-for-linux.sh" % os.path.join(HERE, PKG))


def local_ips():
    """列出本机 IPv4，供虚拟机访问。UDP connect 不产生真实流量，只是问系统要出口地址。"""
    ips = set()
    try:
        for info in socket.getaddrinfo(socket.gethostname(), None, socket.AF_INET):
            ips.add(info[4][0])
    except OSError:
        pass
    probe = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)
    try:
        probe.connect(("192.168.1.1", 1))
        ips.add(probe.getsockname()[0])
    except OSError:
        pass
    finally:
        probe.close()
    return sorted(ip for ip in ips if not ip.startswith("127.") and ":" not in ip)


class Handler(http.server.SimpleHTTPRequestHandler):
    def __init__(self, *args, **kwargs):
        super().__init__(*args, directory=HERE, **kwargs)

    def log_message(self, fmt, *args):
        # 打印请求日志，方便确认虚拟机真的连上了
        print("  " + (fmt % args), flush=True)


addresses = local_ips()
print("=" * 62)
print("Campfire 源码包临时下载服务（只读，Ctrl+C 结束）")
print("=" * 62)
print("包位置 : " + os.path.join(HERE, PKG))
print("本机 IP : " + (", ".join(addresses) if addresses else "未检测到，请 ipconfig 自查"))
print()
print("!! 只认上面这几个 IP，别抄文档或聊天里的示例地址（DHCP 会变）!!")
print("!! IP 直接写数字，别套 < > 尖括号：bash 会当成重定向，curl 根本不会运行 !!")
print()
for ip in addresses:
    print("  【阶段 2 GUI】虚拟机里敲这两条（照抄，别加尖括号、别加 - 开头的选项）：")
    print("    curl %s:%d/step5.sh > step5.sh" % (ip, PORT))
    print("    bash step5.sh")
    print("  【完整 JDK / 自动开局域网】虚拟机里敲一行：")
    print("    curl %s:%d/step6.sh | bash" % (ip, PORT))
    print("  【源码包手动下载】")
    print("    curl -f -O --max-time 30 http://%s:%d/%s" % (ip, PORT, PKG))
    print("    curl -f -O --max-time 30 http://%s:%d/%s.sha256" % (ip, PORT, PKG))
    print("    sha256sum -c %s.sha256" % PKG)
    print("  或浏览器打开：")
    print("    http://%s:%d/" % (ip, PORT))
print()
print("若虚拟机连不上：")
print("  1) 虚拟机里查网关(NAT 模式用这个地址)： ip route | awk '/default/{print $3}'")
print("  2) 虚拟机里先测： ping -c 3 %s" % (addresses[0] if addresses else "主机IP"))
print("  3) Windows 防火墙弹窗勾『允许访问』（公用网络尤其要确认）")
print("  4) VMware 的 VMnet1/VMnet8 网卡若是 Down：")
print("     『编辑 → 虚拟网络编辑器 → 还原默认设置』重建，或把虚拟机改成桥接模式")
print("  5) 校园网 AP 客户端隔离会挡桥接 → 换手机热点，或直接用 U 盘")
print()
print("下载完成后按 Ctrl+C 关掉本服务。")
print("=" * 62)

try:
    server = http.server.ThreadingHTTPServer((BIND, PORT), Handler)
except OSError as exc:
    sys.exit("端口 %d 起不来：%s\n换一个端口： CF_PORT=8001 python serve-for-vm.py" % (PORT, exc))

print("监听中： %s:%d" % (BIND, PORT), flush=True)
try:
    server.serve_forever()
except KeyboardInterrupt:
    print("\n已停止服务。")
finally:
    server.server_close()
