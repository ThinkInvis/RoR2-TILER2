using UnityEngine;
using RoR2;
using BepInEx;
using System;
using System.Reflection;
using System.Linq;
using static TILER2.MiscUtil;
using R2API.Utils;
using R2API;
using UnityEngine.Networking;
using System.Collections.Generic;

namespace TILER2 {
    internal static class NetConfig {
        internal static GameObject netOrchPrefab;
        internal static GameObject netOrchestrator;
        

        private static readonly RoR2.ConVar.BoolConVar allowClientAICSet = new RoR2.ConVar.BoolConVar("aic_allowclientset", ConVarFlags.None, "false", "If true, clients may use the ConCmds aic_set or aic_settemp to temporarily set config values on the server. If false, aic_set and aic_settemp will not work for clients.");

        internal static void EnsureOrchestrator() {
            if(!NetworkServer.active) {
                Debug.LogError("TILER2: EnsureOrchestrator called on client");
            }
            if(!netOrchestrator) {
                netOrchestrator = UnityEngine.Object.Instantiate(netOrchPrefab);
                NetworkServer.Spawn(netOrchestrator);
            }
        }

        private static (List<AutoItemConfig> results, string errorMsg) GetAICFromPath(string path1, string path2, string path3) {
            var p1u = path1.ToUpper();
            var p2u = path2?.ToUpper();
            var p3u = path3?.ToUpper();

            List<AutoItemConfig> matchesLevel1 = new List<AutoItemConfig>(); //no enforced order, no enforced caps, partial matches
            List<AutoItemConfig> matchesLevel2 = new List<AutoItemConfig>(); //enforced order, no enforced caps, partial matches
            List<AutoItemConfig> matchesLevel3 = new List<AutoItemConfig>(); //enforced order, no enforced caps, full matches
            List<AutoItemConfig> matchesLevel4 = new List<AutoItemConfig>(); //enforced order, enforced caps, full matches

            AutoItemConfig.instances.ForEach(x => {
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
                Debug.LogError("TILER2: There are multiple config entries with the path \"" + matchesLevel4[0].readablePath + "\"; this should never happen! Please report this as a bug.");
                return (matchesLevel4, "multiple level 4 matches");
            }
        }

        [ConCommand(commandName = "aic_checkrespond", flags = ConVarFlags.ExecuteOnServer)]
        public static void ConCmdAICCheckRespond(ConCommandArgs args) {
            EnsureOrchestrator();
            NetConfigOrchestrator.instance.CheckRespond(args.sender.connectionToClient, args[0], args[1]);
        }

        [ConCommand(commandName = "aic_get", helpText = "Prints an ingame value managed by TILER2.AutoItemConfig to console.")]
        public static void ConCmdAICGet(ConCommandArgs args) {
            var wrongArgsPre = "TILER2: ConCmd aic_get was used with bad arguments (";
            var usagePost = ").\nUsage: aic_get \"path1\" \"optional path2\" \"optional path3\". Path matches mod name, config category, and config name, in that order.";

            if(args.Count < 1) {
                Debug.LogWarning(wrongArgsPre + "not enough arguments" + usagePost);
                return;
            }
            if(args.Count > 3) {
                Debug.LogWarning(wrongArgsPre + "too many arguments" + usagePost);
                return;
            }

            var (matches, errmsg) = GetAICFromPath(args[0], (args.Count > 1) ? args[1] : null, (args.Count > 2) ? args[2] : null);

            if(errmsg != null) {
                if(matches != null)
                    Debug.Log("The following config settings match that path:\n" + String.Join(", ", matches.Select(x => "\"" + x.readablePath + "\"")));
                else
                    Debug.Log("There are no config settings with complete nor partial matches for that path.");
                return;
            }

            Debug.Log("\"" + matches[0].readablePath + "\" (" + matches[0].propType.Name + "): " + (matches[0].configEntry.Description?.Description ?? "[no description]") + "\nCurrent value: " + matches[0].cachedValue.ToString());
        }

        private static void AICSet(ConCommandArgs args, bool isTemporary) {
            var targetCmd = isTemporary ? "aic_settemp" : "aic_set";

            var errorPre = "TILER2: ConCmd " + targetCmd + " failed (";
            var usagePost = ").\nUsage: " + targetCmd + " \"path1\" \"optional path2\" \"optional path3\" newValue. Path matches mod name, config category, and config name, in that order.";

            if(args.Count < 2) {
                NetConfigOrchestrator.SendConMsg(args.sender, errorPre + "not enough arguments" + usagePost, 1);
                return;
            }
            if(args.Count > 4) {
                NetConfigOrchestrator.SendConMsg(args.sender, errorPre + "too many arguments" + usagePost, 1);
                return;
            }
            
            var (matches, errmsg) = GetAICFromPath(args[0], (args.Count > 2) ? args[1] : null, (args.Count > 3) ? args[2] : null);

            if(errmsg != null) {
                errmsg += ").";
                if(matches != null) {
                    errmsg += "\nThe following config settings have a matching path: " + String.Join(", ", matches.Select(x => "\"" + x.readablePath + "\""));
                }
                NetConfigOrchestrator.SendConMsg(args.sender, errorPre + errmsg, 1);
                return;
            }

            var convStr = args[args.Count - 1];
            
            object convObj;
            try {
                convObj = BepInEx.Configuration.TomlTypeConverter.ConvertToValue(convStr, matches[0].propType);
            } catch {
                NetConfigOrchestrator.SendConMsg(args.sender, errorPre + "(can't convert argument 2 'newValue' to the target config type, " + matches[0].propType.Name + ").", 1);
                return;
            }

            if(!isTemporary) {
                matches[0].configEntry.BoxedValue = convObj;
                if(!matches[0].configEntry.ConfigFile.SaveOnConfigSet) matches[0].configEntry.ConfigFile.Save();
            } else matches[0].OverrideProperty(convObj);
            if(args.sender && !args.sender.hasAuthority) Debug.Log("TILER2: ConCmd " + targetCmd + " from client " + args.sender.userName + " passed. Changed config setting " + matches[0].readablePath + " to " + convObj.ToString());
            NetConfigOrchestrator.SendConMsg(args.sender, "TILER2: ConCmd " + targetCmd + " successfully updated config entry!", 0);
        }

        [ConCommand(commandName = "aic_set", helpText = "While on the main menu, in singleplayer, or hosting a server: permanently override an ingame value managed by TILER2.AutoItemConfig. While non-host: attempts to call aic_settemp on the server instead.")]
        public static void ConCmdAICSet(ConCommandArgs args) {
            //todo: don't reroute AllowNetMismatch, add a SetTempLocal cmd?
            if((args.sender && !args.sender.isServer)) {
                RoR2.Console.instance.RunClientCmd(args.sender, "aic_settemp", args.userArgs.ToArray());
                return;
            } else AICSet(args, false);
        }

        [ConCommand(commandName = "aic_settemp", flags = ConVarFlags.ExecuteOnServer, helpText = "While a run is ongoing: temporarily override an ingame value managed by TILER2.AutoItemConfig. This will last until the end of the run. Use by non-host players can be blocked by the host using the ConVar aic_allowclientset.")]
        public static void ConCmdAICSetTemp(ConCommandArgs args) {
            EnsureOrchestrator();

            #if !DEBUG
            if((!args.sender.hasAuthority) && !allowClientAICSet.value) {
                Debug.LogWarning("TILER2: Client " + args.sender.userName + " tried to use ConCmd aic_settemp, but ConVar aic_allowclientset is set to false. DO NOT change this convar to true, unless you trust everyone who is in or may later join the server; doing so will allow them to temporarily change some config settings.");
                NetConfigOrchestrator.SendConMsg(args.sender, "TILER2: ConCmd aic_settemp cannot be used on this server by anyone other than the host.", 1);
                return;
            }
            #endif

            AICSet(args, true);
        }

        [ConCommand(commandName = "aic", helpText = "Routes to other AutoItemConfig commands (aic_get, aic_set, aic_settemp). For when you forget the underscore.")]
        public static void ConCmdAIC(ConCommandArgs args) {
            if(args.Count == 0) {
                Debug.LogWarning("TILER2: ConCmd aic was not passed enough arguments (needs at least 1 to determine which command to route to).");
                return;
            }
            string cmdToCall = null;
            if(args[0].ToUpper() == "GET") cmdToCall = "aic_get";
            else if(args[0].ToUpper() == "SET") cmdToCall = "aic_set";
            else if(args[0].ToUpper() == "SETTEMP") cmdToCall = "aic_settemp";
            else {
                Debug.LogWarning("TILER2: ConCmd aic_" + args[0] + " does not exist. Valid commands include: aic_get, aic_set, aic_settemp.");
                return;
            }
            RoR2.Console.instance.RunClientCmd(args.sender, cmdToCall, args.userArgs.Skip(1).ToArray());
        }

        /* just in case manual checking ever becomes necessary. TODO: rate limiting?
        [ConCommand(commandName = "AIC_Check", flags = ConVarFlags.ExecuteOnServer, helpText = "Sends a request to the server for a check for config mismatches. Performed automatically during connection.")]
        public static void ConCmdAICCheck(ConCommandArgs args) {
            EnsureOrchestrator();

            if(args.sender.hasAuthority) {
                Debug.LogWarning("TILER2: ConCmd AIC_Check was used directly by server (no-op).");
                return;
            }

            NetOrchestrator.AICSyncAllToOne(args.sender.connectionToClient);
        }*/
    }
    

