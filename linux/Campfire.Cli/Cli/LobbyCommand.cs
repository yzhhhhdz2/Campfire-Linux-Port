// Campfire Linux port — 修改自 Xocio/Campfire v1.1.32 (GPL-3.0)。
// 修改：新增自建大厅的 publish / join 命令；大厅客户端契约仍逐字复用官方 LobbyClient.cs。
//       本文件只负责「公开房间目录」，不参与邀请码联机链路（那条链路不经服务端）。

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Campfire.Link;
using Campfire.Link.Lobby;
using Campfire.Online;

namespace Campfire.Cli
{
    internal static class LobbyCommand
    {
        internal static int Run(string[] args)
        {
            if (args.Length == 0)
                return Usage();
            switch (args[0].ToLowerInvariant())
            {
                case "status":
                    return Status();
                case "list":
                    return List();
                case "publish":
                    return Publish(Slice(args, 1));
                case "join":
                    return Join(Slice(args, 1));
                default:
                    return Usage();
            }
        }

        private static int Status()
        {
            Console.WriteLine("大厅客户端可用 : " + (LobbyClient.IsAvailable ? "是" : "否"));
            Console.WriteLine("服务地址       : " + (OnlineService.BaseUrl ?? "未配置"));
            Console.WriteLine("应用版本       : " + OnlineService.ShortVersion.ToString(3));
            if (!LobbyClient.IsAvailable)
                Console.WriteLine("配置方式       : campfire config set service.url http://127.0.0.1:5080");
            Console.WriteLine();
            Console.WriteLine("说明：自建大厅与官方 api.mccampfire.top 的大厅互不可见；");
            Console.WriteLine("      但邀请码联机不经服务端，Linux 版与官方 Windows 客户端仍可互通。");
            return 0;
        }

        private static int List()
        {
            if (!RequireService())
                return 2;
            try
            {
                var list = LobbyClient.ListAsync().GetAwaiter().GetResult();
                Console.WriteLine("服务       : " + OnlineService.BaseUrl);
                Console.WriteLine("大厅开启   : " + (list.Enabled ? "是" : "否"));
                Console.WriteLine("公告       : " + (list.Notice.Length > 0 ? list.Notice : "（无）"));
                Console.WriteLine("最低版本   : " + (list.MinAppVersion == null ? "不限" : list.MinAppVersion.ToString()));
                Console.WriteLine("刷新间隔   : " + list.RefreshInterval.TotalSeconds + " 秒");
                Console.WriteLine("可用标签   : " + string.Join(", ", list.Tags));
                Console.WriteLine("房间数     : " + list.Rooms.Count);
                foreach (var room in list.Rooms)
                {
                    Console.WriteLine("  [" + room.Id + "] " + room.Title
                                      + "  房主=" + room.Host
                                      + "  " + room.Players + "/" + (room.MaxPlayers > 0 ? room.MaxPlayers.ToString() : "?")
                                      + (room.Locked ? "  有密码" : "")
                                      + (room.Voice ? "  语音" : ""));
                }
                return 0;
            }
            catch (LobbyException ex)
            {
                Console.Error.WriteLine("大厅请求失败：" + ex.Kind + " — " + ex.Message);
                return 1;
            }
            catch (Exception ex) when (ex is System.Net.Http.HttpRequestException || ex is OperationCanceledException)
            {
                Console.Error.WriteLine("连不上自建服务端：" + ex.Message);
                return 1;
            }
        }

