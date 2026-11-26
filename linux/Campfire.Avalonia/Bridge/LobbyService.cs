// Campfire Linux port — 修改自 Xocio/Campfire v1.1.32 (GPL-3.0)。
// 修改：新增。阶段 1.5 的 Campfire.Lobby 是独立 ASP.NET Core 进程，GUI 里只做
//       只读状态探测 + 「本程序自己启动的进程」的启停，绝不 kill 别人起的服务。

using Campfire.Avalonia.Localization;
using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;

namespace Campfire.Avalonia.Bridge
{
    internal sealed class LobbyService
    {
        public const string DefaultUrl = "http://127.0.0.1:5080";

        private Process _owned;

        public string Url => LinuxConfig.ServiceUrl ?? DefaultUrl;

        /// <summary>GUI 输出目录 linux/Campfire.Avalonia/bin/Release/net8.0 → 上溯 4 级到 linux/。</summary>
        public string DllPath => Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory, "..", "..", "..", "..",
            "Campfire.Lobby", "bin", "Release", "net8.0", "Campfire.Lobby.dll"));

        public bool OwnsProcess => _owned != null && !HasExited(_owned);

        /// <summary>HTTP 探测 /v1/lobby；连不上就是未运行，不抛异常。</summary>
        public async Task<string> StatusAsync()
        {
            try
            {
                using (var client = new HttpClient { Timeout = TimeSpan.FromSeconds(2) })
                {
                    HttpResponseMessage response = await client.GetAsync(Url.TrimEnd('/') + "/v1/lobby");
                    return response.IsSuccessStatusCode
                        ? Loc.Format("LobbyRunning", (int)response.StatusCode)
                        : Loc.Format("LobbyListeningBad", (int)response.StatusCode);
                }
            }
            catch (Exception ex) when (ex is HttpRequestException || ex is TaskCanceledException || ex is UriFormatException)
            {
                return Loc.Get("LobbyNotRunning");
            }
        }

        public string Start()
        {
            if (OwnsProcess)
                return Loc.Get("LobbyAlreadyStarted");
            string dll = DllPath;
            if (!File.Exists(dll))
                return Loc.Format("LobbyDllMissing", dll);
            try
            {
                var psi = new ProcessStartInfo("dotnet")
                {
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    WorkingDirectory = Path.GetDirectoryName(dll),
                };
                psi.ArgumentList.Add(dll);
                _owned = Process.Start(psi);
                return _owned == null
                    ? Loc.Get("LobbyStartFailNoProcess")
                    : Loc.Format("LobbyStarted", _owned.Id, Url);
            }
            catch (Exception ex) when (ex is Win32Exception || ex is InvalidOperationException)
            {
                return Loc.Format("LobbyStartFail", ex.Message);
            }
        }

        public string Stop()
        {
            if (!OwnsProcess)
                return Loc.Get("LobbyNotOwned");
            try
            {
                int pid = _owned.Id;
                _owned.Kill();
                _owned.Dispose();
                _owned = null;
                return Loc.Format("LobbyStopped", pid);
            }
            catch (Exception ex) when (ex is Win32Exception || ex is InvalidOperationException)
            {
                return Loc.Format("LobbyStopFail", ex.Message);
            }
        }

        private static bool HasExited(Process process)
        {
            try { return process.HasExited; }
            catch (InvalidOperationException) { return true; }
        }
    }
}