    internal class NetConfigOrchestrator : NetworkBehaviour {
        internal static NetConfigOrchestrator instance;

        [System.Diagnostics.CodeAnalysis.SuppressMessage("Code Quality", "IDE0051:Remove unused private members", Justification = "Used by UnityEngine")]
        private void Awake() {
            instance = this;
        }

        internal static void SendConMsg(NetworkUser user, string msg, int severity = 0) {
            if(user == null || user.hasAuthority)
                ConMsg(msg, severity);
            else
                instance.TargetConMsg(user.connectionToClient, msg, severity);
        }

        [TargetRpc]
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Style", "IDE0060:Remove unused parameter", Justification = "Target param is required by UNetWeaver")]
        private void TargetConMsg(NetworkConnection target, string msg, int severity) {
            ConMsg(msg, severity);
        }

        private static void ConMsg(string msg, int severity) {
            if(severity == 2)
                Debug.LogError(msg);
            else if(severity == 1)
                Debug.LogWarning(msg);
            else
                Debug.Log(msg);
        }

        [Server]
        internal static void ServerSendGlobalChatMsg(string msg) {
            instance.RpcGlobalChatMsg(msg);
        }

        [ClientRpc]
        internal void RpcGlobalChatMsg(string msg) {
            Chat.AddMessage(msg);
        }
        
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Code Quality", "IDE0051:Remove unused private members", Justification = "Used by UnityEngine")]
        private void Update() {
            if(!TILER2Plugin.gCfgMismatchTimeout.Value || !NetworkServer.active) return;
            connectionsToCheck.ForEach(x => {
                x.timeRemaining -= Time.unscaledDeltaTime;
                if(x.timeRemaining <= 0f) {
                    Debug.LogWarning("TILER2: Connection " + x.connection.connectionId + " took too long to respond to config check request, kicking");
                    RoR2.Networking.GameNetworkManager.singleton.ServerKickClient(x.connection, RoR2.Networking.GameNetworkManager.KickReason.Unspecified);
                    x.connection.Disconnect();
                }
            });

            connectionsToCheck.RemoveAll(x => x.timeRemaining <= 0f);
        }

