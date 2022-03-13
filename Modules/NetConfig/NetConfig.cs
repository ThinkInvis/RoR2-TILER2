using BepInEx.Configuration;
using BepInEx.Logging;
using R2API;
using R2API.Networking;
using R2API.Networking.Interfaces;
using RoR2;
using RoR2.Networking;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEngine.Networking;
using static RoR2.Networking.NetworkManagerSystem;
using Compression = System.IO.Compression;

namespace TILER2 {
    /// <summary>
    /// Provides automatic network syncing and mismatch kicking for the AutoConfig module.
    /// </summary>
    public class NetConfig : T2Module<NetConfig> {
        public override bool managedEnable => false;

        [AutoConfig("If true, NetConfig will use the server to check for config mismatches.")]
        public bool enableCheck {get; private set;} = true;
        [AutoConfig("If true, NetConfig will kick clients that fail config checks (caused by config entries internally marked as both DeferForever and DisallowNetMismatch).")]
        public bool mismatchKick {get; private set;} = true;
        [AutoConfig("If true, NetConfig will kick clients that are missing config entries (may be caused by different mod versions on client).")]
        public bool badVersionKick {get; private set;} = true;
        [AutoConfig("If true, NetConfig will kick clients that take too long to respond to config checks (may be caused by missing mods on client, or by major network issues).")]
        public bool timeoutKick {get; private set;} = true;

        public static readonly SimpleLocalizedKickReason kickCritMismatch = new SimpleLocalizedKickReason("TILER2_KICKREASON_NCCRITMISMATCH");
        public static readonly SimpleLocalizedKickReason kickTimeout = new SimpleLocalizedKickReason("TILER2_KICKREASON_NCTIMEOUT");
        public static readonly SimpleLocalizedKickReason kickMissingEntry = new SimpleLocalizedKickReason("TILER2_KICKREASON_NCMISSINGENTRY");

        private static readonly RoR2.ConVar.BoolConVar allowClientNCFGSet = new RoR2.ConVar.BoolConVar("ncfg_allowclientset", ConVarFlags.None, "false", "If true, clients may use the ConCmds ncfg_set or ncfg_settemp to temporarily set config values on the server. If false, ncfg_set and ncfg_settemp will not work for clients.");

        private const float CONN_CHECK_WAIT_TIME = 15f;
        private const int MAX_MESSAGE_SIZE_BYTES = 1100;
        private static readonly List<ClientConfigSyncInfo> clients = new List<ClientConfigSyncInfo>();
        List<ClientConfigSyncInfo> _updateKickList = new List<ClientConfigSyncInfo>();

        //clientside storage for info received from server
        private static string cliPassword;
        private static int cliNetId;
        private static int cliSyncReceiveBytesMax = 0;
        private static int cliSyncReceiveBytes = 0;
        private static Dictionary<int, byte[]> cliSyncReceiveData = new Dictionary<int, byte[]>();

        enum ConfigSyncStatus : byte {
            Invalid, Connect, BeginSync, SyncPass, SyncPassWithChange, SyncWarn, SyncFail
        }

        private class ClientConfigSyncInfo {
            public NetworkConnection connection;
            public string password; //netids are sequential; password detrivializes being able to kick other clients by spamming fail replies
            public bool hasAcked = false;
            public ConfigExchange? currentExchange;
            public readonly List<ConfigExchange> pendingExchanges = new List<ConfigExchange>();
            public readonly float connectedAt = Time.unscaledTime;

            public void AddExchangeOne(AutoConfigBinding bind) {
                AddExchange(new ConfigExchange(new[] {new ConfigExchangeEntry(
                    bind.modName,
                    bind.configEntry.Definition.Section,
                    bind.configEntry.Definition.Key,
                    TomlTypeConverter.ConvertToString(bind.cachedValue, bind.propType)
                )}));
            }
            public void AddExchangeOne(AutoConfigBinding bind, object newValue) {
                AddExchange(new ConfigExchange(new[] {new ConfigExchangeEntry(
                    bind.modName,
                    bind.configEntry.Definition.Section,
                    bind.configEntry.Definition.Key,
                    TomlTypeConverter.ConvertToString(newValue, bind.propType)
                )}));
            }
            public void AddExchangeAll() {
                var validInsts = AutoConfigBinding.instances.Where(x => !x.allowNetMismatch);
                var entries = new List<ConfigExchangeEntry>();

                foreach(var i in validInsts) {
                    entries.Add(new ConfigExchangeEntry(
                        i.modName,
                        i.configEntry.Definition.Section,
                        i.configEntry.Definition.Key,
                        TomlTypeConverter.ConvertToString(i.cachedValue, i.propType)
                        ));
                }

                AddExchange(new ConfigExchange(entries.ToArray()));
            }
            private void AddExchange(ConfigExchange exch) {
                pendingExchanges.Add(exch);
                if(hasAcked && currentExchange == null)
                    AdvanceExchangeQueue();
            }

