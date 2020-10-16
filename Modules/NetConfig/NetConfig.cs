using UnityEngine;
using RoR2;
using System;
using System.Reflection;
using System.Linq;
using R2API;
using RoR2.Networking;
using UnityEngine.Networking;
using System.Collections.Generic;
using BepInEx.Configuration;
using static RoR2.Networking.GameNetworkManager;
using BepInEx.Logging;

namespace TILER2 {
    /// <summary>
    /// Provides automatic network syncing and mismatch kicking for the AutoItemConfig module.
    /// </summary>
    public class NetConfig : T2Module<NetConfig> {
        public static readonly SimpleLocalizedKickReason kickCritMismatch = new SimpleLocalizedKickReason("TILER2_KICKREASON_NCCRITMISMATCH");
        public static readonly SimpleLocalizedKickReason kickTimeout = new SimpleLocalizedKickReason("TILER2_KICKREASON_NCTIMEOUT");
        public static readonly SimpleLocalizedKickReason kickMissingEntry = new SimpleLocalizedKickReason("TILER2_KICKREASON_NCMISSINGENTRY");

        [AutoConfig("If true, NetConfig will use the server to check for config mismatches.")]
        public bool enableCheck {get; private set;} = true;
        [AutoConfig("If true, NetConfig will kick clients that fail config checks (caused by config entries internally marked as both DeferForever and DisallowNetMismatch).")]
        public bool mismatchKick {get; private set;} = true;
        [AutoConfig("If true, NetConfig will kick clients that are missing config entries (may be caused by different mod versions on client).")]
        public bool badVersionKick {get; private set;} = true;
        [AutoConfig("If true, NetConfig will kick clients that take too long to respond to config checks (may be caused by missing mods on client, or by major network issues).")]
        public bool timeoutKick {get; private set;} = true;

        public override void SetupConfig() {
            var netOrchPrefabPrefab = new GameObject("TILER2NetConfigOrchestratorPrefabPrefab");
            netOrchPrefabPrefab.AddComponent<NetworkIdentity>();
            NetConfig.netOrchPrefab = netOrchPrefabPrefab.InstantiateClone("TILER2NetConfigOrchestratorPrefab");
            NetConfig.netOrchPrefab.AddComponent<NetConfigOrchestrator>();
            
            On.RoR2.Networking.GameNetworkManager.OnServerAddPlayerInternal += (orig, self, conn, pcid, extraMsg) => {
                orig(self, conn, pcid, extraMsg);
                if(!enableCheck || Util.ConnectionIsLocal(conn) || NetConfigOrchestrator.checkedConnections.Contains(conn)) return;
                NetConfigOrchestrator.checkedConnections.Add(conn);
                NetConfig.EnsureOrchestrator();
                NetConfigOrchestrator.AICSyncAllToOne(conn);
            };
            
            /*On.RoR2.Run.EndStage += (orig, self) => {
                orig(self);
                AutoItemConfig.CleanupDirty(false);
            };*/

            LanguageAPI.Add("TILER2_KICKREASON_NCCRITMISMATCH", "TILER2 NetConfig: unable to resolve some config mismatches.\nSome settings must be synced, but cannot be changed while the game is running. Please check your console window for details.");
            LanguageAPI.Add("TILER2_KICKREASON_NCTIMEOUT", "TILER2 NetConfig: mismatch check timed out.\nThis may be caused by a mod version mismatch, a network outage, or an error while applying changes.\nIf seeking support for this issue, please make sure to have FULL CONSOLE LOGS from BOTH CLIENT AND SERVER ready to post.");
            LanguageAPI.Add("TILER2_KICKREASON_NCMISSINGENTRY", "TILER2 NetConfig: mismatch check found missing config entries.\nYou are likely using a different version of a mod than the server.");
            LanguageAPI.Add("TILER2_DISABLED_ARTIFACT", "This artifact is <color=#ff7777>force-disabled</color>; it will have no effect ingame.");
        }

        internal static GameObject netOrchPrefab;
        internal static GameObject netOrchestrator;