        private class WaitingConnCheck {
            public NetworkConnection connection;
            public string password;
            public float timeRemaining;
        }
        private const float connCheckWaitTime = 30f;
        private static readonly List<WaitingConnCheck> connectionsToCheck = new List<WaitingConnCheck>();
        internal static readonly List<NetworkConnection> checkedConnections = new List<NetworkConnection>();

        internal void CheckRespond(NetworkConnection conn, string password, string result) {
            var match = connectionsToCheck.Find(x => x.connection == conn && x.password == password);
            if(match == null) {
                Debug.LogError("TILER2: received config check response from unregistered connection!");
                return;
            }
            if(result == "PASS") {
                Debug.Log("TILER2: Connection " + match.connection.connectionId + " passed config check");
                connectionsToCheck.Remove(match);
            } else if(result == "FAIL"){
                if(TILER2Plugin.gCfgMismatchKick.Value) {
                    Debug.LogWarning("TILER2: Connection " + match.connection.connectionId + " failed config check, kicking");
                    RoR2.Networking.GameNetworkManager.singleton.ServerKickClient(match.connection, RoR2.Networking.GameNetworkManager.KickReason.Unspecified);
                } else {
                    Debug.LogWarning("TILER2: Connection " + match.connection.connectionId + " failed config check");
                }
                connectionsToCheck.Remove(match);
            } else {
                Debug.LogError("TILER2: POSSIBLE SECURITY ISSUE: Received registered connection and correct password in CheckRespond, but result is not valid");
            }
        }