            public void AdvanceExchangeQueue() {
                if(!hasAcked) {
                    TILER2Plugin._logger.LogError("ClientConfigSyncInfo.AdvanceExchangeQueue called before client ack");
                    return;
                }
                if(currentExchange == null) {
                    if(pendingExchanges.Count == 0) {
                        TILER2Plugin._logger.LogDebug("ClientConfigSyncInfo.AdvanceExchangeQueue called with empty queue");
                        return;
                    }
                    currentExchange = pendingExchanges[0];
                    pendingExchanges.RemoveAt(0);
                    new MsgRequestConfigSyncBegin(currentExchange.Value.content.Length).Send(connection);
                }
            }

            public void BeginExchange() {
                if(!hasAcked) {
                    TILER2Plugin._logger.LogError("ClientConfigSyncInfo.BeginExchange called before client ack");
                    return;
                }
                if(currentExchange == null) {
                    TILER2Plugin._logger.LogWarning("ClientConfigSyncInfo.BeginExchange called with no immediate exchange, attempting to advance queue");
                    AdvanceExchangeQueue();
                    if(currentExchange == null) {
                        TILER2Plugin._logger.LogError("ClientConfigSyncInfo.BeginExchange called with no immediate or pending exchange");
                        return;
                    }
                }
                var payload = currentExchange.Value.content;
                var step = MAX_MESSAGE_SIZE_BYTES - 32;
                var index = 0;
                for(var i = 0; i < payload.Length; i += step) {
                    var next = payload.Skip(i);
                    new MsgRequestConfigSyncContinue(
                        next.Take(Math.Min(step, next.Count())).ToArray(),
                        index++
                        ).Send(connection);
                }
            }

            public void EndExchange() {
                if(!hasAcked) {
                    TILER2Plugin._logger.LogError("ClientConfigSyncInfo.EndExchange called before client ack");
                    return;
                }
                currentExchange = null;
                AdvanceExchangeQueue();
            }
        }

        private struct ConfigExchangeEntry {
            public string modName;
            public string configCategory;
            public string configName;
            public string serializedValue;

            public ConfigExchangeEntry(string modName, string configCategory, string configName, string serializedValue) {
                this.modName = modName;
                this.configCategory = configCategory;
                this.configName = configName;
                this.serializedValue = serializedValue;
            }
        }

        private struct ConfigExchange {
            public byte[] content;
            public float timestamp;

            public ConfigExchange(ConfigExchangeEntry[] entries) {
                var header = new List<int>();
                header.Add(entries.Length*4);
                var interleavedEntries = new List<string>();
                foreach(var i in entries) {
                    interleavedEntries.Add(i.modName);
                    header.Add(i.modName.Length);
                    interleavedEntries.Add(i.configCategory);
                    header.Add(i.configCategory.Length);
                    interleavedEntries.Add(i.configName);
                    header.Add(i.configName.Length);
                    interleavedEntries.Add(i.serializedValue);
                    header.Add(i.serializedValue.Length);
                }
                var serBytes = header.SelectMany(BitConverter.GetBytes)
                    .Concat(System.Text.Encoding.Unicode.GetBytes(string.Join("",interleavedEntries)))
                    .ToArray();

                using(var inputStream = new MemoryStream(serBytes))
                using(var outputStream = new MemoryStream()) {
                    using(var compressor = new Compression.DeflateStream(outputStream, Compression.CompressionMode.Compress)) {
                        inputStream.CopyTo(compressor);
                    }

                    content = outputStream.ToArray();
                }

                timestamp = Time.unscaledTime;
            }

            public ConfigExchange(byte[] packedEntries) {
                content = packedEntries;
                timestamp = Time.unscaledTime;
            }

            public ConfigExchangeEntry[] Unpack() {
                string[] interleavedEntries;
                int[] entryLengths;
                using(var inputStream = new MemoryStream(content))
                using(var outputStream = new MemoryStream()) {
                    using(var compressor = new Compression.DeflateStream(inputStream, Compression.CompressionMode.Decompress)) {
                        compressor.CopyTo(outputStream);
                    }

                    outputStream.Seek(0, SeekOrigin.Begin);

                    //read 1 int: # of entries
                    byte[] outputBuffer = new byte[4];
                    outputStream.Read(outputBuffer, 0, 4);
                    entryLengths = new int[BitConverter.ToInt32(outputBuffer, 0)];
                    interleavedEntries = new string[entryLengths.Length];
                    //read # entries ints: string bytecounts
                    for(var i = 0; i < entryLengths.Length; i++) {
                        outputStream.Read(outputBuffer, 0, 4);
                        entryLengths[i] = BitConverter.ToInt32(outputBuffer, 0);
                    }
                    //read remaining: strings
                    using(var reader = new StreamReader(outputStream, System.Text.Encoding.Unicode)) {
                        for(var i = 0; i < entryLengths.Length; i++) {
                            char[] readerBuffer = new char[entryLengths[i]];
                            reader.Read(readerBuffer, 0, entryLengths[i]);
                            interleavedEntries[i] = new string(readerBuffer);
                        }
                    }
                }

                ConfigExchangeEntry[] retv = new ConfigExchangeEntry[interleavedEntries.Length / 4];
                var j = 0;
                for(var i = 0; i < interleavedEntries.Length; i += 4) {
                    retv[j++] = new ConfigExchangeEntry(interleavedEntries[i], interleavedEntries[i + 1], interleavedEntries[i + 2], interleavedEntries[i + 3]);
                }
                return retv;
            }
        }