        private static readonly RoR2.ConVar.BoolConVar allowClientAICSet = new RoR2.ConVar.BoolConVar("aic_allowclientset", ConVarFlags.None, "false", "If true, clients may use the ConCmds aic_set or aic_settemp to temporarily set config values on the server. If false, aic_set and aic_settemp will not work for clients.");

        internal static void EnsureOrchestrator() {
            if(!NetworkServer.active) {
                TILER2Plugin._logger.LogError("EnsureOrchestrator called on client");
            }
            if(!netOrchestrator) {
                netOrchestrator = UnityEngine.Object.Instantiate(netOrchPrefab);
                NetworkServer.Spawn(netOrchestrator);
            }
        }

        private static (List<AutoConfigBinding> results, string errorMsg) GetAICFromPath(string path1, string path2, string path3) {
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
                TILER2Plugin._logger.LogError("There are multiple config entries with the path \"" + matchesLevel4[0].readablePath + "\"; this should never happen! Please report this as a bug.");
                return (matchesLevel4, "multiple level 4 matches");
            }
        }

        #if DEBUG
        [ConCommand(commandName = "aic_scramble")]
        public static void ConCmdAICScramble(ConCommandArgs args) {
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

        [ConCommand(commandName = "aic_checkrespond", flags = ConVarFlags.ExecuteOnServer)]
        internal static void ConCmdAICCheckRespond(ConCommandArgs args) {
            EnsureOrchestrator();
            NetConfigOrchestrator.instance.CheckRespond(args.sender.connectionToClient, args[0], args[1]);
        }

        /// <summary>
        /// Registers as console command "aic_get". Prints an ingame value managed by TILER2.AutoItemConfig to console.
        /// </summary>
        /// <param name="args">Console command arguments. Matches 1 to 3 strings: mod name, config category, and config name, in that order or any subset thereof.</param>
        [ConCommand(commandName = "aic_get", helpText = "Prints an ingame value managed by TILER2.AutoItemConfig to console.")]
        public static void ConCmdAICGet(ConCommandArgs args) {
            var wrongArgsPre = "ConCmd aic_get was used with bad arguments (";
            var usagePost = ").\nUsage: aic_get \"path1\" \"optional path2\" \"optional path3\". Path matches mod name, config category, and config name, in that order.";

            if(args.Count < 1) {
                TILER2Plugin._logger.LogWarning(wrongArgsPre + "not enough arguments" + usagePost);
                return;
            }
            if(args.Count > 3) {
                TILER2Plugin._logger.LogWarning(wrongArgsPre + "too many arguments" + usagePost);
                return;
            }

            var (matches, errmsg) = GetAICFromPath(args[0], (args.Count > 1) ? args[1] : null, (args.Count > 2) ? args[2] : null);

            if(errmsg != null) {
                if(matches != null)
                    TILER2Plugin._logger.LogMessage("The following config settings match that path:\n" + String.Join(", ", matches.Select(x => "\"" + x.readablePath + "\"")));
                else
                    TILER2Plugin._logger.LogMessage("There are no config settings with complete nor partial matches for that path.");
                return;
            }

            var strs = new List<string> {
                "\"" + matches[0].readablePath + "\" (" + matches[0].propType.Name + "): " + (matches[0].configEntry.Description?.Description ?? "[no description]"),
                "Current value: " + matches[0].cachedValue.ToString()
            };
            if (AutoConfigBinding.stageDirtyInstances.ContainsKey(matches[0]))
                strs.Add("Value next stage: " + AutoConfigBinding.stageDirtyInstances[matches[0]].Item1.ToString());
            if(AutoConfigBinding.runDirtyInstances.ContainsKey(matches[0])) {
                if(AutoConfigBinding.runDirtyInstances[matches[0]].Equals(matches[0].configEntry.BoxedValue))
                    strs.Add("Temp. override; original value: " + AutoConfigBinding.runDirtyInstances[matches[0]].ToString());
                else
                    strs.Add("Value after game ends: " + AutoConfigBinding.runDirtyInstances[matches[0]].ToString());
            }

            TILER2Plugin._logger.LogMessage(String.Join("\n", strs));
        }

        private static void AICSet(ConCommandArgs args, bool isTemporary) {
            var targetCmd = isTemporary ? "aic_settemp" : "aic_set";

            var errorPre = "ConCmd " + targetCmd + " failed (";
            var usagePost = ").\nUsage: " + targetCmd + " \"path1\" \"optional path2\" \"optional path3\" newValue. Path matches mod name, config category, and config name, in that order.";

            if(args.Count < 2) {
                NetConfigOrchestrator.SendConMsg(args.sender, errorPre + "not enough arguments" + usagePost, LogLevel.Warning);
                return;
            }
            if(args.Count > 4) {
                NetConfigOrchestrator.SendConMsg(args.sender, errorPre + "too many arguments" + usagePost, LogLevel.Warning);
                return;
            }
            
            var (matches, errmsg) = GetAICFromPath(args[0], (args.Count > 2) ? args[1] : null, (args.Count > 3) ? args[2] : null);

            if(errmsg != null) {
                errmsg += ").";
                if(matches != null) {
                    errmsg += "\nThe following config settings have a matching path: " + String.Join(", ", matches.Select(x => "\"" + x.readablePath + "\""));
                }
                NetConfigOrchestrator.SendConMsg(args.sender, errorPre + errmsg, LogLevel.Warning);
                return;
            }

            var convStr = args[args.Count - 1];
            
            object convObj;
            try {
                convObj = TomlTypeConverter.ConvertToValue(convStr, matches[0].propType);
            } catch {
                NetConfigOrchestrator.SendConMsg(args.sender, errorPre + "(can't convert argument 2 'newValue' to the target config type, " + matches[0].propType.Name + ").", LogLevel.Warning);
                return;
            }

            if(!isTemporary) {
                matches[0].configEntry.BoxedValue = convObj;
                if(!matches[0].configEntry.ConfigFile.SaveOnConfigSet) matches[0].configEntry.ConfigFile.Save();
            } else matches[0].OverrideProperty(convObj);
            if(args.sender && !args.sender.hasAuthority) TILER2Plugin._logger.LogMessage("ConCmd " + targetCmd + " from client " + args.sender.userName + " passed. Changed config setting " + matches[0].readablePath + " to " + convObj.ToString());
            NetConfigOrchestrator.SendConMsg(args.sender, "ConCmd " + targetCmd + " successfully updated config entry!");
        }

        /// <summary>
        /// Registers as console command "aic_set". While on the main menu, in singleplayer, or hosting a server: permanently override an ingame value managed by TILER2.AutoItemConfig. While non-host: attempts to call aic_settemp on the server instead.
        /// </summary>
        /// <param name="args">Console command arguments. Matches 1-3 strings (same as aic_get), then any serialized object which is valid for the target config entry.</param>
        [ConCommand(commandName = "aic_set", helpText = "While on the main menu, in singleplayer, or hosting a server: permanently override an ingame value managed by TILER2.AutoItemConfig. While non-host: attempts to call aic_settemp on the server instead.")]
        public static void ConCmdAICSet(ConCommandArgs args) {
            //todo: don't reroute AllowNetMismatch, add a SetTempLocal cmd?
            if((args.sender && !args.sender.isServer)) {
                RoR2.Console.instance.RunClientCmd(args.sender, "aic_settemp", args.userArgs.ToArray());
                return;
            } else AICSet(args, false);
        }

        /// <summary>
        /// Registers as console command "aic_settemp". While a run is ongoing: temporarily override an ingame value managed by TILER2.AutoItemConfig. This will last until the end of the run. Use by non-host players can be blocked by the host using the ConVar aic_allowclientset.
        /// </summary>
        /// <param name="args">Console command arguments. Matches 1-3 strings (same as aic_get), then any serialized object which is valid for the target config entry.</param>
        [ConCommand(commandName = "aic_settemp", flags = ConVarFlags.ExecuteOnServer, helpText = "While a run is ongoing: temporarily override an ingame value managed by TILER2.AutoItemConfig. This will last until the end of the run. Use by non-host players can be blocked by the host using the ConVar aic_allowclientset.")]
        public static void ConCmdAICSetTemp(ConCommandArgs args) {
            EnsureOrchestrator();

#if !DEBUG
            if((!args.sender.hasAuthority) && !allowClientAICSet.value) {
                TILER2Plugin._logger.LogWarning("Client " + args.sender.userName + " tried to use ConCmd aic_settemp, but ConVar aic_allowclientset is set to false. DO NOT change this convar to true, unless you trust everyone who is in or may later join the server; doing so will allow them to temporarily change some config settings.");
                NetConfigOrchestrator.SendConMsg(args.sender, "ConCmd aic_settemp cannot be used on this server by anyone other than the host.", LogLevel.Warning);
                return;
            }
#endif

            AICSet(args, true);
        }

        /// <summary>
        /// Registers as console command "aic". Routes to other AutoItemConfig commands (aic_get, aic_set, aic_settemp).
        /// </summary>
        /// <param name="args">Console command arguments. Matches 1 string, then the console command arguments of the target aic_ command.</param>
        [ConCommand(commandName = "aic", helpText = "Routes to other AutoItemConfig commands (aic_get, aic_set, aic_settemp). For when you forget the underscore.")]
        public static void ConCmdAIC(ConCommandArgs args) {
            if(args.Count == 0) {
                TILER2Plugin._logger.LogWarning("ConCmd aic was not passed enough arguments (needs at least 1 to determine which command to route to).");
                return;
            }
            string cmdToCall;
            if(args[0].ToUpper() == "GET") cmdToCall = "aic_get";
            else if(args[0].ToUpper() == "SET") cmdToCall = "aic_set";
            else if(args[0].ToUpper() == "SETTEMP") cmdToCall = "aic_settemp";
            else {
                TILER2Plugin._logger.LogWarning("ConCmd aic_" + args[0] + " does not exist. Valid commands include: aic_get, aic_set, aic_settemp.");
                return;
            }
            RoR2.Console.instance.RunClientCmd(args.sender, cmdToCall, args.userArgs.Skip(1).ToArray());
        }

        /* just in case manual checking ever becomes necessary. TODO: rate limiting?
        [ConCommand(commandName = "AIC_Check", flags = ConVarFlags.ExecuteOnServer, helpText = "Sends a request to the server for a check for config mismatches. Performed automatically during connection.")]
        public static void ConCmdAICCheck(ConCommandArgs args) {
            EnsureOrchestrator();

            if(args.sender.hasAuthority) {
                TILER2Plugin._logger.LogWarning("ConCmd AIC_Check was used directly by server (no-op).");
                return;
            }

            NetOrchestrator.AICSyncAllToOne(args.sender.connectionToClient);
        }*/
    }
    
