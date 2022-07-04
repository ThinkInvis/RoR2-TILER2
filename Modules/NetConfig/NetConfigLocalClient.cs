using R2API.Networking;
using R2API.Networking.Interfaces;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Networking;
using static TILER2.NetConfigModule;

namespace TILER2 {
    //clientside storage for info received from server
    internal static class NetConfigLocalClient {
        private static string password;
        private static int netId;
        private static int syncReceiveBytesMax = 0;
        private static int syncReceiveBytes = 0;
        private static readonly Dictionary<int, byte[]> syncReceiveData = new();

        private static void ClientCleanupConfigSync() {
            syncReceiveData.Clear();
            syncReceiveBytes = 0;
            syncReceiveBytesMax = 0;
        }

        private static void ClientFinalizeConfigSync() {
            if(!NetworkClient.active) {
                TILER2Plugin._logger.LogError("NetConfigLocalClient.ClientFinalizeConfigSync called on server");
                return;
            }
            List<byte> payload = new();
            for(var i = 0; i < syncReceiveData.Count; i++) {
                if(!syncReceiveData.ContainsKey(i)) {
                    TILER2Plugin._logger.LogError($"Gap in received MsgRequestConfigSyncContinue data at index {i} of {syncReceiveData.Count}");
                    ClientCleanupConfigSync();
                    return;
                }

                payload.AddRange(syncReceiveData[i]);
            }

            if(payload.Count != syncReceiveBytesMax) {
                TILER2Plugin._logger.LogError($"Mismatch in received MsgRequestConfigSyncContinue data vs payload size given by MsgRequestConfigSyncBegin");
                ClientCleanupConfigSync();
                return;
            }

            var entries = new ConfigExchange(payload.ToArray()).Unpack();
            ClientCleanupConfigSync();
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
            } else if(matches > 0) RoR2.Chat.AddMessage($"Synced <color=#ffff00>{matches} setting change{(matches > 1 ? "s" : "")}</color> from the server temporarily. Check the console for details.");
            if(foundWarn)
                result = ConfigSyncStatus.SyncWarn;
            new MsgReplyNetConfig(netId, password, result)
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
                RoR2.Chat.AddMessage(msg);
                return ConfigSyncStatus.SyncWarn;
            } else if(exactMatches.Count == 0) {
                Debug.LogError($"TILER2 NetConfig: The server requested an update for a nonexistent config entry with the path \"{modname}/{category}/{cfgname}\". Make sure you're using the same mods AND mod versions as the server!");
                return ConfigSyncStatus.SyncWarn;
            }

            var newVal = BepInEx.Configuration.TomlTypeConverter.ConvertToValue(value, exactMatches[0].propType);
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

        internal struct MsgRequestConfigSyncBegin : INetMessage {
            int _packageSizeBytes;

            public void Deserialize(NetworkReader reader) {
                _packageSizeBytes = reader.ReadInt32();
            }

            public void Serialize(NetworkWriter writer) {
                writer.Write(_packageSizeBytes);
            }

            public void OnReceived() {
                if(syncReceiveData.Count() != 0) {
                    TILER2Plugin._logger.LogError("MsgRequestConfigSyncBegin received by client with sync already in progress");
                    return;
                }
                syncReceiveBytesMax = _packageSizeBytes;
                new MsgReplyNetConfig(netId, password, ConfigSyncStatus.BeginSync)
                    .Send(NetworkDestination.Server);
            }

            public MsgRequestConfigSyncBegin(int packageSizeBytes) {
                _packageSizeBytes = packageSizeBytes;
            }
        }

        internal struct MsgRequestConfigSyncContinue : INetMessage {
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
                syncReceiveData[_index] = _chunk;
                syncReceiveBytes += _chunk.Length;
                if(syncReceiveBytes == syncReceiveBytesMax) {
                    ClientFinalizeConfigSync();
                } else if(syncReceiveBytes > syncReceiveBytesMax) {
                    TILER2Plugin._logger.LogError("Received more bytes via MsgRequestConfigSyncContinue than payload size given by MsgRequestConfigSyncBegin");
                }
            }

            public MsgRequestConfigSyncContinue(byte[] chunk, int index) {
                _chunk = chunk;
                _index = index;
            }
        }

        internal struct MsgRequestNetConfigAck : INetMessage {
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
                password = _password;
                netId = _netId;
                new MsgReplyNetConfig(_netId, _password, ConfigSyncStatus.Connect).Send(NetworkDestination.Server);
            }

            public MsgRequestNetConfigAck(NetConfigClientInfo cli) {
                _netId = cli.connection.connectionId;
                _password = cli.password;
            }
        }
    }
}
