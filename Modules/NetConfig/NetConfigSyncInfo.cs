using BepInEx.Configuration;
using R2API.Networking.Interfaces;
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine.Networking;
using static TILER2.NetConfigModule;

namespace TILER2 {
    internal class NetConfigClientInfo {
        public NetworkConnection connection;
        public string password; //netids are sequential; password detrivializes being able to kick other clients by spamming fail replies
        public bool hasAcked = false;
        public ConfigExchange? currentExchange;
        public readonly List<ConfigExchange> pendingExchanges = new();
        public readonly float connectedAt = UnityEngine.Time.unscaledTime;

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
                new NetConfigLocalClient.MsgRequestConfigSyncBegin(currentExchange.Value.content.Length).Send(connection);
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
            var step = MAX_MESSAGE_SIZE_BYTES - 4;
            var index = 0;
            for(var i = 0; i < payload.Length; i += step) {
                var next = payload.Skip(i);
                new NetConfigLocalClient.MsgRequestConfigSyncContinue(
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
}