    /// <summary>
    /// Orchestrator GameObject which handles all network messages for NetConfig.
    /// </summary>
    public class NetConfigOrchestrator : NetworkBehaviour {
        internal static NetConfigOrchestrator instance;

        [System.Diagnostics.CodeAnalysis.SuppressMessage("Code Quality", "IDE0051:Remove unused private members", Justification = "Used by UnityEngine")]
        private void Awake() {
            instance = this;
        }

        /// <summary>
        /// Send a console log message to the target NetworkUser.
        /// </summary>
        /// <param name="user">The NetworkUser to send a console message to.</param>
        /// <param name="msg">The content of the sent message.</param>
        /// <param name="severity">The severity of the sent message.</param>
        public static void SendConMsg(NetworkUser user, string msg, LogLevel severity = LogLevel.Message) {
            if(user == null || user.hasAuthority)
                ConMsg(msg, severity);
            else {
                NetConfig.EnsureOrchestrator();
                instance.TargetConMsg(user.connectionToClient, msg, (int)severity);
            }
        }

        [TargetRpc]
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Style", "IDE0060:Remove unused parameter", Justification = "Target param is required by UNetWeaver")]
        private void TargetConMsg(NetworkConnection target, string msg, int severity) {
            ConMsg(msg, (LogLevel)severity);
        }

