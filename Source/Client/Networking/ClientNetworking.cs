using Ionic.Zlib;
using LiteNetLib;
using Multiplayer.Common;
using Multiplayer.Client.EarlyPatches;
using RimWorld;
using Steamworks;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Threading;
using System.ComponentModel;
using Verse;
using UnityEngine;
using System.IO;

namespace Multiplayer.Client
{
    public static class ClientUtil
    {
        public static void TryConnect(string address, int port)
        {
            Multiplayer.session = new MultiplayerSession();

            // todo
            //Multiplayer.session.mods.remoteAddress = address;
            //Multiplayer.session.mods.remotePort = port;

            NetManager netClient = new NetManager(new MpClientNetListener())
            {
                EnableStatistics = true,
                IPv6Enabled = IPv6Mode.Disabled
            };
            
            netClient.Start();
            netClient.ReconnectDelay = 300;
            netClient.MaxConnectAttempts = 8;

            Multiplayer.session.netClient = netClient;
            netClient.Connect(address, port, "");
        }

        public static void HostServer(ServerSettings settings, bool fromReplay, bool withSimulation = false, bool debugMode = false, bool logDesyncTraces = false)
        {
            Log.Message($"Starting the server");

            var session = Multiplayer.session = new MultiplayerSession();
            session.myFactionId = Faction.OfPlayer.loadID;
            session.localSettings = settings;
            session.gameName = settings.gameName;

            var localServer = new MultiplayerServer(settings);

            if (withSimulation)
            {
                localServer.savedGame = GZipStream.CompressBuffer(OnMainThread.cachedGameData);
                localServer.mapData = OnMainThread.cachedMapData.ToDictionary(kv => kv.Key, kv => GZipStream.CompressBuffer(kv.Value));
                localServer.mapCmds = OnMainThread.cachedMapCmds.ToDictionary(kv => kv.Key, kv => kv.Value.Select(c => c.Serialize()).ToList());
            }
            else
            {
                OnMainThread.ClearCaches();
            }

            localServer.debugMode = debugMode;
            localServer.debugOnlySyncCmds = Sync.handlers.Where(h => h.debugOnly).Select(h => h.syncId).ToHashSet();
            localServer.hostOnlySyncCmds = Sync.handlers.Where(h => h.hostOnly).Select(h => h.syncId).ToHashSet();
            localServer.hostUsername = Multiplayer.username;
            localServer.coopFactionId = Faction.OfPlayer.loadID;

            localServer.rwVersion = /*session.mods.remoteRwVersion =*/ VersionControl.CurrentVersionString; // todo
            // localServer.modNames = session.mods.remoteModNames = LoadedModManager.RunningModsListForReading.Select(m => m.Name).ToArray();
            // localServer.modIds = session.mods.remoteModIds = LoadedModManager.RunningModsListForReading.Select(m => m.PackageId).ToArray();
            // localServer.workshopModIds = session.mods.remoteWorkshopModIds = ModManagement.GetEnabledWorkshopMods().ToArray();
            localServer.defInfos = /*session.mods.defInfo =*/ Multiplayer.localDefInfos;
            localServer.serverData = JoinData.WriteServerData();
            
            //Log.Message($"MP Host modIds: {string.Join(", ", localServer.modIds)}");
            //Log.Message($"MP Host workshopIds: {string.Join(", ", localServer.workshopModIds)}");

            if (settings.steam)
                localServer.NetTick += SteamIntegration.ServerSteamNetTick;

            if (fromReplay)
                localServer.gameTimer = TickPatch.Timer;

            MultiplayerServer.instance = localServer;
            session.localServer = localServer;

            if (!fromReplay)
                SetupGameFromSingleplayer();

            foreach (var tickable in TickPatch.AllTickables)
                tickable.Cmds.Clear();

            Find.PlaySettings.usePlanetDayNightSystem = false;

            Multiplayer.RealPlayerFaction = Faction.OfPlayer;
            localServer.playerFactions[Multiplayer.username] = Faction.OfPlayer.loadID;

            SetupLocalClient();

            Find.MainTabsRoot.EscapeCurrentTab(false);

            Multiplayer.session.AddMsg("If you are having any issues with the mod and would like some help resolving them, then please reach out to us on our Discord server:", false);
            Multiplayer.session.AddMsg(new ChatMsg_Url("https://discord.gg/S4bxXpv"), false);

            if (withSimulation)
            {
                StartServerThread();
            }
            else
            {
                var timeSpeed = TimeSpeed.Paused;

                Multiplayer.WorldComp.TimeSpeed = timeSpeed;
                foreach (var map in Find.Maps)
                    map.AsyncTime().TimeSpeed = timeSpeed;
                Multiplayer.WorldComp.UpdateTimeSpeed();

                Multiplayer.WorldComp.debugMode = debugMode;
                Multiplayer.WorldComp.logDesyncTraces = logDesyncTraces;

                LongEventHandler.QueueLongEvent(() =>
                {
                    SaveLoad.CacheGameData(SaveLoad.SaveAndReload());
                    SaveLoad.SendCurrentGameData(false);

                    StartServerThread();
                }, "MpSaving", false, null);
            }

            void StartServerThread()
            {
                var netStarted = localServer.StartListeningNet();
                var lanStarted = localServer.StartListeningLan();

                string text = "Server started.";

                if (netStarted != null)
                    text += (netStarted.Value ? $" Direct at {settings.bindAddress}:{localServer.NetPort}." : " Couldn't bind direct.");

                if (lanStarted != null)
                    text += (lanStarted.Value ? $" LAN at {settings.lanAddress}:{localServer.LanPort}." : " Couldn't bind LAN.");

                session.serverThread = new Thread(localServer.Run)
                {
                    Name = "Local server thread"
                };
                session.serverThread.Start();

                Messages.Message(text, MessageTypeDefOf.SilentInput, false);
                Log.Message(text);
            }
        }