        public override void SetupConfig() {
            base.SetupConfig();

            On.RoR2.Networking.NetworkManagerSystem.OnServerAddPlayerInternal += (orig, self, conn, pcid, extraMsg) => {
                orig(self, conn, pcid, extraMsg);

                if(!enableCheck || Util.ConnectionIsLocal(conn) || clients.Exists(x => x.connection == conn)) return;
                var pw = Guid.NewGuid().ToString("d");
                var cli = new ClientConfigSyncInfo {
                    connection = conn,
                    password = pw
                };

                clients.Add(cli);

                cli.AddExchangeAll();

                new MsgRequestNetConfigAck(cli).Send(conn);
            };
            RoR2.RoR2Application.onUpdate += UpdateConnections;

            /*On.RoR2.Run.EndStage += (orig, self) => {
                orig(self);
                AutoItemConfig.CleanupDirty(false);
            };*/

            LanguageAPI.Add("TILER2_KICKREASON_NCCRITMISMATCH", "TILER2 NetConfig: unable to resolve some config mismatches.\nSome settings must be synced, but cannot be changed while the game is running. Please check your console window for details.");
            LanguageAPI.Add("TILER2_KICKREASON_NCTIMEOUT", "TILER2 NetConfig: mismatch check timed out.\nThis may be caused by a mod version mismatch, a network outage, or an error while applying changes.\nIf seeking support for this issue, please make sure to have FULL CONSOLE LOGS from BOTH CLIENT AND SERVER ready to post.");
            LanguageAPI.Add("TILER2_KICKREASON_NCMISSINGENTRY", "TILER2 NetConfig: mismatch check found missing config entries.\nYou are likely using a different version of a mod than the server.");
            LanguageAPI.Add("TILER2_DISABLED_ARTIFACT", "This artifact is <color=#ff7777>force-disabled</color>; it will have no effect ingame.");

            NetworkingAPI.RegisterMessageType<MsgRequestNetConfigAck>();
            NetworkingAPI.RegisterMessageType<MsgRequestConfigSyncBegin>();
            NetworkingAPI.RegisterMessageType<MsgRequestConfigSyncContinue>();
            NetworkingAPI.RegisterMessageType<MsgReplyNetConfig>();
        }

        private static (List<AutoConfigBinding> results, string errorMsg) GetBindingFromPath(string path1, string path2, string path3) {
            var p1u = path1.ToUpper();
            var p2u = path2?.ToUpper();
            var p3u = path3?.ToUpper();

            List<AutoConfigBinding> matchesLevel1 = new List<AutoConfigBinding>(); //no enforced order, no enforced caps, partial matches
            List<AutoConfigBinding> matchesLevel2 = new List<AutoConfigBinding>(); //enforced order, no enforced caps, partial matches
            List<AutoConfigBinding> matchesLevel3 = new List<AutoConfigBinding>(); //enforced order, no enforced caps, full matches
            List<AutoConfigBinding> matchesLevel4 = new List<AutoConfigBinding>(); //enforced order, enforced caps, full matches

            AutoConfigBinding.instances.ForEach(x => {
                if(!x.allowConCmd) return;

                var name = x.configEntry.Definition.Key;
                var nameu = name.ToUpper();
                var cat = x.configEntry.Definition.Section;
                var catu = cat.ToUpper();
                var mod = x.modName;
                var modu = mod.ToUpper();

                if(path2 == null) {
                    //passed 1 part; could be mod, cat, or name
                    if(nameu.Contains(p1u)
                    || catu.Contains(p1u)
                    || modu.Contains(p1u)) {
                        matchesLevel1.Add(x);
                        matchesLevel2.Add(x);
                    } else return;
                    if(nameu == p1u)
                        matchesLevel3.Add(x);
                    else return;
                    if(name == path1)
                        matchesLevel4.Add(x);
                } else if(path3 == null) {
                    //passed 2 parts; could be mod/cat, mod/name, or cat/name
                    //enforced order only matches mod/cat or cat/name
                    var modMatch1u = modu.Contains(p1u);
                    var catMatch1u = catu.Contains(p1u);
                    var catMatch2u = catu.Contains(p2u);
                    var nameMatch2u = nameu.Contains(p2u);
                    if((modMatch1u && catMatch2u) || (catMatch1u && nameMatch2u) || (modMatch1u && nameMatch2u))
                        matchesLevel1.Add(x);
                    else return;

                    if(!(modMatch1u && nameMatch2u))
                        matchesLevel2.Add(x);
                    else return;

                    var modMatch1 = mod.Contains(path1);
                    var catMatch1 = cat.Contains(path1);
                    var catMatch2 = cat.Contains(path2);
                    var nameMatch2 = name.Contains(path2);

                    if((modMatch1 && catMatch2) || (catMatch1 && nameMatch2))
                        matchesLevel3.Add(x);
                    else return;

                    var modExact1 = mod == path1;
                    var catExact1 = cat == path1;
                    var catExact2 = cat == path2;
                    var nameExact2 = name == path2;

                    if((modExact1 && catExact2) || (catExact1 && nameExact2))
                        matchesLevel4.Add(x);
                } else {
                    //passed 3 parts; must be mod/cat/name
                    if(nameu.Contains(p3u)
                    && catu.Contains(p2u)
                    && modu.Contains(p1u)) {
                        matchesLevel1.Add(x);
                        matchesLevel2.Add(x);
                    } else return;
                    if(modu == p3u && catu == p2u && nameu == p1u)
                        matchesLevel3.Add(x);
                    else return;
                    if(mod == path3 && cat == path2 && name == path1)
                        matchesLevel4.Add(x);
                }
            });

            if(matchesLevel1.Count == 0) return (null, "no level 1 matches");
            else if(matchesLevel1.Count == 1) return (matchesLevel1, null);

            if(matchesLevel2.Count == 0) return (matchesLevel1, "multiple level 1 matches, no level 2 matches");
            else if(matchesLevel2.Count == 1) return (matchesLevel2, null);

            if(matchesLevel3.Count == 0) return (matchesLevel2, "multiple level 2 matches, no level 3 matches");
            else if(matchesLevel3.Count == 1) return (matchesLevel3, null);

            if(matchesLevel4.Count == 0) return (matchesLevel3, "multiple level 3 matches, no level 4 matches");
            else if(matchesLevel4.Count == 1) return (matchesLevel4, null);
            else {
                Debug.LogError($"TILER2 NetConfig: There are multiple config entries with the path \"{matchesLevel4[0].readablePath}\"; this should never happen! Please report this as a bug.");
                return (matchesLevel4, "multiple level 4 matches");
            }
        }