        private static void ConMsg(string msg, LogLevel severity) {
            TILER2Plugin._logger.Log(severity, msg);
        }

        /// <summary>
        /// Send a chat message to all players in a server.
        /// </summary>
        /// <param name="msg">The content of the sent message.</param>
        [Server]
        public static void ServerSendGlobalChatMsg(string msg) {
            NetConfig.EnsureOrchestrator();
            instance.RpcGlobalChatMsg(msg);
        }

        [ClientRpc]
        internal void RpcGlobalChatMsg(string msg) {
            Chat.AddMessage(msg);
        }
        
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Code Quality", "IDE0051:Remove unused private members", Justification = "Used by UnityEngine")]
        private void Update() {
            if(!NetworkServer.active) return;
            connectionsToCheck.ForEach(x => {
                x.timeRemaining -= Time.unscaledDeltaTime;
                if(x.timeRemaining <= 0f) {
                    if(NetConfig.instance.timeoutKick) {
                        TILER2Plugin._logger.LogWarning("Connection " + x.connection.connectionId + " took too long to respond to config check request! Kick-on-timeout option is enabled; kicking client.");
                        GameNetworkManager.singleton.ServerKickClient(x.connection, NetConfig.kickTimeout);
                    } else
                        TILER2Plugin._logger.LogWarning("Connection " + x.connection.connectionId + " took too long to respond to config check request! Kick-on-timeout option is disabled.");
                }
            });

            connectionsToCheck.RemoveAll(x => x.timeRemaining <= 0f);
        }