        private static void SetupGameFromSingleplayer()
        {
            MultiplayerWorldComp comp = new MultiplayerWorldComp(Find.World);

            Faction NewFaction(int id, string name, FactionDef def)
            {
                Faction faction = Find.FactionManager.AllFactions.FirstOrDefault(f => f.loadID == id);

                if (faction == null)
                {
                    faction = new Faction() { loadID = id, def = def };

                    foreach (Faction other in Find.FactionManager.AllFactionsListForReading)
                        faction.TryMakeInitialRelationsWith(other);

                    Find.FactionManager.Add(faction);

                    comp.factionData[faction.loadID] = FactionWorldData.New(faction.loadID);
                }

                faction.Name = name;
                faction.def = def;

                return faction;
            }

            Faction dummyFaction = NewFaction(-1, "Multiplayer dummy faction", Multiplayer.DummyFactionDef);

            Faction.OfPlayer.Name = $"{Multiplayer.username}'s faction";
            //comp.factionData[Faction.OfPlayer.loadID] = FactionWorldData.FromCurrent();

            Multiplayer.game = new MultiplayerGame
            {
                dummyFaction = dummyFaction,
                worldComp = comp
            };

            comp.globalIdBlock = new IdBlock(GetMaxUniqueId(), 1_000_000_000);

            //var opponent = NewFaction(Multiplayer.GlobalIdBlock.NextId(), "Opponent", Multiplayer.FactionDef);
            //opponent.TrySetRelationKind(Faction.OfPlayer, FactionRelationKind.Hostile, false);

            foreach (FactionWorldData data in comp.factionData.Values)
            {
                foreach (DrugPolicy p in data.drugPolicyDatabase.policies)
                    p.uniqueId = Multiplayer.GlobalIdBlock.NextId();

                foreach (Outfit o in data.outfitDatabase.outfits)
                    o.uniqueId = Multiplayer.GlobalIdBlock.NextId();

                foreach (FoodRestriction o in data.foodRestrictionDatabase.foodRestrictions)
                    o.id = Multiplayer.GlobalIdBlock.NextId();
            }

            foreach (Map map in Find.Maps)
            {
                //mapComp.mapIdBlock = localServer.NextIdBlock();

                BeforeMapGeneration.SetupMap(map);
                //BeforeMapGeneration.InitNewMapFactionData(map, opponent);

                MapAsyncTimeComp async = map.AsyncTime();
                async.mapTicks = Find.TickManager.TicksGame;
                async.TimeSpeed = Find.TickManager.CurTimeSpeed;
            }

            Multiplayer.WorldComp.UpdateTimeSpeed();
        }

        private static void SetupLocalClient()
        {
            if (Multiplayer.session.localSettings.arbiter)
                StartArbiter();

            LocalClientConnection localClient = new LocalClientConnection(Multiplayer.username);
            LocalServerConnection localServerConn = new LocalServerConnection(Multiplayer.username);

            localServerConn.clientSide = localClient;
            localClient.serverSide = localServerConn;

            localClient.State = ConnectionStateEnum.ClientPlaying;
            localServerConn.State = ConnectionStateEnum.ServerPlaying;

            var serverPlayer = Multiplayer.LocalServer.OnConnected(localServerConn);
            serverPlayer.color = new ColorRGB(255, 0, 0);
            serverPlayer.status = PlayerStatus.Playing;
            serverPlayer.SendPlayerList();

            Multiplayer.session.client = localClient;
            Multiplayer.session.ReapplyPrefs();
        }