        private void UpdateConnections() {
            if(!NetworkServer.active) return;
            foreach(var cli in clients) {
                if((!cli.hasAcked && (Time.unscaledTime - cli.connectedAt) > CONN_CHECK_WAIT_TIME)
                    || (cli.currentExchange != null && (Time.unscaledTime - cli.currentExchange.Value.timestamp) > CONN_CHECK_WAIT_TIME)) {
                    if(timeoutKick) {
                        TILER2Plugin._logger.LogWarning($"Connection {cli.connection.connectionId} took too long to respond to config check request! Kick-on-timeout option is enabled; kicking client.");
                        //kicking during this loop could modify clients collection
                        _updateKickList.Add(cli);
                    } else
                        TILER2Plugin._logger.LogWarning($"Connection {cli.connection.connectionId} took too long to respond to config check request! Kick-on-timeout option is disabled.");
                }
            }

            foreach(var k in _updateKickList) {
                NetworkManagerSystem.singleton.ServerKickClient(k.connection, kickTimeout);
                clients.Remove(k);
            }
            _updateKickList.Clear();

            clients.RemoveAll(x => x.connection == null || !x.connection.isConnected);
        }

        internal static void ServerSyncAllToOne(NetworkConnection conn) {
            if(!NetworkServer.active) {
                TILER2Plugin._logger.LogError("NetConfig.ServerNCFGSyncAllToOne called on client");
                return;
            }
            var cli = clients.Find(x => x.connection == conn);
            if(cli == null) {
                return;
            }
            cli.AddExchangeAll();
        }

        internal static void ServerSyncOneToAll(AutoConfigBinding binding, object newValue) {
            if(!NetworkServer.active) {
                TILER2Plugin._logger.LogError("NetConfig.ServerNCFGSyncOneToAll called on client");
                return;
            }
            foreach(var cli in clients) {
                cli.AddExchangeOne(binding, newValue);
            }
        }

        private static void ClientCleanupNCFGSync() {
            cliSyncReceiveData.Clear();
            cliSyncReceiveBytes = 0;
            cliSyncReceiveBytesMax = 0;
        }

        private static void ClientFinalizeConfigSync() {
            if(!NetworkClient.active) {
                TILER2Plugin._logger.LogError("NetConfig.ClientFinalizeConfigSync called on server");
                return;
            }
            List<byte> payload = new List<byte>();
            for(var i = 0; i < cliSyncReceiveData.Count; i++) {
                if(!cliSyncReceiveData.ContainsKey(i)) {
                    TILER2Plugin._logger.LogError($"Gap in received MsgRequestConfigSyncContinue data at index {i} of {cliSyncReceiveData.Count}");
                    ClientCleanupNCFGSync();
                    return;
                }

                payload.AddRange(cliSyncReceiveData[i]);
            }

            if(payload.Count != cliSyncReceiveBytesMax) {
                TILER2Plugin._logger.LogError($"Mismatch in received MsgRequestConfigSyncContinue data vs payload size given by MsgRequestConfigSyncBegin");
                ClientCleanupNCFGSync();
                return;
            }

            var entries = new ConfigExchange(payload.ToArray()).Unpack();
            ClientCleanupNCFGSync();
            TILER2Plugin._logger.LogDebug($"NetConfig.ClientFinalizeConfigSync received payload of {entries.Length} entries");

            int matches = 0;
            bool foundCrit = false;
            bool foundWarn = false;
            foreach(var i in entries) {
                var res = ClientSyncConfigEntry(i.modName, i.configCategory, i.configName, i.serializedValue, true);
                if(res == ConfigSyncStatus.SyncPassWithChange) matches++;
                else if(res == ConfigSyncStatus.SyncWarn) foundWarn = true;
                else if(res == ConfigSyncStatus.SyncFail) foundCrit = true;
            }
            var result = ConfigSyncStatus.SyncPass;
            if(foundCrit) {
                Debug.LogError("TILER2 NetConfig: The above config entries marked with \"UNRESOLVABLE MISMATCH\" are different on the server, must be identical between server and client, and cannot be changed while the game is running. Close the game, change these entries to match the server's, then restart and rejoin the server.");
                result = ConfigSyncStatus.SyncFail;
            } else if(matches > 0) Chat.AddMessage($"Synced <color=#ffff00>{matches} setting changes</color> from the server temporarily. Check the console for details.");
            if(foundWarn)
                result = ConfigSyncStatus.SyncWarn;
            new MsgReplyNetConfig(cliNetId, cliPassword, result)
                .Send(NetworkDestination.Server);
        }