        private class WaitingConnCheck {
            public NetworkConnection connection;
            public string password;
            public float timeRemaining;
        }
        private const float connCheckWaitTime = 15f;
        private static readonly List<WaitingConnCheck> connectionsToCheck = new List<WaitingConnCheck>();
        internal static readonly List<NetworkConnection> checkedConnections = new List<NetworkConnection>();

        internal void CheckRespond(NetworkConnection conn, string password, string result) {
            var match = connectionsToCheck.Find(x => x.connection == conn && x.password == password);
            if(match == null) {
                TILER2Plugin._logger.LogError("Received config check response from unregistered connection!");
                return;
            }
            if(result == "PASS") {
                TILER2Plugin._logger.LogDebug("Connection " + match.connection.connectionId + " passed config check");
                connectionsToCheck.Remove(match);
            } else if(result == "FAILMM"){
                if(NetConfig.instance.mismatchKick) {
                    TILER2Plugin._logger.LogWarning("Connection " + match.connection.connectionId + " failed config check (crit mismatch), kicking");
                    GameNetworkManager.singleton.ServerKickClient(match.connection, NetConfig.kickCritMismatch);
                } else {
                    TILER2Plugin._logger.LogWarning("Connection " + match.connection.connectionId + " failed config check (crit mismatch)");
                }
                connectionsToCheck.Remove(match);
            } else if(result == "FAILBV"
                || result == "FAIL") { //from old mod version
                var msg = (result == "FAIL") ? "using old TILER2 version" : "missing entries";
                if(NetConfig.instance.badVersionKick) {
                    TILER2Plugin._logger.LogWarning("Connection " + match.connection.connectionId + " failed config check (" + msg + "), kicking");
                    GameNetworkManager.singleton.ServerKickClient(match.connection, NetConfig.kickMissingEntry);
                } else {
                    TILER2Plugin._logger.LogWarning("Connection " + match.connection.connectionId + " failed config check (" + msg + ")");
                }
                connectionsToCheck.Remove(match);
            } else {
                TILER2Plugin._logger.LogError("POSSIBLE SECURITY ISSUE: Received registered connection and correct password in CheckRespond, but result is not valid");
            }
        }

        internal static void AICSyncAllToOne(NetworkConnection conn) {
            var validInsts = AutoConfigBinding.instances.Where(x => !x.allowNetMismatch);
            var serValues = new List<string>();
            var modnames = new List<string>();
            var categories = new List<string>();
            var cfgnames = new List<string>();
            foreach(var i in validInsts) {
                serValues.Add(TomlTypeConverter.ConvertToString(i.cachedValue, i.propType));
                modnames.Add(i.modName);
                categories.Add(i.configEntry.Definition.Section);
                cfgnames.Add(i.configEntry.Definition.Key);
            }

            string password = Guid.NewGuid().ToString("d");

            connectionsToCheck.Add(new WaitingConnCheck{
                connection = conn,
                password = password,
                timeRemaining = connCheckWaitTime
            });

            instance.TargetAICSyncAllToOne(conn, password, modnames.ToArray(), categories.ToArray(), cfgnames.ToArray(), serValues.ToArray());
        }