        private static void StartArbiter()
        {
            Multiplayer.session.AddMsg("The Arbiter instance is starting...", false);

            Multiplayer.LocalServer.SetupArbiterConnection();

            string args = $"-batchmode -nographics -arbiter -logfile arbiter_log.txt -connect=127.0.0.1:{Multiplayer.LocalServer.ArbiterPort}";

            if (GenCommandLine.TryGetCommandLineArg("savedatafolder", out string saveDataFolder))
                args += $" \"-savedatafolder={saveDataFolder}\"";

            string arbiterInstancePath;
            if (Application.platform == RuntimePlatform.OSXPlayer) {
                arbiterInstancePath = Application.dataPath + "/MacOS/" + Process.GetCurrentProcess().MainModule.ModuleName;
            } else {
                arbiterInstancePath = Process.GetCurrentProcess().MainModule.FileName;
            }

            try
            {
                Multiplayer.session.arbiter = Process.Start(
                    arbiterInstancePath,
                    args
                );
            }
            catch (Exception ex)
            {
                Multiplayer.session.AddMsg("Arbiter failed to start.", false);
                Log.Error("Arbiter failed to start.", false);
                Log.Error(ex.ToString());
                if (ex.InnerException is Win32Exception)
                {
                    Log.Error("Win32 Error Code: " + ((Win32Exception)ex).NativeErrorCode.ToString());
                }
            }
        }

        public static int GetMaxUniqueId()
        {
            return typeof(UniqueIDsManager)
                .GetFields(BindingFlags.NonPublic | BindingFlags.Instance)
                .Where(f => f.FieldType == typeof(int))
                .Select(f => (int)f.GetValue(Find.UniqueIDsManager))
                .Max();
        }
    }

    public class MpClientNetListener : INetEventListener
    {
        public void OnPeerConnected(NetPeer peer)
        {
            IConnection conn = new MpNetConnection(peer);
            conn.username = Multiplayer.username;
            conn.State = ConnectionStateEnum.ClientJoining;

            Multiplayer.session.client = conn;
            Multiplayer.session.ReapplyPrefs();

            MpLog.Log("Net client connected");
        }

        public void OnNetworkError(IPEndPoint endPoint, SocketError error)
        {
            Log.Warning($"Net client error {error}");
        }

        public void OnNetworkReceive(NetPeer peer, NetPacketReader reader, DeliveryMethod method)
        {
            byte[] data = reader.GetRemainingBytes();
            Multiplayer.HandleReceive(new ByteReader(data), method == DeliveryMethod.ReliableOrdered);
        }

        public void OnPeerDisconnected(NetPeer peer, DisconnectInfo info)
        {
            MpDisconnectReason reason;
            byte[] data;

            if (info.AdditionalData.IsNull)
            {
                reason = MpDisconnectReason.Failed;
                data = new byte[0];
            }
            else
            {
                var reader = new ByteReader(info.AdditionalData.GetRemainingBytes());
                reason = (MpDisconnectReason)reader.ReadByte();
                data = reader.ReadPrefixedBytes();
            }

            Multiplayer.session.HandleDisconnectReason(reason, data);

            ConnectionStatusListeners.TryNotifyAll_Disconnected();

            OnMainThread.StopMultiplayer();
            MpLog.Log("Net client disconnected");
        }

        private static string DisconnectReasonString(DisconnectReason reason)
        {
            switch (reason)
            {
                case DisconnectReason.ConnectionFailed: return "Connection failed";
                case DisconnectReason.ConnectionRejected: return "Connection rejected";
                case DisconnectReason.Timeout: return "Timed out";
                case DisconnectReason.HostUnreachable: return "Host unreachable";
                case DisconnectReason.InvalidProtocol: return "Invalid library protocol";
                default: return "Disconnected";
            }
        }

        public void OnConnectionRequest(ConnectionRequest request) { }
        public void OnNetworkLatencyUpdate(NetPeer peer, int latency) { }
        public void OnNetworkReceiveUnconnected(IPEndPoint remoteEndPoint, NetPacketReader reader, UnconnectedMessageType messageType) { }
    }

    // Within same process
    public class LocalClientConnection : IConnection
    {
        public LocalServerConnection serverSide;

        public override int Latency { get => 0; set { } }