        private static ConfigSyncStatus ClientSyncConfigEntry(string modname, string category, string cfgname, string value, bool silent) {
            if(!NetworkClient.active) {
                TILER2Plugin._logger.LogError("NetConfig.ClientSyncConfigEntry called on server");
                return ConfigSyncStatus.Invalid;
            }
            var exactMatches = AutoConfigBinding.instances.FindAll(x => {
                return x.configEntry.Definition.Key == cfgname
                && x.configEntry.Definition.Section == category
                && x.modName == modname;
            });
            if(exactMatches.Count > 1) {
                var msg = $"TILER2 NetConfig: There are multiple config entries with the path \"{modname}/{category}/{cfgname}\"; this should never happen! Please report this as a bug.";
                Debug.LogError(msg);
                //important, make sure user knows
                Chat.AddMessage(msg);
                return ConfigSyncStatus.SyncWarn;
            } else if(exactMatches.Count == 0) {
                Debug.LogError($"TILER2 NetConfig: The server requested an update for a nonexistent config entry with the path \"{modname}/{category}/{cfgname}\". Make sure you're using the same mods AND mod versions as the server!");
                return ConfigSyncStatus.SyncWarn;
            }

            var newVal = TomlTypeConverter.ConvertToValue(value, exactMatches[0].propType);
            if(!exactMatches[0].cachedValue.Equals(newVal)) {
                if(exactMatches[0].netMismatchCritical) {
                    Debug.LogError($"TILER2 NetConfig: UNRESOLVABLE MISMATCH on \"{modname}/{category}/{cfgname}\"! Requested {newVal} vs current {exactMatches[0].cachedValue}");
                    return ConfigSyncStatus.SyncFail;
                }
                exactMatches[0].OverrideProperty(newVal, silent);
                return ConfigSyncStatus.SyncPassWithChange;
            }
            return ConfigSyncStatus.SyncPass;
        }

        #region Networking Interfaces
        private struct MsgRequestNetConfigAck : INetMessage {
            private string _password;
            private int _netId;

            public void Deserialize(NetworkReader reader) {
                _netId = reader.ReadInt32();
                _password = reader.ReadString();
            }

            public void Serialize(NetworkWriter writer) {
                writer.Write(_netId);
                writer.Write(_password);
            }

            public void OnReceived() {
                cliPassword = _password;
                cliNetId = _netId;
                new MsgReplyNetConfig(_netId, _password, ConfigSyncStatus.Connect).Send(NetworkDestination.Server);
            }

            public MsgRequestNetConfigAck(ClientConfigSyncInfo cli) {
                _netId = cli.connection.connectionId;
                _password = cli.password;
            }
        }

        private struct MsgReplyNetConfig : INetMessage {
            private string _password;
            private int _netId;
            private ConfigSyncStatus _status;

            public void Deserialize(NetworkReader reader) {
                _netId = reader.ReadInt32();
                _password = reader.ReadString();
                _status = (ConfigSyncStatus)reader.ReadByte();
            }

            public void Serialize(NetworkWriter writer) {
                writer.Write(_netId);
                writer.Write(_password);
                writer.Write((byte)_status);
            }