        [TargetRpc]
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Style", "IDE0060:Remove unused parameter", Justification = "Target param is required by UNetWeaver")]
        private void TargetAICSyncAllToOne(NetworkConnection target, string password, string[] modnames, string[] categories, string[] cfgnames, string[] values) {
            int matches = 0;
            bool foundCrit = false;
            bool foundWarn = false;
            for(var i = 0; i < modnames.Length; i++) {
                var res = CliAICSync(modnames[i], categories[i], cfgnames[i], values[i], true);
                if(res == 1) matches++;
                else if(res == -1) foundWarn = true;
                else if(res == -2) foundCrit = true;
            }
            if(NetworkUser.readOnlyLocalPlayersList.Count == 0) TILER2Plugin._logger.LogError("Received TargetAICSyncAllToOne, but readOnlyLocalPlayersList is empty; can't send response");
            if(foundCrit == true) {
                TILER2Plugin._logger.LogError("The above config entries marked with \"CRITICAL MISMATCH\" are different on the server, and they cannot be changed while the game is running. Close the game, change these entries to match the server's, then restart and rejoin the server.");
                RoR2.Console.instance.SubmitCmd(NetworkUser.readOnlyLocalPlayersList[0], "AIC_CheckRespond " + password + " FAILMM");
                return;
            }
            else if(matches > 0) Chat.AddMessage("Synced <color=#ffff00>"+ matches +" setting changes</color> from the server temporarily. Check the console for details.");
            RoR2.Console.instance.SubmitCmd(NetworkUser.readOnlyLocalPlayersList[0], "AIC_CheckRespond " + password + (foundWarn ? " FAILBV" : " PASS"));
        }

        [Server]
        internal void ServerAICSyncOneToAll(AutoConfigBinding targetConfig, object newValue) {
            foreach(var user in NetworkUser.readOnlyInstancesList) {
                if(user.hasAuthority || (user.connectionToClient != null && Util.ConnectionIsLocal(user.connectionToClient))) continue;
                TargetAICSyncOneToAll(user.connectionToClient, targetConfig.modName, targetConfig.configEntry.Definition.Section, targetConfig.configEntry.Definition.Key,
                    TomlTypeConverter.ConvertToString(newValue, targetConfig.propType));
            }
        }

        [TargetRpc]
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Style", "IDE0060:Remove unused parameter", Justification = "Target param is required by UNetWeaver")]
        private void TargetAICSyncOneToAll(NetworkConnection target, string modname, string category, string cfgname, string value) {
            CliAICSync(modname, category, cfgname, value, false);
        }

        [Client]
        private int CliAICSync(string modname, string category, string cfgname, string value, bool silent) {
            var exactMatches = AutoConfigBinding.instances.FindAll(x => {
                return x.configEntry.Definition.Key == cfgname
                && x.configEntry.Definition.Section == category
                && x.modName == modname;
            });
            if(exactMatches.Count > 1) {
                TILER2Plugin._logger.LogError("(Server requesting update) There are multiple config entries with the path \"" + modname + "/" + category + "/" + cfgname + "\"; this should never happen! Please report this as a bug.");
                return -1;
            } else if(exactMatches.Count == 0) {
                TILER2Plugin._logger.LogError("The server requested an update for a nonexistent config entry with the path \"" + modname + "/" + category + "/" + cfgname + "\". Make sure you're using the same mods AND mod versions as the server!");
                return -1;
            }

            var newVal = TomlTypeConverter.ConvertToValue(value, exactMatches[0].propType);
            if(!exactMatches[0].cachedValue.Equals(newVal)) {
                if(exactMatches[0].netMismatchCritical) {
                    TILER2Plugin._logger.LogError("CRITICAL MISMATCH on \"" + modname + "/" + category + "/" + cfgname + "\": Requested " + newVal.ToString() + " vs current " + exactMatches[0].cachedValue.ToString());
                    return -2;
                }
                exactMatches[0].OverrideProperty(newVal, silent);
                return 1;
            }
            return 0;
        }
    }
}