        public LocalClientConnection(string username)
        {
            this.username = username;
        }

        protected override void SendRaw(byte[] raw, bool reliable)
        {
            Multiplayer.LocalServer.Enqueue(() =>
            {
                try
                {
                    serverSide.HandleReceive(new ByteReader(raw), reliable);
                }
                catch (Exception e)
                {
                    Log.Error($"Exception handling packet by {serverSide}: {e}");
                }
            });
        }

        public override void Close(MpDisconnectReason reason, byte[] data)
        {
        }

        public override string ToString()
        {
            return "LocalClientConn";
        }
    }

    // Within same process
    public class LocalServerConnection : IConnection
    {
        public LocalClientConnection clientSide;

        public override int Latency { get => 0; set { } }

        public LocalServerConnection(string username)
        {
            this.username = username;
        }

        protected override void SendRaw(byte[] raw, bool reliable)
        {
            OnMainThread.Enqueue(() =>
            {
                try
                {
                    clientSide.HandleReceive(new ByteReader(raw), reliable);
                }
                catch (Exception e)
                {
                    Log.Error($"Exception handling packet by {clientSide}: {e}");
                }
            });
        }

        public override void Close(MpDisconnectReason reason, byte[] data)
        {
        }

        public override string ToString()
        {
            return "LocalServerConn";
        }
    }

    public abstract class SteamBaseConn : IConnection
    {
        public readonly CSteamID remoteId;

        public readonly ushort recvChannel; // currently only for client
        public readonly ushort sendChannel; // currently only for server

        public SteamBaseConn(CSteamID remoteId, ushort recvChannel, ushort sendChannel)
        {
            this.remoteId = remoteId;
            this.recvChannel = recvChannel;
            this.sendChannel = sendChannel;
        }

        protected override void SendRaw(byte[] raw, bool reliable)
        {
            byte[] full = new byte[1 + raw.Length];
            full[0] = reliable ? (byte)2 : (byte)0;
            raw.CopyTo(full, 1);

            SendRawSteam(full, reliable);
        }

        public void SendRawSteam(byte[] raw, bool reliable)
        {
            SteamNetworking.SendP2PPacket(
                remoteId,
                raw,
                (uint)raw.Length,
                reliable ? EP2PSend.k_EP2PSendReliable : EP2PSend.k_EP2PSendUnreliable,
                sendChannel
            );
        }

        public override void Close(MpDisconnectReason reason, byte[] data)
        {
            if (State == ConnectionStateEnum.ClientSteam) return;

            Send(Packets.Special_Steam_Disconnect, GetDisconnectBytes(reason, data));
        }

        protected override void HandleReceive(int msgId, int fragState, ByteReader reader, bool reliable)
        {
            if (msgId == (int)Packets.Special_Steam_Disconnect)
            {
                OnDisconnect();
            }
            else
            {
                base.HandleReceive(msgId, fragState, reader, reliable);
            }
        }

        public virtual void OnError(EP2PSessionError error)
        {
            OnDisconnect();
        }

        protected abstract void OnDisconnect();

        public override string ToString()
        {
            return $"SteamP2P ({remoteId}) ({username})";
        }
    }

    public class SteamClientConn : SteamBaseConn
    {
        static ushort RandomChannelId() => (ushort)new System.Random().Next();

        public SteamClientConn(CSteamID remoteId) : base(remoteId, RandomChannelId(), 0)
        {
        }

        protected override void HandleReceive(int msgId, int fragState, ByteReader reader, bool reliable)
        {
            if (msgId == (int)Packets.Special_Steam_Disconnect)
                Multiplayer.session.HandleDisconnectReason((MpDisconnectReason)reader.ReadByte(), reader.ReadPrefixedBytes());

            base.HandleReceive(msgId, fragState, reader, reliable);
        }

        public override void OnError(EP2PSessionError error)
        {
            Multiplayer.session.disconnectReasonKey = error == EP2PSessionError.k_EP2PSessionErrorTimeout ? "Connection timed out" : "Connection error";
            base.OnError(error);
        }

        protected override void OnDisconnect()
        {
            ConnectionStatusListeners.TryNotifyAll_Disconnected();
            OnMainThread.StopMultiplayer();
        }
    }

    public class SteamServerConn : SteamBaseConn
    {
        public SteamServerConn(CSteamID remoteId, ushort clientChannel) : base(remoteId, 0, clientChannel)
        {
        }

        protected override void OnDisconnect()
        {
            serverPlayer.Server.OnDisconnected(this, MpDisconnectReason.ClientLeft);
        }
    }

}