            public void OnReceived() {
                NetworkConnection conn = null;
                foreach(var checkConn in NetworkServer.connections) {
                    if(checkConn != null && checkConn.connectionId == _netId) {
                        conn = checkConn;
                        break;
                    }
                }
                if(conn == null) {
                    TILER2Plugin._logger.LogError($"NetConfig received reply from an invalid connectionId {_netId}! Reply has password \"{_password}\", type {_status}");
                    foreach(var cliF in clients) {
                        if(cliF.password == _password) {
                            TILER2Plugin._logger.LogError($"    Password matches user with connectionId {cliF.connection.connectionId}");
                        }
                    }
                    return;
                }
                var cli = clients.Find(x => x.connection == conn);
                if(cli == null) {
                    TILER2Plugin._logger.LogError($"NetConfig received reply from untracked connectionId {_netId}! Reply has password \"{_password}\", type {_status}");
                    return;
                }
                if(cli.password != _password) {
                    TILER2Plugin._logger.LogError($"NetConfig received reply from connectionId {_netId} with invalid password! Reply has password \"{_password}\", expected \"{cli.password}\"; type {_status}");
                    return;
                }
                TILER2Plugin._logger.LogDebug($"NetConfig reply OK! Reply has connectionId {_netId}, password \"{_password}\", type {_status}");

                if(_status == ConfigSyncStatus.Connect) {
                    cli.hasAcked = true;
                    cli.AdvanceExchangeQueue();
                } else if(_status == ConfigSyncStatus.BeginSync) {
                    cli.BeginExchange();
                } else if(_status == ConfigSyncStatus.SyncPass) {
                    TILER2Plugin._logger.LogDebug($"connectionId {_netId} passed config check");
                    cli.EndExchange();
                } else if(_status == ConfigSyncStatus.SyncWarn) {
                    if(NetConfig.instance.badVersionKick) {
                        TILER2Plugin._logger.LogWarning($"connectionId {_netId} failed config check (missing entries), kicking");
                        NetworkManagerSystem.singleton.ServerKickClient(conn, kickMissingEntry);
                    } else {
                        TILER2Plugin._logger.LogWarning($"connectionId {_netId} failed config check (missing entries)");
                        cli.EndExchange();
                    }
                } else if(_status == ConfigSyncStatus.SyncFail) {
                    if(NetConfig.instance.mismatchKick) {
                        TILER2Plugin._logger.LogWarning($"connectionId {_netId} failed config check (a config with DeferForever and PreventNetMismatch is mismatched), kicking");
                        NetworkManagerSystem.singleton.ServerKickClient(conn, kickCritMismatch);
                    } else {
                        TILER2Plugin._logger.LogWarning($"connectionId {_netId} failed config check (a config with DeferForever and PreventNetMismatch is mismatched)");
                        cli.EndExchange();
                    }
                } else {
                    TILER2Plugin._logger.LogError($"NetConfig received reply from connectionId {_netId} with invalid type! Reply has password \"{_password}\", type {_status}");
                }
            }

            public MsgReplyNetConfig(int netId, string password, ConfigSyncStatus status) {
                _netId = netId;
                _password = password;
                _status = status;
            }
        }

        private struct MsgRequestConfigSyncBegin : INetMessage {
            int _packageSizeBytes;

            public void Deserialize(NetworkReader reader) {
                _packageSizeBytes = reader.ReadInt32();
            }

            public void Serialize(NetworkWriter writer) {
                writer.Write(_packageSizeBytes);
            }

            public void OnReceived() {
                if(cliSyncReceiveData.Count() != 0) {
                    TILER2Plugin._logger.LogError("MsgRequestConfigSyncBegin received by client with sync already in progress");
                    return;
                }
                cliSyncReceiveBytesMax = _packageSizeBytes;
                new MsgReplyNetConfig(cliNetId, cliPassword, ConfigSyncStatus.BeginSync)
                    .Send(NetworkDestination.Server);
            }

            public MsgRequestConfigSyncBegin(int packageSizeBytes) {
                _packageSizeBytes = packageSizeBytes;
            }
        }

        private struct MsgRequestConfigSyncContinue : INetMessage {
            byte[] _chunk;
            int _index;

            public void Deserialize(NetworkReader reader) {
                _chunk = reader.ReadBytesAndSize();
                _index = reader.ReadInt32();
            }

            public void Serialize(NetworkWriter writer) {
                writer.WriteBytesAndSize(_chunk, _chunk.Length);
                writer.Write(_index);
            }

            public void OnReceived() {
                cliSyncReceiveData[_index] = _chunk;
                cliSyncReceiveBytes += _chunk.Length;
                if(cliSyncReceiveBytes == cliSyncReceiveBytesMax) {
                    ClientFinalizeConfigSync();
                } else if(cliSyncReceiveBytes > cliSyncReceiveBytesMax) {
                    TILER2Plugin._logger.LogError("Received more bytes via MsgRequestConfigSyncContinue than payload size given by MsgRequestConfigSyncBegin");
                }
            }

            public MsgRequestConfigSyncContinue(byte[] chunk, int index) {
                _chunk = chunk;
                _index = index;
            }
        }
        #endregion

        #region Console Commands
#if DEBUG
        [ConCommand(commandName = "NCFG_scramble")]
        public static void ConCmdNCFGScramble(ConCommandArgs args) {
            var rng = new Xoroshiro128Plus(0);
            AutoItemConfig.instances.ForEach(x => {
                /*if(x.configEntry.Description.AcceptableValues is AcceptableValueList) {

                } else if(x.configEntry.Description.AcceptableValues is AcceptableValueRange)*/
                var av = x.configEntry.Description.AcceptableValues;
                if(x.propType == typeof(bool))
                    x.UpdateProperty(rng.nextBool);
                else if(x.propType == typeof(int)) {
                    if(av != null && av is AcceptableValueRange<int>)
                        x.UpdateProperty((int)Mathf.Lerp(((AcceptableValueRange<int>)av).MinValue, ((AcceptableValueRange<int>)av).MaxValue, rng.nextNormalizedFloat));
                    else if(av != null && av is AcceptableValueList<int>)
                        x.UpdateProperty(rng.NextElementUniform(((AcceptableValueList<int>)av).AcceptableValues));
                    else
                        x.UpdateProperty(rng.nextInt);
                } else if(x.propType == typeof(float)) {
                    if(av != null && av is AcceptableValueRange<float>)
                        x.UpdateProperty(Mathf.Lerp(((AcceptableValueRange<float>)av).MinValue, ((AcceptableValueRange<float>)av).MaxValue, rng.nextNormalizedFloat));
                    else if(av != null && av is AcceptableValueList<float>)
                        x.UpdateProperty(rng.NextElementUniform(((AcceptableValueList<float>)av).AcceptableValues));
                    else
                        x.UpdateProperty((rng.nextNormalizedFloat - 0.5f) * float.MaxValue);
                }
            });
        }
#endif

