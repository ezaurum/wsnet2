using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace WSNet2.DotnetClient
{
    public class StrMessage : IWSNet2Serializable
    {
        string str;

        public StrMessage() { }
        public StrMessage(string str)
        {
            this.str = str;
        }

        public void Serialize(SerialWriter writer)
        {
            writer.Write(str);
        }

        public void Deserialize(SerialReader reader, int len)
        {
            str = reader.ReadString();
        }

        public override string ToString()
        {
            return str;
        }
    }

    public class DotnetClient
    {
        const uint SearchGroup = 100;

        static AuthDataGenerator authgen = new AuthDataGenerator();

        static bool showPong = false;
        static bool showNetInfo = false;

        static Dictionary<string, Action<Room, string>> cmds = new Dictionary<string, Action<Room, string>>()
        {
            {
                "leave",
                (room, p) => {
                    room.Leave(p);
                }
            },
            {
                "kick",
                (room, p) => {
                    try
                    {
                        var ps = p.Split(" ", 2);
                        room.Kick(room.Players[ps[0]], ((ps.Length>1)? ps[1]: ""));
                    }
                    catch (Exception e)
                    {
                        Console.WriteLine($"kick error: {e.Message}");
                    }
                }
            },
            {
                "switchmaster",
                (room, p)=> {
                    Console.WriteLine($"switch master to {p}");
                    try
                    {
                        room.SwitchMaster(
                            room.Players[p],
                            (t, id) => Console.WriteLine($"SwitchMaster({id}) error: {t}"));
                    }
                    catch (Exception e)
                    {
                        Console.WriteLine($"switch master error: {e.Message}");
                    }
                }
            },
            {
                "roomprop",
                (room, p) => {
                    var strs = p.Split(' ');
                    var joinable = !room.Joinable;
                    var deadline = room.ClientDeadline + 3;
                    var pubProps = new Dictionary<string, object>();
                    if (strs.Length > 0)
                    {
                        pubProps["public-modify"] = strs[0];
                    }
                    Dictionary<string, object> privProps = null;
                    if (strs.Length > 1)
                    {
                        privProps = new Dictionary<string, object>(){
                            {"private-modify", strs[1]},
                        };
                    }
                    room.ChangeRoomProperty(
                        joinable: joinable,
                        clientDeadline: deadline,
                        publicProps: pubProps,
                        privateProps: privProps,
                        onErrorResponse: (t, v, j, w, sg, mp, cd, pub, priv) =>
                        {
                            var f = !v.HasValue ? "-" : v.Value ? "V" : "x";
                            f += !j.HasValue ? "-" : j.Value ? "J" : "x";
                            f += !w.HasValue ? "-" : w.Value ? "W" : "x";
                            var pubp = "";
                            if (pub != null)
                            {
                                foreach (var kv in pub)
                                {
                                    pubp += $"{kv.Key}:{kv.Value},";
                                }
                            }
                            var prip = "";
                            if (priv != null)
                            {
                                foreach (var kv in priv)
                                {
                                    prip += $"{kv.Key}:{kv.Value},";
                                }
                            }
                            Console.WriteLine($"OnRoomPropertyChanged {t}: flg={f} sg={sg} mp={mp} cd={cd} pub={pubp} priv={prip}");
                        });
                }
            },
            {
                "myprop",
                (room, p) => {
                    var strs = p.Split(' ');
                    if (strs.Length != 2)
                    {
                        Console.WriteLine("invalid param: myprop <key> <value>");
                        return;
                    }

                    room.ChangeMyProperty(new Dictionary<string, object>() { { strs[0], strs[1] } });
                }
            },
            {
                "pause",
                (room, p) => {
                    room.Pause();
                    Console.WriteLine("room paused");
                }
            },
            {
                "restart",
                (room, p) => {
                    room.Restart();
                    Console.WriteLine("room restarted");
                }
            },
            {
                "showpong",
                (room, p) => {
                    showPong = !showPong;
                    Console.WriteLine("show pong " + (showPong ? "on" : "off"));
                }
            },
            {
                "netinfo",
                (room, p) => {
                    showNetInfo = !showNetInfo;
                    Console.WriteLine("netinfo " + (showNetInfo ? "on" : "off"));
                }
            },
        };

        static bool doAsCmd(Room room, string str)
        {
            if (str[0] != '!')
            {
                return false;
            }

            var ss = str.Substring(1).Split(" ", 2);
            if (!cmds.ContainsKey(ss[0]))
            {
                Console.WriteLine("commands: " + string.Join(" ", cmds.Keys));
                return true;
            }

            cmds[ss[0]](room, (ss.Length>1) ? ss[1] : "");
            return true;
        }

        static async Task callbackrunner(WSNet2Client cli, CancellationToken ct)
        {
            while (true)
            {
                ct.ThrowIfCancellationRequested();
                cli.ProcessCallback();
                await Task.Delay(1000);
            }
        }

        static void RPCMessage(string senderId, StrMessage msg)
        {
            Console.WriteLine($"OnRPCMessage [{senderId}]: {msg}");
        }
        static void RPCMessages(string senderId, StrMessage[] msgs)
        {
            var strs = string.Join<StrMessage>(',', msgs);
            Console.WriteLine($"OnRPCMessages [{senderId}]: {strs}");
        }
        static void RPCString(string senderId, string str)
        {
            Console.WriteLine($"OnRPCString [{senderId}]: {str}");
        }

        enum Cmd
        {
            create,
            join,
            search,
            ids,
            nums,
            watch,
        }

        static void printHelp()
        {
            var cmds = string.Join(", ", Enum.GetNames(typeof(Cmd)));
            Console.WriteLine("Usage: dotnet run <command> [params...]");
            Console.WriteLine($"Command: {cmds}");
        }

        static Cmd? getCmd(string[] args)
        {
            if (args.Length > 0)
            {
                var arg = args[0];
                foreach (var c in Enum.GetValues(typeof(Cmd)))
                {
                    if (arg == c.ToString())
                    {
                        return (Cmd)c;
                    }
                }
            }
            return null;
        }

        static async Task Search(WSNet2Client client)
        {
            var query = new Query();
            query.Between("bbb", 20, 80);
            query.Contain("ccc", "a");
            query.Contain("ddd", 2);
            query.Contain("eee", 1.1);

            var roomsrc = new TaskCompletionSource<PublicRoom[]>(TaskCreationOptions.RunContinuationsAsynchronously);

            client.Search(
                SearchGroup,
                query,
                10,
                true,
                false,
                (rs) =>
                {
                    Console.WriteLine($"onSuccess: {rs.Length}");
                    roomsrc.TrySetResult(rs);
                },
                (e) =>
                {
                    Console.WriteLine($"onFailed: {e}");
                    roomsrc.TrySetCanceled();
                });

            PrintRooms(await roomsrc.Task);
        }


        static async Task SearchByIds(WSNet2Client client, string[] ids)
        {
            var roomsrc = new TaskCompletionSource<PublicRoom[]>(TaskCreationOptions.RunContinuationsAsynchronously);

            client.Search(
                ids, null,
                (rs) =>
                {
                    Console.WriteLine($"onSuccess: {rs.Length}");
                    roomsrc.TrySetResult(rs);
                },
                (e) =>
                {
                    Console.WriteLine($"onFailed: {e}");
                    roomsrc.TrySetCanceled();
                });

            PrintRooms(await roomsrc.Task);
        }

        static async Task SearchByNums(WSNet2Client client, int[] numbers)
        {
            var roomsrc = new TaskCompletionSource<PublicRoom[]>(TaskCreationOptions.RunContinuationsAsynchronously);

            client.Search(
                numbers, null,
                (rs) =>
                {
                    Console.WriteLine($"onSuccess: {rs.Length}");
                    roomsrc.TrySetResult(rs);
                },
                (e) =>
                {
                    Console.WriteLine($"onFailed: {e}");
                    roomsrc.TrySetCanceled();
                });

            PrintRooms(await roomsrc.Task);
        }

        static void PrintRooms(PublicRoom[] rooms)
        {
            Console.WriteLine("rooms:");
            foreach (var room in rooms)
            {
                var props = "";
                foreach (var kv in room.PublicProps)
                {
                    props += $"{kv.Key}:{kv.Value},";
                }
                Console.WriteLine($"{room.Id} #{room.Number:D3} {room.PlayerCount}/{room.MaxPlayers} [{props}] {room.Created}");
            }
        }

        static async Task Main(string[] args)
        {
            var cmd = getCmd(args);
            if (cmd == null)
            {
                printHelp();
                return;
            }

            var rand = new Random();
            var userid = ((cmd == Cmd.join || cmd == Cmd.watch) && args.Length > 2) ? args[2] : $"user{rand.Next(99):000}";
            Console.WriteLine($"user id: {userid}");

            WSNet2Serializer.Register<StrMessage>(0);

            NetworkInformer.SetCallback(NetInfoCallback);

            var authData = authgen.Generate("testapppkey", userid);

            var client = new WSNet2Client(
                "http://localhost:8080",
                "testapp",
                userid,
                authData,
                null);

            var cts = new CancellationTokenSource();
            _ = Task.Run(async () => await callbackrunner(client, cts.Token));

            switch (cmd.Value)
            {
                case Cmd.search:
                    await Search(client);
                    cts.Cancel();
                    return;
                case Cmd.ids:
                    await SearchByIds(client, args.Skip(1).ToArray());
                    cts.Cancel();
                    return;
                case Cmd.nums:
                    await SearchByNums(client, args.Skip(1).Select(int.Parse).ToArray());
                    cts.Cancel();
                    return;
            }

            var cliProps = new Dictionary<string, object>(){
                {"name", userid},
            };

            var roomJoined = new TaskCompletionSource<Room>(TaskCreationOptions.RunContinuationsAsynchronously);
            Action<Room> onJoined = (Room room) =>
            {
                roomJoined.TrySetResult(room);

                room.OnError += (e) => Console.WriteLine($"OnError: {e}");
                room.OnErrorClosed += (e) => Console.WriteLine($"OnErrorClosed: {e}");
                room.OnJoined += (me) => Console.WriteLine($"OnJoined: {me.Id}");
                room.OnClosed += (m) => Console.WriteLine($"OnClosed: {m}");
                room.OnOtherPlayerJoined += (p) => Console.WriteLine($"OnOtherPlayerJoined: {p.Id}");
                room.OnOtherPlayerRejoined += (p) => Console.WriteLine($"OnOtherPlayerRejoined: {p.Id}");
                room.OnOtherPlayerLeft += (p, m) => Console.WriteLine($"OnOtherplayerleft: {p.Id}: {m}");
                room.OnMasterPlayerSwitched += (p, n) => Console.WriteLine($"OnMasterPlayerSwitched: {p.Id} -> {n.Id}");
                room.OnPongReceived += (r, w, ts) => {
                    if (showPong) Console.WriteLine(
                        $"onPong: RTT={r} Watchers={w} LMTS={{"+ts.Select(kv => $"{kv.Key}:{kv.Value}").Aggregate((a,s)=>$"{a},{s}")+"}");
                };
                room.OnRoomPropertyChanged += (visible, joinable, watchable, searchGroup, maxPlayers, clientDeadline, publicProps, privateProps) =>
                {
                    var flags = !visible.HasValue ? "-" : visible.Value ? "V" : "x";
                    flags += !joinable.HasValue ? "-" : joinable.Value ? "J" : "x";
                    flags += !watchable.HasValue ? "-" : watchable.Value ? "W" : "x";
                    var pubp = publicProps?.Select(kv => $"{kv.Key}:{kv.Value}").Aggregate((a, s) => $"{a},{s}");
                    var prip = privateProps?.Select(kv => $"{kv.Key}:{kv.Value}").Aggregate((a, s) => $"{a},{s}");

                    Console.WriteLine($"OnRoomPropertyChanged: flg={flags} sg={searchGroup} mp={maxPlayers} cd={clientDeadline} pub={pubp} priv={prip}");
                };
                room.OnPlayerPropertyChanged += (p, props) =>
                {
                    var propstr = props?.Select(kv => $"{kv.Key}:{kv.Value}").Aggregate((a, s) => $"{a},{s}");
                    Console.WriteLine($"OnPlayerPropertyChanged: {p.Id} {propstr}");
                };
                room.OnClosed += (_) => cts.Cancel();
                room.OnErrorClosed += (_) => cts.Cancel();

                room.RegisterRPC<StrMessage>(RPCMessage);
                room.RegisterRPC<StrMessage>(RPCMessages);
                room.RegisterRPC(RPCString);
            };
            Action<Exception> onFailed = (Exception e) => roomJoined.TrySetException(e);

            if (cmd == Cmd.create)
            {
                // create room
                var pubProps = new Dictionary<string, object>(){
                    {"aaa", "public"},
                    {"bbb", (int)rand.Next(100)},
                    {"ccc", new object[]{1, 3, "a", 3.5f}},
                    {"ddd", new int[]{2, 4, 5, 8}},
                    {"eee", new double[]{-10, 1.1, 0.5}},
                    {"strmcg", new StrMessage("msg!")},
                };
                var privProps = new Dictionary<string, object>(){
                    {"aaa", "private"},
                    {"ccc", false},
                };
                var roomOpt = new RoomOption(10, SearchGroup, pubProps, privProps).WithClientDeadline(30).WithNumber(true);

                client.Create(roomOpt, cliProps, onJoined, onFailed, null);
            }
            else if (cmd == Cmd.join)
            {
                var number = int.Parse(args[1]);
                client.Join(number, null, cliProps, onJoined, onFailed, null);
            }
            else // watch
            {
                var number = int.Parse(args[1]);
                var query = new Query().GreaterEqual("bbb", (int)20);
                client.Watch(number, query, onJoined, onFailed, null);
            }

            try
            {
                var room = await roomJoined.Task;
                var rp = "";
                var rpp = "";
                foreach (var kv in room.PublicProps)
                {
                    rp += $"{kv.Key}:{kv.Value},";
                }
                foreach (var kv in room.PrivateProps)
                {
                    rpp += $"{kv.Key}:{kv.Value},";
                }
                Console.WriteLine($"joined room = {room.Id} [{room.Number}]; pub[{rp}] priv[{rpp}]");

                foreach (var p in room.Players)
                {
                    var pp = $"  player {p.Key}: ";
                    foreach (var kv in p.Value.Props)
                    {
                        pp += $"{kv.Key}:{kv.Value}, ";
                    }
                    Console.WriteLine(pp);
                }

                int i = 0;

                while (true)
                {
                    cts.Token.ThrowIfCancellationRequested();
                    var str = Console.ReadLine();
                    if (str == "")
                    {
                        continue;
                    }

                    cts.Token.ThrowIfCancellationRequested();

                    if (doAsCmd(room, str))
                    {
                        continue;
                    }

                    switch (i % 3)
                    {
                        case 0:
                            Console.WriteLine($"rpc to master: {str}");
                            var msg = new StrMessage(str);
                            room.RPC(RPCMessage, msg, Room.RPCToMaster);
                            break;
                        case 1:
                            var ids = room.Players.Keys.ToArray();
                            var target = ids[rand.Next(ids.Length)];
                            Console.WriteLine($"rpc to {target}: {str}");
                            room.RPC(RPCString, str, target, "nobody");
                            break;
                        case 2:
                            Console.WriteLine($"rpc to all: {str}");
                            var msgs = new StrMessage[]{
                                new StrMessage(str), new StrMessage(str),
                            };
                            room.RPC(RPCMessages, msgs);
                            break;
                    }
                    i++;
                }
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception e)
            {
                Console.WriteLine("exception: " + e);
                cts.Cancel();
            }
        }

        static Dictionary<string, object> emptydict = new Dictionary<string, object>();

        static void NetInfoCallback(NetworkInformer.Info info)
        {
            if (!showNetInfo) return;

            var str = JsonSerializer.Serialize((object)info, new JsonSerializerOptions { IncludeFields = true });
            Console.WriteLine($"NetInfo: {info.GetType().Name} {str}");

            switch (info)
            {
                case NetworkInformer.RoomSendRoomPropInfo i:
                    Console.WriteLine("  public props:");
                    foreach (var kv in WSNet2Serializer.NewReader(i.PublicProps).ReadDict() ?? emptydict)
                    {
                        Console.WriteLine($"    {kv.Key}: {kv.Value}");
                    }
                    Console.WriteLine("  private props:");
                    foreach (var kv in WSNet2Serializer.NewReader(i.PrivateProps).ReadDict() ?? emptydict)
                    {
                        Console.WriteLine($"    {kv.Key}: {kv.Value}");
                    }
                    break;

                case NetworkInformer.RoomSendPlayerPropInfo i:
                    Console.WriteLine("  props:");
                    foreach (var kv in WSNet2Serializer.NewReader(i.Props).ReadDict() ?? emptydict)
                    {
                        Console.WriteLine($"    {kv.Key}: {kv.Value}");
                    }
                    break;

                case NetworkInformer.RoomSendRPCInfo i:
                    Console.WriteLine($"  param: {WSNet2Serializer.NewReader(i.Param).Read()}");
                    break;

                case NetworkInformer.RoomReceiveJoinedInfo i:
                    Console.WriteLine("  props:");
                    foreach (var kv in WSNet2Serializer.NewReader(i.Props).ReadDict() ?? emptydict)
                    {
                        Console.WriteLine($"    {kv.Key}: {kv.Value}");
                    }
                    break;

                case NetworkInformer.RoomReceiveRoomPropInfo i:
                    Console.WriteLine("  public props:");
                    foreach (var kv in WSNet2Serializer.NewReader(i.PublicProps).ReadDict() ?? emptydict)
                    {
                        Console.WriteLine($"    {kv.Key}: {kv.Value}");
                    }
                    Console.WriteLine("  private props:");
                    foreach (var kv in WSNet2Serializer.NewReader(i.PrivateProps).ReadDict() ?? emptydict)
                    {
                        Console.WriteLine($"    {kv.Key}: {kv.Value}");
                    }
                    break;

                case NetworkInformer.RoomReceivePlayerPropInfo i:
                    Console.WriteLine("  props:");
                    foreach (var kv in WSNet2Serializer.NewReader(i.Props).ReadDict() ?? emptydict)
                    {
                        Console.WriteLine($"    {kv.Key}: {kv.Value}");
                    }
                    break;

                case NetworkInformer.RoomReceiveRPCInfo i:
                    Console.WriteLine($"  param: {WSNet2Serializer.NewReader(i.Param).Read()}");
                    break;
            }
        }
    }
}