        /// <summary>
        /// 把一个已有的邀请码公开到大厅。默认前台长驻并按服务端给的心跳间隔续约，
        /// Ctrl+C 时撤下；--once 用于脚本/自检，发布后立刻撤下。
        /// 注意：这条命令不创建联机房间，邀请码必须来自另一个正在 host 的客户端。
        /// </summary>
        private static int Publish(string[] args)
        {
            if (!RequireService())
                return 2;

            string codeText = null;
            string title = "Campfire CLI 房间";
            string host = "";
            string version = "";
            string map = "";
            string description = "";
            string password = "";
            int players = 1;
            int maxPlayers = 0;
            bool voice = false;
            bool showRegion = false;
            bool once = false;
            var tags = new List<string>();

            try
            {
                for (int i = 0; i < args.Length; i++)
                {
                    switch (args[i].ToLowerInvariant())
                    {
                        case "--code": codeText = Next(args, ref i); break;
                        case "--title": title = Next(args, ref i); break;
                        case "--host": host = Next(args, ref i); break;
                        case "--version": version = Next(args, ref i); break;
                        case "--map": map = Next(args, ref i); break;
                        case "--description": description = Next(args, ref i); break;
                        case "--password": password = Next(args, ref i); break;
                        case "--players": players = int.Parse(Next(args, ref i)); break;
                        case "--max-players": maxPlayers = int.Parse(Next(args, ref i)); break;
                        case "--tags": tags.AddRange(Next(args, ref i).Split(',')); break;
                        case "--voice": voice = true; break;
                        case "--region": showRegion = true; break;
                        case "--once": once = true; break;
                        default:
                            Console.Error.WriteLine("未知参数：" + args[i]);
                            return 2;
                    }
                }
            }
            catch (Exception ex) when (ex is ArgumentException || ex is FormatException)
            {
                Console.Error.WriteLine("参数错误：" + ex.Message);
                return 2;
            }

            if (string.IsNullOrWhiteSpace(codeText))
            {
                Console.Error.WriteLine("缺少 --code（邀请码必须来自正在 host 的客户端）。");
                return 2;
            }
            RoomCode code = RoomCode.Find(codeText);
            if (code == null)
            {
                Console.Error.WriteLine("邀请码格式不正确：" + codeText);
                return 2;
            }

            var listing = new LobbyListing
            {
                Code = code,
                Title = title,
                Host = host,
                Version = version,
                Map = map,
                Tags = tags,
                MaxPlayers = maxPlayers,
                ShowRegion = showRegion,
                Password = password,
                Icon = null,
                Description = description,
                Voice = voice,
            };

            using var cts = new CancellationTokenSource();
            Console.CancelKeyPress += (s, e) => { e.Cancel = true; cts.Cancel(); };
            LobbyTicket ticket = null;
            try
            {
                ticket = LobbyClient.PublishAsync(listing, players, cts.Token).GetAwaiter().GetResult();
                Console.WriteLine("已公开到大厅：");
                Console.WriteLine("  房间 ID    : " + ticket.Id);
                Console.WriteLine("  标题       : " + ticket.Title);
                Console.WriteLine("  邀请码     : " + code.Code);
                Console.WriteLine("  心跳间隔   : " + ticket.HeartbeatInterval.TotalSeconds + " 秒");
                if (once)
                {
                    Console.WriteLine("--once：发布成功，立即撤下。");
                    return 0;
                }
                Console.WriteLine("按 Ctrl+C 撤下房间。");

                TimeSpan wait = ticket.HeartbeatInterval;
                while (!cts.IsCancellationRequested)
                {
                    try
                    {
                        Task.Delay(wait, cts.Token).GetAwaiter().GetResult();
                    }
                    catch (OperationCanceledException)
                    {
                        break;
                    }
                    wait = LobbyClient.HeartbeatAsync(ticket, players, maxPlayers, voice, cts.Token)
                        .GetAwaiter().GetResult();
                    Console.WriteLine("[" + DateTime.Now.ToString("HH:mm:ss") + "] 心跳正常，下次 " + wait.TotalSeconds + " 秒后。");
                }
                return 0;
            }
            catch (OperationCanceledException)
            {
                return 0;
            }
            catch (LobbyException ex)
            {
                Console.Error.WriteLine("大厅发布失败：" + ex.Kind + " — " + ex.Message);
                return 1;
            }
            finally
            {
                if (ticket != null)
                {
                    try
                    {
                        LobbyClient.CloseAsync(ticket).GetAwaiter().GetResult();
                        Console.WriteLine("已从大厅撤下。");
                    }
                    catch (Exception)
                    {
                        Console.Error.WriteLine("撤下房间失败（服务端会按心跳超时自动清理）。");
                    }
                }
            }
        }

        /// <summary>从大厅换取邀请码，再交给标准的 join 流程。</summary>
        private static int Join(string[] args)
        {
            if (!RequireService())
                return 2;
            string id = null;
            string password = null;
            try
            {
                for (int i = 0; i < args.Length; i++)
                {
                    switch (args[i].ToLowerInvariant())
                    {
                        case "--id": id = Next(args, ref i); break;
                        case "--password": password = Next(args, ref i); break;
                        default:
                            if (id == null && !args[i].StartsWith("--"))
                                id = args[i];
                            else
                            {
                                Console.Error.WriteLine("未知参数：" + args[i]);
                                return 2;
                            }
                            break;
                    }
                }
            }
            catch (ArgumentException ex)
            {
                Console.Error.WriteLine("参数错误：" + ex.Message);
                return 2;
            }

            if (string.IsNullOrWhiteSpace(id))
            {
                Console.Error.WriteLine("用法：campfire lobby join <房间ID> [--password 密码]");
                return 2;
            }
            try
            {
                RoomCode code = LobbyClient.GetCodeAsync(id, password).GetAwaiter().GetResult();
                Console.WriteLine("邀请码     : " + code.Code);
                Console.WriteLine("下一步     : campfire join --code " + code.Code);
                return 0;
            }
            catch (LobbyException ex)
            {
                Console.Error.WriteLine("取邀请码失败：" + ex.Kind + " — " + ex.Message);
                return 1;
            }
        }

        private static bool RequireService()
        {
            if (LobbyClient.IsAvailable)
                return true;
            Console.Error.WriteLine("没有配置自建大厅地址，大厅不可用。");
            Console.Error.WriteLine("先用 campfire config set service.url http://127.0.0.1:5080 或 --service-url 指定。");
            return false;
        }

        private static string Next(string[] args, ref int i)
        {
            if (i + 1 >= args.Length)
                throw new ArgumentException("缺少参数值：" + args[i]);
            return args[++i];
        }

        private static string[] Slice(string[] args, int start)
        {
            var result = new string[Math.Max(0, args.Length - start)];
            Array.Copy(args, start, result, 0, result.Length);
            return result;
        }

        private static int Usage()
        {
            Console.Error.WriteLine("用法：campfire lobby <status|list|publish|join>");
            Console.Error.WriteLine("  status                        查看服务地址与版本");
            Console.Error.WriteLine("  list                          列出公开房间");
            Console.Error.WriteLine("  publish --code U/XXXX-... [--title 标题] [--host 房主] [--password 密码]");
            Console.Error.WriteLine("          [--players N] [--max-players N] [--tags a,b] [--map 地图]");
            Console.Error.WriteLine("          [--version 版本] [--description 简介] [--voice] [--region] [--once]");
            Console.Error.WriteLine("  join <房间ID> [--password 密码]  换取邀请码");
            return 2;
        }
    }
}