        const string NCFG_GET_WRONG_ARGS_PRE = "ConCmd ncfg_get was used with bad arguments (";
        const string NCFG_GET_USAGE_POST = ").\nUsage: ncfg_get \"path1\" \"optional path2\" \"optional path3\". Path matches mod name, config category, and config name, in that order.";
        /// <summary>
        /// Registers as console command "ncfg_get". Prints an ingame value managed by TILER2.AutoItemConfig to console.
        /// </summary>
        /// <param name="args">Console command arguments. Matches 1 to 3 strings: mod name, config category, and config name, in that order or any subset thereof.</param>
        [ConCommand(commandName = "ncfg_get", helpText = "Prints an ingame value managed by TILER2.AutoItemConfig to console.")]
        public static void ConCmdNCFGGet(ConCommandArgs args) {
            if(args.Count < 1) {
                Debug.LogWarning($"{NCFG_GET_WRONG_ARGS_PRE}not enough arguments{NCFG_GET_USAGE_POST}");
                return;
            }
            if(args.Count > 3) {
                Debug.LogWarning($"{NCFG_GET_WRONG_ARGS_PRE}too many arguments{NCFG_GET_USAGE_POST}");
                return;
            }

            var (matches, errmsg) = GetBindingFromPath(args[0], (args.Count > 1) ? args[1] : null, (args.Count > 2) ? args[2] : null);

            if(errmsg != null) {
                if(matches != null)
                    Debug.Log($"The following config settings match that path:\n{String.Join(", ", matches.Select(x => "\"" + x.readablePath + "\""))}");
                else
                    Debug.Log("There are no config settings with complete nor partial matches for that path.");
                return;
            }

            var strs = new List<string> {
                $"\"{matches[0].readablePath}\" ({matches[0].propType.Name}): {(matches[0].configEntry.Description?.Description ?? "[no description]")}",
                $"Current value: {matches[0].cachedValue}"
            };
            if(AutoConfigBinding.stageDirtyInstances.ContainsKey(matches[0]))
                strs.Add($"Value next stage: {AutoConfigBinding.stageDirtyInstances[matches[0]].Item1}");
            if(AutoConfigBinding.runDirtyInstances.ContainsKey(matches[0])) {
                if(AutoConfigBinding.runDirtyInstances[matches[0]].Equals(matches[0].configEntry.BoxedValue))
                    strs.Add($"Temp. override; original value: {AutoConfigBinding.runDirtyInstances[matches[0]]}");
                else
                    strs.Add($"Value after game ends: {AutoConfigBinding.runDirtyInstances[matches[0]]}");
            }