        internal static void AICSyncAllToOne(NetworkConnection conn) {
            var validInsts = AutoItemConfig.instances.Where(x => !x.allowNetMismatch);
            var serValues = new List<string>();
            var modnames = new List<string>();
            var categories = new List<string>();
            var cfgnames = new List<string>();
            foreach(var i in validInsts) {
                serValues.Add(BepInEx.Configuration.TomlTypeConverter.ConvertToString(i.cachedValue, i.propType));
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
            for(var i = 0; i < modnames.Length; i++) {
                var res = CliAICSync(modnames[i], categories[i], cfgnames[i], values[i], true);
                if(res == 1) matches++;
                else if(res == -1) foundCrit = true;
            }
            if(foundCrit == true) {
                Debug.LogError("TILER2: The above no-mismatch-allowed config entries are different on the server, and they cannot be changed while the game is running. Please close the game, change these entries to match the server's, then restart and rejoin the server.");
                RoR2.Console.instance.SubmitCmd(NetworkUser.readOnlyInstancesList[0], "AIC_CheckRespond " + password + " FAIL");
                return;
            }
            else if(matches > 0) Chat.AddMessage("Synced <color=#ffff00>"+ matches +" setting changes</color> from the server temporarily. Check the console for details.");
            RoR2.Console.instance.SubmitCmd(NetworkUser.readOnlyInstancesList[0], "AIC_CheckRespond " + password + " PASS");
        }

        [Server]
        internal void ServerAICSyncOneToAll(AutoItemConfig targetConfig, object newValue) {
            foreach(var user in NetworkUser.readOnlyInstancesList) {
                if(user.hasAuthority || (user.connectionToClient != null && Util.ConnectionIsLocal(user.connectionToClient))) continue;
                TargetAICSyncOneToAll(user.connectionToClient, targetConfig.modName, targetConfig.configEntry.Definition.Section, targetConfig.configEntry.Definition.Key,
                    BepInEx.Configuration.TomlTypeConverter.ConvertToString(newValue, targetConfig.propType));
            }
        }

        [TargetRpc]
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Style", "IDE0060:Remove unused parameter", Justification = "Target param is required by UNetWeaver")]
        private void TargetAICSyncOneToAll(NetworkConnection target, string modname, string category, string cfgname, string value) {
            CliAICSync(modname, category, cfgname, value, false);
        }

        [Client]
        private int CliAICSync(string modname, string category, string cfgname, string value, bool silent) {
            var exactMatches = AutoItemConfig.instances.FindAll(x => {
                return x.configEntry.Definition.Key == cfgname
                && x.configEntry.Definition.Section == category
                && x.modName == modname;
            });
            if(exactMatches.Count > 1) {
                Debug.LogError("TILER2: (Server requesting update) There are multiple config entries with the path \"" + modname + "/" + category + "/" + cfgname + "\"; this should never happen! Please report this as a bug.");
                return 0;
            } else if(exactMatches.Count == 0) {
                Debug.LogError("TILER2: The server requested an update for a nonexistent config entry with the path \"" + modname + "/" + category + "/" + cfgname + "\". Make sure you're using the same mods as the server!");
                return 0;
            }

            var newVal = BepInEx.Configuration.TomlTypeConverter.ConvertToValue(value, exactMatches[0].propType);
            if(!exactMatches[0].cachedValue.Equals(newVal)) {
                if(exactMatches[0].netMismatchCritical) {
                    Debug.LogError("CRITICAL MISMATCH on \"" + modname + "/" + category + "/" + cfgname + "\": Requested " + newVal.ToString() + " vs current " + exactMatches[0].cachedValue.ToString());
                    return -1;
                }
                exactMatches[0].OverrideProperty(newVal, silent);
                return 1;
            }
            return 0;
        }
    }
}