            Debug.Log(String.Join("\n", strs));
        }

        private static void NCFGSet(ConCommandArgs args, bool isTemporary) {
            var targetCmd = isTemporary ? "ncfg_settemp" : "ncfg_set";

            var errorPre = $"ConCmd {targetCmd} failed (";
            var usagePost = $").\nUsage: {targetCmd} \"path1\" \"optional path2\" \"optional path3\" newValue. Path matches mod name, config category, and config name, in that order.";

            if(args.Count < 2) {
                NetUtil.SendConMsg(args.sender, $"{errorPre}not enough arguments{usagePost}", LogLevel.Warning);
                return;
            }
            if(args.Count > 4) {
                NetUtil.SendConMsg(args.sender, $"{errorPre}too many arguments{usagePost}", LogLevel.Warning);
                return;
            }

            var (matches, errmsg) = GetBindingFromPath(args[0], (args.Count > 2) ? args[1] : null, (args.Count > 3) ? args[2] : null);

            if(errmsg != null) {
                errmsg += ").";
                if(matches != null) {
                    errmsg += $"\nThe following config settings have a matching path: {String.Join(", ", matches.Select(x => "\"" + x.readablePath + "\""))}";
                }
                NetUtil.SendConMsg(args.sender, errorPre + errmsg, LogLevel.Warning);
                return;
            }

            var convStr = args[args.Count - 1];

            object convObj;
            try {
                convObj = TomlTypeConverter.ConvertToValue(convStr, matches[0].propType);
            } catch {
                NetUtil.SendConMsg(args.sender, $"{errorPre}can't convert argument 2 'newValue', \"{convStr}\", to the target config type, {matches[0].propType.Name}).", LogLevel.Warning);
                return;
            }

            if(!isTemporary) {
                matches[0].configEntry.BoxedValue = convObj;
                if(!matches[0].configEntry.ConfigFile.SaveOnConfigSet) matches[0].configEntry.ConfigFile.Save();
            } else matches[0].OverrideProperty(convObj);
            if(args.sender && !args.sender.hasAuthority) {
                Debug.Log($"TILER2 NetConfig: ConCmd {targetCmd} from client {args.sender.userName} passed. Changed config setting {matches[0].readablePath} to {convObj}.");
                NetUtil.SendConMsg(args.sender, $"ConCmd {targetCmd} successfully updated config entry! Changed config setting {matches[0].readablePath} to {convObj}.");
            } else {
                Debug.Log($"TILER2 NetConfig: {targetCmd} successful. Changed config setting {matches[0].readablePath} to {convObj}.");
            }
        }

        /// <summary>
        /// Registers as console command "ncfg_set". While on the main menu, in singleplayer, or hosting a server: permanently override an ingame value managed by TILER2.AutoItemConfig. While non-host: attempts to call ncfg_settemp on the server instead.
        /// </summary>
        /// <param name="args">Console command arguments. Matches 1-3 strings (same as ncfg_get), then any serialized object which is valid for the target config entry.</param>
        [ConCommand(commandName = "ncfg_set", helpText = "While on the main menu, in singleplayer, or hosting a server: permanently override an ingame value managed by TILER2.AutoItemConfig. While non-host: attempts to call ncfg_settemp on the server instead.")]
        public static void ConCmdNCFGSet(ConCommandArgs args) {
            //todo: don't reroute AllowNetMismatch, add a SetTempLocal cmd?
            if(args.sender && !args.sender.isServer) {
                RoR2.Console.instance.RunClientCmd(args.sender, "ncfg_settemp", args.userArgs.ToArray());
                return;
            } else NCFGSet(args, false);
        }

        /// <summary>
        /// Registers as console command "ncfg_settemp". While a run is ongoing: temporarily override an ingame value managed by TILER2.AutoItemConfig. This will last until the end of the run. Use by non-host players can be blocked by the host using the ConVar ncfg_allowclientset.
        /// </summary>
        /// <param name="args">Console command arguments. Matches 1-3 strings (same as NCFG_get), then any serialized object which is valid for the target config entry.</param>
        [ConCommand(commandName = "ncfg_settemp", flags = ConVarFlags.ExecuteOnServer, helpText = "While a run is ongoing: temporarily override an ingame value managed by TILER2.AutoItemConfig. This will last until the end of the run. Use by non-host players can be blocked by the host using the ConVar ncfg_allowclientset.")]
        public static void ConCmdNCFGSetTemp(ConCommandArgs args) {
#if !DEBUG
            if((!args.sender.hasAuthority) && !allowClientNCFGSet.value) {
                Debug.LogWarning($"TILER2 NetConfig: Client {args.sender.userName} tried to use ConCmd ncfg_settemp, but ConVar ncfg_allowclientset is set to false. DO NOT change this convar to true, unless you trust everyone who is in or may later join the server; doing so will allow them to temporarily change some config settings.");
                NetUtil.SendConMsg(args.sender, "ConCmd ncfg_settemp cannot be used on this server by anyone other than the host.", LogLevel.Warning);
                return;
            }
#endif

            NCFGSet(args, true);
        }

        /// <summary>
        /// Registers as console command "ncfg". Routes to other AutoItemConfig commands (ncfg_get, ncfg_set, ncfg_settemp).
        /// </summary>
        /// <param name="args">Console command arguments. Matches 1 string, then the console command arguments of the target ncfg_ command.</param>
        [ConCommand(commandName = "ncfg", helpText = "Routes to other AutoItemConfig commands (ncfg_get, ncfg_set, ncfg_settemp). For when you forget the underscore.")]
        public static void ConCmdNCFG(ConCommandArgs args) {
            if(args.Count == 0) {
                Debug.LogWarning("ConCmd ncfg was not passed enough arguments (needs at least 1 to determine which command to route to).");
                return;
            }
            string cmdToCall;
            if(args[0].ToUpper() == "GET") cmdToCall = "ncfg_get";
            else if(args[0].ToUpper() == "SET") cmdToCall = "ncfg_set";
            else if(args[0].ToUpper() == "SETTEMP") cmdToCall = "ncfg_settemp";
            else {
                Debug.LogWarning($"ConCmd ncfg_{args[0]} does not exist. Valid commands include: ncfg_get, ncfg_set, ncfg_settemp.");
                return;
            }
            RoR2.Console.instance.RunClientCmd(args.sender, cmdToCall, args.userArgs.Skip(1).ToArray());
        }

        /* just in case manual checking ever becomes necessary. TODO: rate limiting?
        [ConCommand(commandName = "ncfg_check", flags = ConVarFlags.ExecuteOnServer, helpText = "Sends a request to the server for a check for config mismatches. Performed automatically during connection.")]
        public static void ConCmdNCFGCheck(ConCommandArgs args) {
            EnsureOrchestrator();

            if(args.sender.hasAuthority) {
                TILER2Plugin._logger.LogWarning("ConCmd ncfg_check was used directly by server (no-op).");
                return;
            }

            NetOrchestrator.NCFGSyncAllToOne(args.sender.connectionToClient);
        }*/
        #endregion
    }
}
